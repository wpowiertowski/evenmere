using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Persistence;
using Godot;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The calm, dismissible note shown after a load whose <see cref="SaveReport"/> lists remaps, ruins or drops. Every
/// line comes from the report itself; the panel only adds the title and the close button. It stays hidden for clean
/// loads.
/// </summary>
public sealed partial class LoadReportNote : PanelContainer
{
    private Label? _title;
    private VBoxContainer? _lines;
    private Button? _close;

    /// <summary>Raised when the note is dismissed.</summary>
    public event Action? Dismissed;

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -240f;
        OffsetTop = -200f;
        OffsetRight = 240f;
        OffsetBottom = 200f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        CustomMinimumSize = new Vector2(480f, 0);
        Visible = false;
        Build();
    }

    /// <summary>Shows the report, or hides the note when the load was clean.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="report">What loading changed or dropped.</param>
    /// <returns><see langword="true"/> when the note is now visible.</returns>
    public bool ShowReport(UiStrings strings, SaveReport report)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(report);

        if (report.IsClean)
        {
            Visible = false;
            return false;
        }

        if (_title is not null)
        {
            _title.Text = strings.Get("ui.load.report.title");
        }

        if (_lines is not null)
        {
            foreach (Node child in _lines.GetChildren())
            {
                _lines.RemoveChild(child);
                child.QueueFree();
            }

            foreach (string line in AllLines(report))
            {
                var label = new Label
                {
                    Text = line,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                _lines.AddChild(label);
            }
        }

        if (_close is not null)
        {
            _close.Text = strings.Get("ui.load.report.close");
            _close.TooltipText = strings.Get("ui.load.report.close.tip");
        }

        Visible = true;
        _close?.GrabFocus();
        return true;
    }

    /// <summary>Hides the note.</summary>
    public void Dismiss()
    {
        Visible = false;
        Dismissed?.Invoke();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || @event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            Dismiss();
            GetViewport().SetInputAsHandled();
        }
    }

    private static IEnumerable<string> AllLines(SaveReport report)
    {
        foreach (string line in report.Remapped)
        {
            yield return line;
        }

        foreach (string line in report.Ruins)
        {
            yield return line;
        }

        foreach (string line in report.Dropped)
        {
            yield return line;
        }

        foreach (string line in report.Notes)
        {
            yield return line;
        }
    }

    private void Build()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        margin.AddChild(top);

        var title = new Label();
        top.AddChild(title);
        _title = title;

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 200f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(scroll);

        var lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lines.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(lines);
        _lines = lines;

        var bottom = new HBoxContainer();
        bottom.Alignment = BoxContainer.AlignmentMode.End;
        top.AddChild(bottom);

        var close = new Button { FocusMode = FocusModeEnum.All };
        close.Pressed += Dismiss;
        bottom.AddChild(close);
        _close = close;
    }
}
