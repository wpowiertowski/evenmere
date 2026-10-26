using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The three buttons beside the HUD that open the research, civics and statistics panels. They sit at the top-left just under
/// the HUD bar, where an open panel (docked right) never covers them. Each button toggles its panel and closes the
/// others, so at most one panel is open. The buttons take keyboard and gamepad focus like the HUD's own, so the
/// panels stay reachable from the HUD focus chain.
/// </summary>
public sealed partial class PanelToggles : HBoxContainer
{
    private SimHost? _host;
    private ContentDb? _builtFor;
    private Control? _hudBar;
    private Button? _researchButton;
    private Button? _cultureButton;
    private Button? _statsButton;

    /// <summary>Gets or sets the path to the HUD bar, whose bottom edge the buttons sit under.</summary>
    [Export]
    public NodePath HudBarPath { get; set; } = new("../Hud/HudRoot/Margin/Bar");

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the research panel, relative to this node.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, relative to this node.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <summary>Gets or sets the path to the statistics panel, relative to this node.</summary>
    [Export]
    public NodePath StatsPath { get; set; } = new("../StatsPanel");

    /// <summary>
    /// Toggles the statistics panel, closing the research and civics panels when it opens. The debug stats flag
    /// calls this so screenshots travel the same path as a button press.
    /// </summary>
    public void ToggleStats()
    {
        Control? stats = GetNodeOrNull<Control>(StatsPath);
        if (stats is null)
        {
            return;
        }

        bool show = !stats.Visible;
        if (show)
        {
            if (GetNodeOrNull<Control>(ResearchPath) is { } research)
            {
                research.Visible = false;
            }

            if (GetNodeOrNull<Control>(CulturePath) is { } culture)
            {
                culture.Visible = false;
            }
        }

        stats.Visible = show;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0f;
        AnchorRight = 0f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 16f;
        OffsetTop = 128f;
        OffsetRight = 500f;
        OffsetBottom = 200f;
        AddThemeConstantOverride("separation", 8);

        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] toggles: no SimHost at '{HostPath}'; the panel buttons stay empty.");
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The buttons sit just under the HUD bar's actual bottom edge, so a taller bar (more tiers, longer
        // workforce text) can never cover them. Falls back to the placed offsets while the bar is missing.
        if (_hudBar is null)
        {
            _hudBar = GetNodeOrNull<Control>(HudBarPath);
        }

        if (_hudBar is not null)
        {
            float top = _hudBar.GetGlobalRect().End.Y + 8f;
            if (Math.Abs(OffsetTop - top) > 0.5f)
            {
                OffsetTop = top;
                OffsetBottom = top + 72f;
            }
        }

        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            Build(host);
        }
    }

    private void Build(SimHost host)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _builtFor = host.Content;
        var strings = new UiStrings(host.Locale);
        Control? research = GetNodeOrNull<Control>(ResearchPath);
        Control? culture = GetNodeOrNull<Control>(CulturePath);
        Control? stats = GetNodeOrNull<Control>(StatsPath);
        _researchButton = MakeButton(
            strings.Get("ui.research.open"),
            strings.Get("ui.research.open.tip"),
            research,
            culture,
            stats);
        AddChild(_researchButton);

        _cultureButton = MakeButton(
            strings.Get("ui.culture.open"),
            strings.Get("ui.culture.open.tip"),
            culture,
            research,
            stats);
        AddChild(_cultureButton);

        _statsButton = MakeButton(
            strings.Get("ui.stats.open"),
            strings.Get("ui.stats.open.tip"),
            stats,
            research,
            culture);
        AddChild(_statsButton);
    }

    private static Button MakeButton(string text, string tip, Control? panel, params Control?[] others)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tip,
            FocusMode = FocusModeEnum.All,
        };

        if (panel is not null)
        {
            button.Pressed += () =>
            {
                bool show = !panel.Visible;
                if (show)
                {
                    foreach (Control? other in others)
                    {
                        if (other is not null)
                        {
                            other.Visible = false;
                        }
                    }
                }

                panel.Visible = show;
            };
        }
        else
        {
            button.Disabled = true;
        }

        return button;
    }
}
