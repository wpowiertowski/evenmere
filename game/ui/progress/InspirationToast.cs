using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Research;
using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// A small, calm toast that names a tech or civic whose inspiration just triggered. It shows for about five
/// seconds, replacing any toast already showing. M1-26 builds the shared notification feed later and can replace
/// this; until then the panels stay quiet and the toast lives on its own.
/// </summary>
public sealed partial class InspirationToast : PanelContainer
{
    private const double ShowSeconds = 5.0;

    private SimHost? _host;
    private ContentDb? _builtFor;
    private UiStrings? _strings;
    private Label? _label;
    private Godot.Timer? _timer;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = -260f;
        OffsetRight = 260f;
        OffsetTop = -120f;
        OffsetBottom = -64f;
        MouseFilter = MouseFilterEnum.Ignore;

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_label);

        _timer = new Godot.Timer { WaitTime = ShowSeconds, OneShot = true };
        _timer.Timeout += () => Visible = false;
        AddChild(_timer);

        Visible = false;

        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] toast: no SimHost at '{HostPath}'; inspirations stay silent.");
            return;
        }

        _host.EventsDrained += OnEvents;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
        }
    }

    /// <summary>Shows the toast with a ready-made text, for scripted runs and tests.</summary>
    /// <param name="text">The text to show.</param>
    public void ShowText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_label is null || _timer is null)
        {
            return;
        }

        _label.Text = text;
        Visible = true;
        _timer.Start();
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        SimHost? host = _host;
        if (host is null || !host.IsRunning)
        {
            return;
        }

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            _builtFor = host.Content;
            _strings = new UiStrings(host.Locale);
        }

        foreach (SimEvent @event in events)
        {
            if (@event is InspirationTriggered inspiration)
            {
                ShowInspiration(inspiration);
            }
        }
    }

    private void ShowInspiration(InspirationTriggered inspiration)
    {
        ContentDb? content = _builtFor;
        UiStrings? strings = _strings;
        if (content is null || strings is null)
        {
            return;
        }

        string name = inspiration.Tech is { } tech
            ? strings.Get($"tech.{content.Techs.NameOf(tech)}.name")
            : inspiration.Civic is { } civic
                ? strings.Get($"civic.{content.Civics.NameOf(civic)}.name")
                : strings.Get("ui.hud.unavailable");
        string key = inspiration.Tech is not null ? "ui.tree.toast.tech" : "ui.tree.toast.civic";
        ShowText(strings.Format(key, ("name", name)));
    }
}
