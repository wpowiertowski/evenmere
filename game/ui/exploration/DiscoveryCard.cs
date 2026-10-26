using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Exploration;
using Godot;

namespace Evenmere.Game.Ui.Exploration;

/// <summary>
/// The discovery card (M2-23): a calm card that opens when an expedition returns to a region holding a
/// discovery, naming it and previewing its effect. Names come from the
/// <c>discovery.&lt;kind&gt;.&lt;index&gt;</c> locale keys (M2-13b's <c>names.json</c>); until those land, a
/// generic kind name from this card's own locale file shows instead, with no error. Multiple reveals queue
/// and page with Next. Fully gamepad-reachable: the buttons take focus while the card is open.
/// </summary>
public sealed partial class DiscoveryCard : PanelContainer
{
    private SimHost? _host;
    private UiStrings? _strings;
    private object? _builtFor;
    private IReadOnlyDictionary<ushort, string>? _regionKeys;
    private string _regionMapId = string.Empty;
    private readonly Queue<DiscoveryView> _pending = new();
    private DiscoveryView? _shown;

    private Label? _title;
    private Label? _name;
    private Label? _region;
    private Label? _effect;
    private Button? _nextButton;
    private Button? _closeButton;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] discoveries: no SimHost at '{HostPath}'; the card stays empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
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
            _builtFor = host.Content;
            _strings = new UiStrings(host.Locale);
            Retitle();
            _regionMapId = string.Empty;
        }

        if (_regionKeys is null || _regionMapId != host.MapId)
        {
            _regionMapId = host.MapId;
            _regionKeys = RegionNames.ForMap(host);
        }

        if (_shown is { } shown)
        {
            Show(shown);
        }
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
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is DiscoveryRevealed revealed)
            {
                if (FindDiscovery(host, revealed) is { } view)
                {
                    _pending.Enqueue(view);
                    GD.Print($"[Evenmere] Discovery: {view.Kind} in region {view.Region} revealed.");
                }
            }
        }

        if (!Visible && _pending.Count > 0)
        {
            Show(_pending.Dequeue());
        }
    }

    private static DiscoveryView? FindDiscovery(SimHost host, DiscoveryRevealed revealed)
    {
        // The event names the tile; the card names the discovery by its index in map order, matching names.json.
        DiscoveryView[] views = host.Query.Exploration.Discoveries();
        for (int i = 0; i < views.Length; i++)
        {
            DiscoveryView view = views[i];
            if (view.Region == revealed.Region && view.Kind == revealed.Kind && view.X == revealed.X && view.Y == revealed.Y)
            {
                return view;
            }
        }

        return null;
    }

    private void Place()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -240f;
        OffsetRight = 240f;
        OffsetTop = -180f;
        OffsetBottom = 180f;
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private void BuildChrome()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        _title = new Label();
        top.AddChild(_title);

        _name = new Label();
        top.AddChild(_name);

        _region = new Label();
        top.AddChild(_region);

        _effect = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        top.AddChild(_effect);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        top.AddChild(buttons);

        _nextButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _nextButton.Pressed += ShowNext;
        buttons.AddChild(_nextButton);

        _closeButton = new Button { FocusMode = Control.FocusModeEnum.All, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _closeButton.Pressed += Close;
        buttons.AddChild(_closeButton);
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _closeButton is null || _nextButton is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.exploration.discovery.title");
        _closeButton.Text = _strings.Get("ui.exploration.discovery.close");
        _closeButton.TooltipText = _strings.Get("ui.exploration.discovery.close.tip");
        _nextButton.Text = _strings.Get("ui.exploration.discovery.next");
        _nextButton.TooltipText = _strings.Get("ui.exploration.discovery.next.tip");
    }

    private void Show(DiscoveryView view)
    {
        _shown = view;
        if (_host is not { IsRunning: true } || _strings is null)
        {
            return;
        }

        UiStrings strings = _strings;
        DiscoveryView[] views = _host.Query.Exploration.Discoveries();
        int index = Array.IndexOf(views, view);

        if (_name is not null)
        {
            _name.Text = DiscoveryName(strings, view.Kind, index);
        }

        if (_region is not null)
        {
            _region.Text = strings.Format("ui.exploration.discovery.region", ("region", RegionNames.Resolve(strings.Get, _regionKeys, view.Region)));
        }

        if (_effect is not null)
        {
            _effect.Text = EffectText(strings, view.Kind);
        }

        if (_nextButton is not null)
        {
            _nextButton.Visible = _pending.Count > 0;
        }

        Visible = true;
        if (_pending.Count > 0)
        {
            _nextButton?.GrabFocus();
        }
        else
        {
            _closeButton?.GrabFocus();
        }
    }

    private void ShowNext()
    {
        if (_pending.Count == 0)
        {
            Visible = false;
            return;
        }

        Show(_pending.Dequeue());
    }

    /// <summary>Closes the card.</summary>
    public void Close()
    {
        Visible = false;
    }

    /// <summary>
    /// Names a discovery: the <c>discovery.&lt;kind&gt;.&lt;index&gt;</c> key first, then the generic kind name,
    /// never an error. The kind slug is explicit per kind so the card cannot drift from names.json.
    /// </summary>
    /// <param name="strings">The display text.</param>
    /// <param name="kind">The discovery kind.</param>
    /// <param name="index">The discovery's index in map order, or -1 when unknown.</param>
    /// <returns>The display name.</returns>
    internal static string DiscoveryName(UiStrings strings, DiscoveryKind kind, int index)
    {
        ArgumentNullException.ThrowIfNull(strings);

        string slug = KindSlug(kind);
        if (index >= 0)
        {
            // names.json may use the bare key or the dotted name key; either resolves, neither errors.
            string key = $"discovery.{slug}.{index}";
            foreach (string candidate in (string[])[key, key + ".name"])
            {
                string named = strings.Get(candidate);
                if (!string.Equals(named, candidate, StringComparison.Ordinal))
                {
                    return named;
                }
            }
        }

        return strings.Get($"ui.exploration.discovery.{kind}");
    }

    private static string KindSlug(DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.NaturalWonder => "natural_wonder",
        DiscoveryKind.Ruin => "ruin",
        DiscoveryKind.Shipwreck => "shipwreck",
        DiscoveryKind.StoneDeposit => "stone_deposit",
        DiscoveryKind.ClayDeposit => "clay_deposit",
        DiscoveryKind.IronDeposit => "iron_deposit",
        DiscoveryKind.CoalDeposit => "coal_deposit",
        DiscoveryKind.CopperDeposit => "copper_deposit",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private string EffectText(UiStrings strings, DiscoveryKind kind)
    {
        if (_host is not { IsRunning: true } host)
        {
            return strings.Get("ui.exploration.discovery.effect.unknown");
        }

        DiscoveryRewards? rewards = host.Content.Rules.Discoveries;
        switch (kind)
        {
            case DiscoveryKind.Ruin:
                if (rewards is not null && rewards.RuinKnowledge > Amount.Zero)
                {
                    return strings.Format(
                        "ui.exploration.discovery.effect.ruin",
                        ("amount", NumberFormat.Units(rewards.RuinKnowledge)));
                }

                break;
            case DiscoveryKind.Shipwreck:
                if (rewards is not null && !rewards.ShipwreckGoods.IsDefaultOrEmpty && rewards.ShipwreckGoods.Length > 0)
                {
                    var parts = new List<string>(rewards.ShipwreckGoods.Length);
                    foreach (GoodAmount grant in rewards.ShipwreckGoods)
                    {
                        parts.Add($"+{NumberFormat.Units(grant.Amount)} {strings.GoodName(host.Content.Goods.NameOf(grant.Good))}");
                    }

                    return strings.Format("ui.exploration.discovery.effect.shipwreck", ("goods", string.Join(", ", parts)));
                }

                break;
            case DiscoveryKind.NaturalWonder:
                return strings.Get("ui.exploration.discovery.effect.wonder");
            default:
                break;
        }

        return strings.Get("ui.exploration.discovery.effect.unknown");
    }
}
