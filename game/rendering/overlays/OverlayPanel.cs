using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Godot;

namespace Evenmere.Game.Rendering.Overlays;

/// <summary>
/// The overlay picker: a right-docked panel with one focusable button per unlocked layer, grouped under small
/// headers, and a legend for the shown one. Only layers whose building or crop is unlocked are listed, so locked
/// content never spoils a future era; the list refreshes about once a second while open, so newly unlocked layers
/// appear without reopening. Every button takes keyboard and gamepad focus through the built-in
/// <see cref="Godot.Control.FocusModeEnum.All"/> (no new input actions) and the scroll follows focus, so the panel
/// stays reachable with the <c>ui_*</c> actions at 1280 × 800. It is opened by <see cref="Toggle"/>, which the
/// HUD-adjacent toggles call.
/// </summary>
public sealed partial class OverlayPanel : PanelContainer
{
    private const float PanelWidth = 300f;
    private const float PanelMargin = 16f;
    private const float PanelTop = 216f;

    // The Menu button sits bottom-right; ending the panel above it keeps every layer button clear of it.
    private const float MenuClearance = 72f;

    private const double RefreshSeconds = 1.0;

    private SimHost? _host;
    private ContentDb? _builtFor;
    private OverlayRenderer? _renderer;
    private VBoxContainer? _groups;
    private VBoxContainer? _legend;
    private UiStrings? _strings;
    private OverlaySpec[] _offered = [];
    private readonly List<(Button Button, OverlaySpec Spec)> _options = [];
    private OverlaySpec? _shown;
    private double _refreshIn;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="OverlayRenderer"/> node, relative to this node.</summary>
    [Export]
    public NodePath RendererPath { get; set; } = new("../Overlays");

    /// <summary>Opens the panel if it is closed and closes it if it is open.</summary>
    public void Toggle() => Visible = !Visible;

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        Visible = false;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _host ??= GetNodeOrNull<SimHost>(HostPath);
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        _refreshIn -= delta;
        if (!ReferenceEquals(_builtFor, host.Content) || _refreshIn <= 0)
        {
            Rebuild(host);
            _refreshIn = RefreshSeconds;
        }
    }

    private void Rebuild(SimHost host)
    {
        _builtFor = host.Content;
        _host = host;
        _strings = new UiStrings(host.Locale);
        var model = new OverlayViewModel(host.Content, host.Query.Overlay);
        OverlaySpec[] offered = model.Available(host.Query.Progression.IsUnlocked);
        if (!offered.SequenceEqual(_offered))
        {
            _offered = offered;
            _options.Clear();
            RebuildOptions();
        }

        RefreshLegend();
    }

    private void EnsureLayout()
    {
        if (_groups is not null)
        {
            return;
        }

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        margin.AddChild(stack);

        var title = new Label { Text = _strings?.Get("ui.overlays.title") ?? "Overlays" };
        stack.AddChild(title);

        var scroll = new ScrollContainer
        {
            FollowFocus = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        stack.AddChild(scroll);

        var scrolled = new VBoxContainer();
        scrolled.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(scrolled);

        _groups = new VBoxContainer();
        _groups.AddThemeConstantOverride("separation", 6);
        scrolled.AddChild(_groups);

        _legend = new VBoxContainer();
        _legend.AddThemeConstantOverride("separation", 2);
        scrolled.AddChild(_legend);
    }

    private void RebuildOptions()
    {
        EnsureLayout();
        if (_groups is null || _strings is null)
        {
            return;
        }

        foreach (Node child in _groups.GetChildren())
        {
            _groups.RemoveChild(child);
            child.QueueFree();
        }

        foreach (OverlayGroup group in Enum.GetValues<OverlayGroup>())
        {
            OverlaySpec[] specs = [.. _offered.Where(s => s.Group() == group)];
            if (specs.Length == 0)
            {
                continue;
            }

            _groups.AddChild(new Label { Text = _strings.Get(OverlayGroups.Key(group)) });
            foreach (OverlaySpec spec in specs)
            {
                OverlaySpec captured = spec;
                var button = new Button
                {
                    Text = _strings.Get(captured.LabelKey()),
                    FocusMode = FocusModeEnum.All,
                    ToggleMode = true,
                    ButtonPressed = _shown == captured,
                };
                button.Pressed += () => Show(captured);
                _groups.AddChild(button);
                _options.Add((button, captured));
            }
        }

    }

    private void Show(OverlaySpec spec)
    {
        _renderer ??= GetNodeOrNull<OverlayRenderer>(RendererPath);
        if (_renderer is null)
        {
            GD.PushError("[Evenmere] overlays: no OverlayRenderer at '" + RendererPath + "'; the buttons stay empty.");
            return;
        }

        if (_shown == spec)
        {
            _renderer.HideLayer();
            _shown = null;
        }
        else
        {
            _renderer.Show(spec);
            _shown = spec;
        }

        foreach ((Button button, OverlaySpec option) in _options)
        {
            button.ButtonPressed = _shown == option;
        }

        RefreshLegend();
    }

    private void RefreshLegend()
    {
        if (_legend is null)
        {
            return;
        }

        foreach (Node child in _legend.GetChildren())
        {
            _legend.RemoveChild(child);
            child.QueueFree();
        }

        if (_shown is not { } spec || _strings is null)
        {
            return;
        }

        foreach (OverlayLegendEntry entry in OverlayPalette.LegendFor(spec))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            if (entry.IconGoodId is { } icon && IconLibrary.LoadGood(icon) is { } texture)
            {
                row.AddChild(new TextureRect
                {
                    Texture = texture,
                    CustomMinimumSize = new Vector2(16, 16),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                });
            }

            row.AddChild(new ColorRect
            {
                Color = new Color(entry.Swatch.R, entry.Swatch.G, entry.Swatch.B, entry.Swatch.A),
                CustomMinimumSize = new Vector2(16, 16),
            });
            row.AddChild(new Label { Text = _strings.Get(entry.LabelKey) });
            _legend.AddChild(row);
        }
    }

    private void Place()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = -PanelWidth - PanelMargin;
        OffsetRight = -PanelMargin;
        OffsetTop = PanelTop;
        OffsetBottom = -MenuClearance;
        MouseFilter = MouseFilterEnum.Stop;
    }
}
