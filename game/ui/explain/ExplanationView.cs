using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.Explain;

/// <summary>
/// Renders an <see cref="Explanation"/> tree: one row per line with indentation,
/// a status glyph and the locale text with typed arguments formatted by
/// <see cref="ExplainFormat"/>. Building subjects are buttons that raise
/// <see cref="BuildingLinkActivated"/> (the inspector focuses the camera and opens
/// that building); good subjects raise <see cref="GoodLinkActivated"/> and the view
/// pushes the new good's explanation onto a small back stack.
/// </summary>
public sealed partial class ExplanationView : VBoxContainer
{
    private const float IndentPerLevel = 18f;

    private readonly List<Explanation> _stack = new();
    private readonly List<string> _titles = new();

    private ContentDb? _content;
    private UiStrings? _strings;
    private Func<GoodId, Explanation>? _explainGood;
    private Func<EntityId, string>? _buildingName;
    private Func<EntityId, int>? _storehouseReach;
    private HBoxContainer? _bar;
    private Button? _backButton;
    private Label? _titleLabel;
    private VBoxContainer? _rows;

    /// <summary>Raised when a building subject link is clicked.</summary>
    public event Action<EntityId>? BuildingLinkActivated;

    /// <summary>Raised when a good subject link is clicked (after the view already pushed the good).</summary>
    public event Action<GoodId>? GoodLinkActivated;

    /// <summary>Gets or sets how many levels of children are shown, where 0 is the root alone. The default shows everything.</summary>
    public int MaxDepth { get; set; } = int.MaxValue;

    /// <summary>Gets whether the back stack can go back.</summary>
    public bool CanGoBack => _stack.Count > 1;

    /// <summary>Wires the content, text and lookups. Must be called before <see cref="ShowRoot"/>.</summary>
    /// <param name="content">The content database, for ID names.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="explainGood">Looks up a good's explanation (usually <c>SimHost.Query.Explain.ExplainGood</c>).</param>
    /// <param name="buildingName">Names a building for its subject-link button; <see langword="null"/> for the raw ID.</param>
    /// <param name="storehouseReach">Reads a storehouse's reach in tiles from content; <see langword="null"/> for the raw cost.</param>
    public void Setup(ContentDb content, UiStrings strings, Func<GoodId, Explanation> explainGood, Func<EntityId, string>? buildingName = null, Func<EntityId, int>? storehouseReach = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(explainGood);
        if (ReferenceEquals(_content, content)
            && ReferenceEquals(_strings, strings)
            && _explainGood == explainGood
            && _buildingName == buildingName
            && _storehouseReach == storehouseReach)
        {
            // Idempotent refresh (M2-26 focus walk): the same lookups keep their live controls instead of
            // rebuilding them, so focus never sits on a freed button and repeated refreshes cost nothing.
            return;
        }

        _content = content;
        _strings = strings;
        _explainGood = explainGood;
        _buildingName = buildingName;
        _storehouseReach = storehouseReach;
        EnsureChrome();
        Rebuild();
    }

    /// <summary>Refreshes the lookups after a new world starts, keeping the same visible explanation when possible.</summary>
    /// <param name="content">The new content database.</param>
    /// <param name="strings">The new display text.</param>
    /// <param name="explainGood">The new good-explanation lookup.</param>
    /// <param name="buildingName">Names a building for its subject-link button; <see langword="null"/> for the raw ID.</param>
    /// <param name="storehouseReach">Reads a storehouse's reach in tiles from content; <see langword="null"/> for the raw cost.</param>
    public void ResetContent(ContentDb content, UiStrings strings, Func<GoodId, Explanation> explainGood, Func<EntityId, string>? buildingName = null, Func<EntityId, int>? storehouseReach = null)
    {
        _content = content;
        _strings = strings;
        _explainGood = explainGood;
        _buildingName = buildingName;
        _storehouseReach = storehouseReach;
        Rebuild();
    }

    /// <summary>Shows an explanation, clearing the back stack.</summary>
    /// <param name="explanation">The explanation.</param>
    /// <param name="title">The title above the tree; empty hides it.</param>
    public void ShowRoot(Explanation explanation, string title = "")
    {
        ArgumentNullException.ThrowIfNull(explanation);
        if (_stack.Count == 1
            && string.Equals(_titles[0], title, StringComparison.Ordinal)
            && _stack[0].Equals(explanation))
        {
            // Idempotent refresh (M2-26 focus walk): showing the same explanation keeps its live controls instead
            // of rebuilding them, so focus never sits on a freed button and repeated refreshes cost nothing.
            return;
        }

        _stack.Clear();
        _titles.Clear();
        _stack.Add(explanation);
        _titles.Add(title);
        Rebuild();
    }

    /// <summary>Shows a good's explanation, pushing it onto the back stack.</summary>
    /// <param name="good">The good.</param>
    public void ShowGood(GoodId good)
    {
        if (_explainGood is null)
        {
            return;
        }

        Push(_explainGood(good), string.Empty);
        GoodLinkActivated?.Invoke(good);
    }

