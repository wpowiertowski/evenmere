using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Monuments;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Godot;

namespace Evenmere.Game.Ui.Events;

/// <summary>
/// The festival panel (M2-27): opened from a Festival Ground, it lists the available festivals with their costs
/// and effects and why one cannot be held (season, yearly cooldown, goods — from the sim's <c>HoldFestival</c>
/// rejection reasons, shown as calm sentences). Holding sends <c>HoldFestival</c> for the ground it opened on.
/// While a festival runs, its row is marked, bunting appears over the ground, and the HUD shows a quiet badge.
/// </summary>
public sealed partial class FestivalPanel : PanelContainer
{
    private SimHost? _host;
    private ContentDb? _builtFor;
    private FestivalPanelModel? _model;
    private UiStrings? _strings;
    private EntityId _ground = EntityId.None;
    private MonumentRenderer? _bunting;
    private Label? _title;
    private Label? _subtitle;
    private Label? _running;
    private Label? _note;
    private Label? _empty;
    private Button? _closeButton;
    private VBoxContainer? _rows;
    private readonly Dictionary<FestivalId, RowControls> _rowControls = new();

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets the ground the panel opened on, or <see cref="EntityId.None"/> when closed.</summary>
    public EntityId Ground => _ground;

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] festivals: no SimHost at '{HostPath}'; the panel stays empty.");
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

        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (_model is null || !ReferenceEquals(_builtFor, host.Content))
        {
            RebuildModel(host);
        }

        _model?.Advance(delta);
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

    /// <summary>Opens the panel on a festival ground.</summary>
    /// <param name="ground">The ground the <c>HoldFestival</c> commands name.</param>
    public void Open(EntityId ground)
    {
        _ground = ground;
        if (_model is not null)
        {
            _model.Ground = ground;
            _model.MarkDirty();
            _model.Advance(10.0);
        }

        Visible = true;
    }

    /// <summary>Closes the panel.</summary>
    public void Close()
    {
        Visible = false;
        _ground = EntityId.None;
        if (_model is not null)
        {
            _model.Ground = EntityId.None;
        }

        HideBunting();
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (_model is null)
        {
            RebuildModel(host);
        }

        if (_model is null)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is CommandRejected rejected
                && rejected.Command is HoldFestival
                && _note is not null
                && _strings is not null)
            {
                _note.Visible = true;
                _note.Text = rejected.Code switch
                {
                    RejectReason.FestivalNotUnlocked => _strings.Get("ui.events.festival.block.locked"),
                    RejectReason.WrongSeason => _strings.Get("ui.events.festival.block.season"),
                    RejectReason.FestivalAlreadyRunning => _strings.Get("ui.events.festival.block.running"),
                    RejectReason.FestivalHeldThisYear => _strings.Get("ui.events.festival.block.held"),
                    RejectReason.FestivalGoodsShort => rejected.Detail,
                    _ => rejected.Detail,
                };
            }
        }

        _model.MarkDirty();
    }

    private void Place()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -300f;
        OffsetRight = 300f;
        OffsetTop = -290f;
        OffsetBottom = 290f;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void RebuildModel(SimHost host)
    {
        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        _model = new FestivalPanelModel(new SimFestivalData(host), _strings) { Ground = _ground };
        _model.Refreshed += OnRefreshed;
        _model.MarkDirty();
        Retitle();
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _closeButton is null || _subtitle is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.events.festival.title");
        _subtitle.Text = _strings.Get("ui.events.festival.subtitle");
        _closeButton.Text = _strings.Get("ui.events.festival.close");
        _closeButton.TooltipText = _strings.Get("ui.events.festival.close.tip");
    }

    private void OnRefreshed() => UpdateAll();

    private void UpdateAll()
    {
        if (_model is null || _strings is null || _rows is null)
        {
            return;
        }

        UiStrings strings = _strings;
        FestivalPanelModel model = _model;

        if (_running is not null)
        {
            _running.Visible = model.RunningLine is not null;
            _running.Text = model.RunningLine ?? string.Empty;
        }

        if (_empty is not null)
        {
            _empty.Visible = model.Rows.Count == 0;
            _empty.Text = strings.Get("ui.events.festival.empty");
        }

        SyncRows(model, strings);
        RefreshBunting();
    }

    private void SyncRows(FestivalPanelModel model, UiStrings strings)
    {
        var seen = new HashSet<FestivalId>();
        foreach (FestivalRow row in model.Rows)
        {
            seen.Add(row.Id);
            if (!_rowControls.TryGetValue(row.Id, out RowControls? controls))
            {
                controls = AddRow(row.Id);
                _rowControls[row.Id] = controls;
            }

            controls.Name.Text = row.Name;
            controls.Cost.Text = row.CostText;
            controls.Days.Text = row.DaysText;
            controls.Culture.Text = row.CultureText;
            controls.Effect.Text = row.EffectText;
            controls.Status.Visible = !string.IsNullOrEmpty(row.StatusText);
            controls.Status.Text = row.StatusText;
            controls.Hold.Disabled = !row.CanHold;
            // A Hold that cannot fire stays visible (with its calm reason above it) but leaves the focus
            // chain, so gamepad focus never stops on a dead button and the focus walk only proves live ones.
            controls.Hold.FocusMode = row.CanHold ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        }

        var stale = new List<FestivalId>();
        foreach (FestivalId id in _rowControls.Keys)
        {
            if (!seen.Contains(id))
            {
                stale.Add(id);
            }
        }

        foreach (FestivalId id in stale)
        {
            _rowControls[id].Root.QueueFree();
            _rowControls.Remove(id);
        }

        if (_note is not null && _note.Visible && model.Rows.Count == 0)
        {
            _note.Visible = false;
        }
    }

    private RowControls AddRow(FestivalId id)
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 2);
        _rows!.AddChild(root);

        var name = new Label();
        name.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(name);

        var cost = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(cost);

        var days = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(days);

        var culture = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(culture);

        var effect = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(effect);

        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(status);

        var hold = new Button { FocusMode = Control.FocusModeEnum.All };
        hold.Pressed += () => SendHold(id);
        root.AddChild(hold);

        if (_strings is not null)
        {
            hold.Text = _strings.Get("ui.events.festival.hold");
            hold.TooltipText = _strings.Get("ui.events.festival.hold.tip");
        }

        var separator = new HSeparator();
        root.AddChild(separator);

        return new RowControls(root, name, cost, days, culture, effect, status, hold);
    }

    private void SendHold(FestivalId id)
    {
        if (_host is null || _model is null)
        {
            return;
        }

        if (_model.HoldCommand(id) is { } command)
        {
            if (_note is not null)
            {
                _note.Visible = false;
            }

            _host.Enqueue(command);
            _model.MarkDirty();
        }
    }

    private void RefreshBunting()
    {
        if (_host is not { IsRunning: true } host)
        {
            HideBunting();
            return;
        }

        if (host.Query.Culture.ActiveFestivalView() is not { } run)
        {
            HideBunting();
            return;
        }

        if (!host.Query.Map.TryGetBuilding(run.Ground, out BuildingInfo info))
        {
            HideBunting();
            return;
        }

        _bunting ??= MakeBunting();
        float groundY = GroundHeight(host, info);
        _bunting.ShowBunting(
            new Vector3(info.Origin.X, groundY, info.Origin.Y),
            new Vector3(info.Origin.X + info.Width, groundY, info.Origin.Y + info.Height));
    }

    private MonumentRenderer MakeBunting()
    {
        // The bunting lives in world space, so it hangs off the scene root rather than this panel.
        var renderer = new MonumentRenderer();
        GetTree().CurrentScene.AddChild(renderer);
        return renderer;
    }

    private void HideBunting()
    {
        _bunting?.Clear();
    }

    private static float GroundHeight(SimHost host, BuildingInfo info)
    {
        float top = 0f;
        for (int y = info.Origin.Y; y < info.Origin.Y + info.Height; y++)
        {
            for (int x = info.Origin.X; x < info.Origin.X + info.Width; x++)
            {
                top = Math.Max(top, Rendering.Terrain.TerrainSurface.GroundHeight(host.Query.Map.GetTile(x, y).Elevation));
            }
        }

        return top;
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

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        var title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(title);
        _title = title;

        var close = new Button { FocusMode = Control.FocusModeEnum.All };
        close.Pressed += Close;
        header.AddChild(close);
        _closeButton = close;

        var subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(subtitle);
        _subtitle = subtitle;

        var running = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(running);
        _running = running;

        var note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        top.AddChild(note);
        _note = note;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        top.AddChild(scroll);

        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(rows);
        _rows = rows;

        var empty = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(empty);
        _empty = empty;
    }

    private sealed class RowControls(
        VBoxContainer Root,
        Label Name,
        Label Cost,
        Label Days,
        Label Culture,
        Label Effect,
        Label Status,
        Button Hold)
    {
        public VBoxContainer Root { get; } = Root;

        public Label Name { get; } = Name;

        public Label Cost { get; } = Cost;

        public Label Days { get; } = Days;

        public Label Culture { get; } = Culture;

        public Label Effect { get; } = Effect;

        public Label Status { get; } = Status;

        public Button Hold { get; } = Hold;
    }

    /// <summary>Reads the festival facts from the running simulation: content for the defs, queries for the state.</summary>
    private sealed class SimFestivalData(SimHost host) : IFestivalData
    {
        public bool IsRunning => host.Query.Culture.IsFestivalRunning();

        public Season SeasonNow => host.Clock.Season;

        public string? RunningName => host.Query.Culture.ActiveFestivalView() is { } run
            ? FestivalName(host, run.Festival)
            : null;

        public int RunningDaysLeft => host.Query.Culture.ActiveFestivalView()?.DaysLeft ?? 0;

        public IReadOnlyList<FestivalFacts> Festivals
        {
            get
            {
                ContentDb content = host.Content;
                var facts = new List<FestivalFacts>(content.Festivals.Count);
                for (int i = 0; i < content.Festivals.Count; i++)
                {
                    var id = FestivalId.From(checked((ushort)i));
                    FestivalDef def = content.Get(id);
                    facts.Add(new FestivalFacts(
                        id,
                        FestivalName(host, id),
                        [.. def.Seasons],
                        CostOf(host, def),
                        def.Days,
                        def.CulturePerDay,
                        def.SeasonBonus,
                        EffectText(host, def),
                        host.Query.Progression.IsFestivalUnlocked(id),
                        host.Query.Culture.FestivalsHeldInYear(id, checked((int)host.Clock.Year)) > 0));
                }

                return facts;
            }
        }

        private static ImmutableArray<FestivalCostFact> CostOf(SimHost host, FestivalDef def)
        {
            // The same discount the sim charges: each good through the island's festivalCost modifiers, rounded
            // down like the construction bills (FestivalScheduler.DiscountedCost, internal to the sim).
            var strings = new UiStrings(host.Locale);
            var lines = new FestivalCostFact[def.Cost.Goods.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                GoodAmount line = def.Cost.Goods[i];
                int discount = host.Query.Progression.BonusPercent(Stat.FestivalCost, ModifierContext.ForGood(line.Good));
                Amount need = line.Amount.MultiplyBy(Ratio.FromBonusPercent(discount));
                lines[i] = new FestivalCostFact(
                    strings.GoodName(host.Content.Goods.NameOf(line.Good)),
                    need,
                    host.Query.Storage.Stock(line.Good));
            }

            return [.. lines];
        }

        private static string FestivalName(SimHost host, FestivalId id)
        {
            ContentDb content = host.Content;
            string key = $"festival.{content.NameOf(id)}.name";
            // A missing string reads back as the key itself, so fall back to the raw content ID.
            string text = host.Locale.Get(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(id) : text;
        }

        private static string EffectText(SimHost host, FestivalDef def)
        {
            var strings = new UiStrings(host.Locale);
            IReadOnlyList<GraphUnlock> unlocks = CivicGraphBuilder.DescribeEffects(
                host.Content, strings, def.Effects, static _ => null);
            if (unlocks.Count == 0)
            {
                string key = $"festival.{host.Content.NameOf(def.Id)}.desc";
                return strings.Get(key);
            }

            var parts = new List<string>(unlocks.Count);
            foreach (GraphUnlock unlock in unlocks)
            {
                parts.Add(unlock.Glyph.Length > 0 ? $"{unlock.Glyph}  {unlock.Text}" : unlock.Text);
            }

            return string.Join("; ", parts);
        }
    }
}
