using Godot;

namespace Evenmere.Game.Ui.Research;

/// <summary>
/// One tech-tree card: a focusable panel with the name button (click for the target, Shift-click for the
/// queue), the state, cost and progress, the inspiration sentence and the unlock list with glyphs. The graph
/// view sets the explicit focus neighbours, so gamepad focus follows the tree's edges through the built-in
/// <c>ui_*</c> actions; the per-card queue button keeps queueing reachable without Shift-click.
/// </summary>
public sealed partial class TreeNodeView : PanelContainer
{
    /// <summary>Raised when the name button is pressed: make this node the research target.</summary>
    public event Action<int>? TargetRequested;

    /// <summary>Raised when the queue button is pressed or the name is Shift-clicked: append this node to the queue.</summary>
    public event Action<int>? QueueRequested;

    /// <summary>Gets or sets the node's index in the graph's node list.</summary>
    public int NodeIndex { get; set; } = -1;

    /// <summary>Gets the name button, whose focus neighbours the graph wires to the tree's edges.</summary>
    public Button MainButton => _main ?? throw new InvalidOperationException("SetNode has not run yet.");

    private static readonly Color _ink = new(0.15f, 0.17f, 0.20f);
    private static readonly Color _muted = new(0.37f, 0.42f, 0.46f);
    private static readonly Color _sea = new(0.12f, 0.35f, 0.47f);
    private static readonly Color _dim = new(0.55f, 0.57f, 0.60f);
    private static readonly Color _gold = new(0.69f, 0.54f, 0.18f);
    private static readonly Dictionary<(GraphNodeState State, bool Gateway), StyleBoxFlat> _cards = new();

    private VBoxContainer? _box;
    private Button? _main;
    private Label? _state;
    private Label? _cost;
    private ProgressBar? _bar;
    private Label? _inspiration;
    private Label? _unlocks;
    private Button? _queue;

    /// <summary>Shows the node's data on the card, building the chrome on first use.</summary>
    /// <param name="node">The node to show.</param>
    public void SetNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureChrome();

        NodeIndex = node.Index;
        _main!.Text = node.Name;
        _main.TooltipText = node.Tooltip;
        _state!.Text = node.StateText;
        _state.AddThemeColorOverride("font_color", node.State == GraphNodeState.Locked ? _dim : _muted);
        _cost!.Text = node.CostText;
        _cost.AddThemeColorOverride("font_color", node.State == GraphNodeState.Locked ? _dim : _ink);
        _bar!.Value = node.Fraction * 1000.0;

        string badge = node.InspiredBadge;
        _inspiration!.Text = badge.Length > 0 ? $"✦ {badge} · {node.InspirationText}" : node.InspirationText;
        _inspiration.AddThemeColorOverride("font_color", node.Inspired ? _sea : _muted);

        var parts = new List<string>(node.Unlocks.Count);
        foreach (GraphUnlock unlock in node.Unlocks)
        {
            parts.Add(unlock.Glyph.Length > 0 ? $"{unlock.Glyph}  {unlock.Text}" : unlock.Text);
        }

        _unlocks!.Text = string.Join(", ", parts);
        _queue!.Visible = node.State is not GraphNodeState.Done and not GraphNodeState.Target;

        AddThemeStyleboxOverride("panel", CardFor(node.State, node.Gateway));
    }

    private void EnsureChrome()
    {
        if (_box is not null)
        {
            return;
        }

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        AddChild(box);
        _box = box;

        var main = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        main.Pressed += () => TargetRequested?.Invoke(NodeIndex);
        main.GuiInput += OnMainInput;
        box.AddChild(main);
        _main = main;

        _state = Small(box);
        _cost = Small(box);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1000,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 12),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddChild(bar);
        _bar = bar;

        _inspiration = Small(box);
        _inspiration.AutowrapMode = TextServer.AutowrapMode.Off;
        _inspiration.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _inspiration.ClipText = true;
        _unlocks = Small(box);
        _unlocks.AutowrapMode = TextServer.AutowrapMode.Off;
        _unlocks.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _unlocks.ClipText = true;

        var queue = new Button
        {
            Text = "+",
            FocusMode = Control.FocusModeEnum.All,
        };
        queue.Pressed += () => QueueRequested?.Invoke(NodeIndex);
        box.AddChild(queue);
        _queue = queue;
    }

    private static Label Small(VBoxContainer box)
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(label);
        return label;
    }

    private void OnMainInput(InputEvent @event)
    {
        // Mouse only: Shift-click queues, like the list rows. Keyboard and gamepad confirmation arrives
        // through Pressed (sets the target), and the card's own queue button queues without Shift.
        if (@event is InputEventMouseButton pressed
            && pressed.Pressed
            && pressed.ButtonIndex == MouseButton.Left
            && Input.IsKeyPressed(Key.Shift))
        {
            QueueRequested?.Invoke(NodeIndex);
            _main!.AcceptEvent();
        }
    }

    private static StyleBoxFlat CardFor(GraphNodeState state, bool gateway)
    {
        if (_cards.TryGetValue((state, gateway), out StyleBoxFlat? cached))
        {
            return cached;
        }

        Color border = state switch
        {
            GraphNodeState.Done => new Color(0.25f, 0.55f, 0.30f),
            GraphNodeState.Target => new Color(0.12f, 0.35f, 0.47f),
            GraphNodeState.Queued => new Color(0.55f, 0.45f, 0.25f),
            GraphNodeState.Locked => new Color(0.70f, 0.70f, 0.68f),
            _ => new Color(0.60f, 0.62f, 0.64f),
        };

        Color fill = state switch
        {
            GraphNodeState.Done => new Color(0.82f, 0.90f, 0.80f),
            GraphNodeState.Target => new Color(0.85f, 0.92f, 0.96f),
            GraphNodeState.Queued => new Color(0.94f, 0.90f, 0.78f),
            GraphNodeState.Locked => new Color(0.90f, 0.90f, 0.88f),
            _ => new Color(0.96f, 0.96f, 0.94f),
        };

        if (gateway)
        {
            border = _gold;
        }

        var card = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
        };
        card.SetBorderWidthAll(gateway || state == GraphNodeState.Target ? 3 : 2);
        card.SetCornerRadiusAll(8);
        card.ContentMarginLeft = 8;
        card.ContentMarginRight = 8;
        card.ContentMarginTop = 8;
        card.ContentMarginBottom = 8;
        _cards[(state, gateway)] = card;
        return card;
    }
}