    /// <summary>Goes back one step in the stack, when <see cref="CanGoBack"/>.</summary>
    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        _stack.RemoveAt(_stack.Count - 1);
        _titles.RemoveAt(_titles.Count - 1);
        Rebuild();
    }

    /// <summary>Clears the view.</summary>
    public void Clear()
    {
        _stack.Clear();
        _titles.Clear();
        Rebuild();
    }

    private void Push(Explanation explanation, string title)
    {
        _stack.Add(explanation);
        _titles.Add(title);
        // The HUD tooltip is a compact two-level view; its back stack stays small on purpose.
        const int MaxStack = 8;
        while (_stack.Count > MaxStack)
        {
            _stack.RemoveAt(0);
            _titles.RemoveAt(0);
        }

        Rebuild();
    }

    private void EnsureChrome()
    {
        if (_rows is not null)
        {
            return;
        }

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        AddChild(bar);
        _bar = bar;

        _backButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _backButton.Pressed += GoBack;
        bar.AddChild(_backButton);

        _titleLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        bar.AddChild(_titleLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(360f, 220f),
        };
        AddChild(scroll);

        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_rows);
    }

    private void Rebuild()
    {
        if (_rows is null || _backButton is null || _titleLabel is null || _bar is null)
        {
            return;
        }

        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        if (_stack.Count == 0 || _content is null || _strings is null)
        {
            _backButton.Visible = false;
            _titleLabel.Visible = false;
            return;
        }

        ContentDb content = _content;
        UiStrings strings = _strings;
        Explanation current = _stack[^1];
        string title = _titles[^1];

        // The bar holds only navigation: hide it when there is nothing to go back to and no title.
        _bar.Visible = CanGoBack || !string.IsNullOrEmpty(title);
        _backButton.Visible = CanGoBack;
        _backButton.Text = strings.Get("ui.inspector.back");
        _backButton.TooltipText = strings.Get("ui.inspector.back.tip");
        _titleLabel.Visible = !string.IsNullOrEmpty(title);
        _titleLabel.Text = title;

        AddLine(current.Root, 0, content, strings);
    }

    private void AddLine(ExplanationLine line, int depth, ContentDb content, UiStrings strings)
    {
        if (_rows is null)
        {
            return;
        }

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _rows.AddChild(row);

        var indent = new Control { CustomMinimumSize = new Vector2(depth * IndentPerLevel, 0f) };
        indent.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(indent);

        var icon = new Label { Text = ExplainFormat.IconForKey(line.Key) };
        icon.AddThemeFontSizeOverride("font_size", 15);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(icon);

        var text = new Label
        {
            Text = FormatText(line, content, strings),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        text.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(text);

        foreach (SubjectLink subject in line.Subjects)
        {
            Button? link = MakeLink(subject, content, strings);
            if (link is not null)
            {
                row.AddChild(link);
            }
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (ExplanationLine child in line.Children)
        {
            AddLine(child, depth + 1, content, strings);
        }
    }

    private string FormatText(ExplanationLine line, ContentDb content, UiStrings strings)
    {
        // Reach reads better as tiles of road than as raw cost units; the reach comes from content.
        if (string.Equals(line.Key, "explain.building.reach", StringComparison.Ordinal)
            && _storehouseReach is not null
            && line.Args.Length >= 2
            && line.Subjects.Length >= 1
            && line.Subjects[0].Kind == SubjectKind.Building)
        {
            int reach = _storehouseReach(line.Subjects[0].AsBuilding());
            if (reach > 0)
            {
                return ExplainFormat.FormatReach((int)line.Args[1].Value, reach, strings);
            }
        }

        return ExplainFormat.FormatLine(line, content, strings);
    }

    private Button? MakeLink(SubjectLink subject, ContentDb content, UiStrings strings)
    {
        switch (subject.Kind)
        {
            case SubjectKind.Building:
                {
                    EntityId id = subject.AsBuilding();
                    string name = _buildingName?.Invoke(id) ?? $"#{id.Value}";
                    var button = new Button
                    {
                        Text = name,
                        FocusMode = Control.FocusModeEnum.All,
                        TooltipText = strings.Format("ui.inspector.link.building.tip", ("name", name)),
                    };
                    button.Pressed += () => BuildingLinkActivated?.Invoke(id);
                    return button;
                }

            case SubjectKind.Good:
                {
                    GoodId good = subject.AsGood();
                    string name;
                    try
                    {
                        name = strings.GoodName(content.NameOf(good));
                    }
                    catch (Exception)
                    {
                        return null;
                    }

                    var button = new Button
                    {
                        Text = name,
                        FocusMode = Control.FocusModeEnum.All,
                        TooltipText = strings.Format("ui.inspector.link.good.tip", ("name", name)),
                    };
                    button.Pressed += () => ShowGood(good);
                    return button;
                }

            default:
                return null;
        }
    }
}
