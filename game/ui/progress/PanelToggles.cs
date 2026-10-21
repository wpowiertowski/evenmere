using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The two buttons beside the HUD that open the research and civics panels. They sit at the top-left just under
/// the HUD bar, where an open panel (docked right) never covers them. Each button toggles its panel and closes the
/// other, so at most one panel is open. The buttons take keyboard and gamepad focus like the HUD's own, so the
/// panels stay reachable from the HUD focus chain.
/// </summary>
public sealed partial class PanelToggles : HBoxContainer
{
    private SimHost? _host;
    private ContentDb? _builtFor;
    private Button? _researchButton;
    private Button? _cultureButton;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the research panel, relative to this node.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, relative to this node.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

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
        _researchButton = MakeButton(
            strings.Get("ui.research.open"),
            strings.Get("ui.research.open.tip"),
            research,
            culture);
        AddChild(_researchButton);

        _cultureButton = MakeButton(
            strings.Get("ui.culture.open"),
            strings.Get("ui.culture.open.tip"),
            culture,
            research);
        AddChild(_cultureButton);
    }

    private static Button MakeButton(string text, string tip, Control? panel, Control? other)
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
                if (show && other is not null)
                {
                    other.Visible = false;
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
