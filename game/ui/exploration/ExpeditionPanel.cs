using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Exploration;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Godot;
using SimRotation = Evenmere.Sim.Queries.Types.Rotation;

namespace Evenmere.Game.Ui.Exploration;

/// <summary>
/// The expedition picker (M2-23): opened from a Scout's Lodge, it lists the adjacent fogged regions with
/// their ETA and confirms with <see cref="StartExpedition"/>. The ETA uses the sim's own pure helper
/// (<see cref="ExpeditionMath.DurationDays"/>) with the distance and terrain inputs re-read from the view,
/// never a duplicated formula. An expedition already under way shows with its days left, and the sim's
/// rejection reason shows when it refuses. Every button is focusable, so the whole picker is gamepad-reachable.
/// </summary>
public sealed partial class ExpeditionPanel : PanelContainer
{
    /// <summary>The tag a finished building must carry to send expeditions; matched by tag, never by content ID.</summary>
    public const string LodgeTag = "expedition_lodge";

    private const float RefreshIntervalSeconds = 0.5f;

    private SimHost? _host;
    private ContentDb? _builtFor;
    private UiStrings? _strings;
    private EntityId _lodge = EntityId.None;
    private IReadOnlyDictionary<ushort, string>? _regionKeys;
    private string _regionMapId = string.Empty;
    private double _secondsSinceRefresh = double.MaxValue;
    private int _stageStep = -1;
    private string _stageMode = string.Empty;

    private Label? _title;
    private Label? _subtitle;
    private Label? _status;
    private Label? _empty;
    private Label? _note;
    private Button? _closeButton;
    private VBoxContainer? _rows;
    private readonly Dictionary<ushort, RowControls> _rowControls = new();

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the camera rig, used by the hills screenshot staging.</summary>
    [Export]
    public NodePath RigPath { get; set; } = new("../CameraRig");

    /// <summary>Gets the lodge the picker opened on, or <see cref="EntityId.None"/> when closed.</summary>
    public EntityId Lodge => _lodge;

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] expeditions: no SimHost at '{HostPath}'; the picker stays empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
        Visible = false;
        if (OS.IsDebugBuild())
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--expedition-stage=", StringComparison.Ordinal))
                {
                    _stageMode = arg["--expedition-stage=".Length..];
                    _stageStep = 0;
                }
            }
        }
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
            _secondsSinceRefresh = double.MaxValue;
        }

        if (_stageStep >= 0)
        {
            RunStaging(host);
        }

        if (!Visible)
        {
            return;
        }

        _secondsSinceRefresh += delta;
        if (_secondsSinceRefresh < RefreshIntervalSeconds)
        {
            return;
        }

        _secondsSinceRefresh = 0.0;
        Refresh();
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

    /// <summary>Opens the picker on a lodge.</summary>
    /// <param name="lodge">The Scout's Lodge to send expeditions from.</param>
    public void Open(EntityId lodge)
    {
        _lodge = lodge;
        if (_note is not null)
        {
            _note.Visible = false;
        }

        Visible = true;
        _secondsSinceRefresh = double.MaxValue;
        GD.Print($"[Evenmere] Expedition: opened picker on lodge {lodge.Value}.");
    }

    /// <summary>Closes the picker.</summary>
    public void Close()
    {
        Visible = false;
        _lodge = EntityId.None;
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is CommandRejected rejected
                && rejected.Command is StartExpedition send
                && send.Lodge == _lodge
                && _note is not null
                && _strings is not null)
            {
                _note.Visible = true;
                _note.Text = _strings.Format("ui.exploration.expedition.rejected", ("detail", rejected.Detail));
            }
            else if (@event is RegionRevealed)
            {
                _secondsSinceRefresh = double.MaxValue;
            }
        }
    }

    private void Place()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -280f;
        OffsetRight = 280f;
        OffsetTop = -260f;
        OffsetBottom = 260f;
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

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(_title);

        _closeButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _closeButton.Pressed += Close;
        header.AddChild(_closeButton);

        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_subtitle);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_status);

        _empty = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_empty);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(scroll);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_rows);

        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_note);
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _closeButton is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.exploration.expedition.title");
        _closeButton.Text = _strings.Get("ui.exploration.expedition.close");
        _closeButton.TooltipText = _strings.Get("ui.exploration.expedition.close.tip");
    }

    private void Refresh()
    {
        if (_host is not { IsRunning: true } host || _strings is null || _rows is null)
        {
            return;
        }

        UiStrings strings = _strings;
        if (_regionKeys is null || _regionMapId != host.MapId)
        {
            _regionMapId = host.MapId;
            _regionKeys = RegionNames.ForMap(host);
        }

        if (!host.Query.Map.TryGetBuilding(_lodge, out BuildingInfo lodge))
        {
            if (_subtitle is not null)
            {
                _subtitle.Text = strings.Get("ui.exploration.expedition.unknown");
            }

            ShowStatus(null, strings);
            SyncRows([], strings);
            return;
        }

        if (_subtitle is not null)
        {
            _subtitle.Text = strings.Format(
                "ui.exploration.expedition.subtitle",
                ("lodge", BuildingDisplayName(host.Content, strings, lodge.Type)));
        }

        ExpeditionView? underway = host.Query.Exploration.ExpeditionForLodge(_lodge);
        ShowStatus(underway, strings);
        if (underway is { } active)
        {
            SyncRows([], strings);
            return;
        }

        List<Candidate> candidates = ComputeCandidates(host, _lodge, lodge);
        if (_empty is not null)
        {
            _empty.Visible = candidates.Count == 0;
            _empty.Text = strings.Get("ui.exploration.expedition.empty");
        }

        SyncRows(candidates, strings);
    }

    private void ShowStatus(ExpeditionView? underway, UiStrings strings)
    {
        if (_status is null || _host is not { IsRunning: true })
        {
            return;
        }

        if (underway is not { } active)
        {
            _status.Visible = false;
            return;
        }

        _status.Visible = true;
        string days = active.DaysLeft == 1
            ? strings.Get("ui.exploration.expedition.days.one")
            : strings.Format("ui.exploration.expedition.days.other", ("count", NumberFormat.Count(active.DaysLeft)));
        _status.Text = strings.Format(
            "ui.exploration.expedition.underway",
            ("region", RegionNames.Resolve(strings.Get, _regionKeys, active.TargetRegion)),
            ("days", days));
    }

    private void SyncRows(List<Candidate> candidates, UiStrings strings)
    {
        if (_rows is null)
        {
            return;
        }

        var seen = new HashSet<ushort>();
        bool first = true;
        foreach (Candidate candidate in candidates)
        {
            seen.Add(candidate.Region);
            if (!_rowControls.TryGetValue(candidate.Region, out RowControls? controls))
            {
                controls = AddRow(candidate.Region);
                _rowControls[candidate.Region] = controls;
            }

            string days = candidate.Days == 1
                ? strings.Get("ui.exploration.expedition.days.one")
                : strings.Format("ui.exploration.expedition.days.other", ("count", NumberFormat.Count(candidate.Days)));
            controls.Label.Text = RegionNames.Resolve(strings.Get, _regionKeys, candidate.Region)
                + " — " + days;
            controls.Send.Text = strings.Get("ui.exploration.expedition.send");
            controls.Send.TooltipText = strings.Format(
                "ui.exploration.expedition.send.tip",
                ("region", NumberFormat.Count(candidate.Region)),
                ("days", days));
            if (first)
            {
                controls.Send.GrabFocus();
                first = false;
            }
        }

        var gone = new List<ushort>();
        foreach ((ushort region, RowControls controls) in _rowControls)
        {
            if (!seen.Contains(region))
            {
                gone.Add(region);
                _rows.RemoveChild(controls.Row);
                controls.Row.QueueFree();
            }
        }

        foreach (ushort region in gone)
        {
            _rowControls.Remove(region);
        }

        if (first && _closeButton is not null && Visible)
        {
            _closeButton.GrabFocus();
        }
    }

    private RowControls AddRow(ushort region)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _rows!.AddChild(row);

        var label = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(label);

        var send = new Button { FocusMode = Control.FocusModeEnum.All };
        send.Pressed += () => Send(region);
        row.AddChild(send);
        return new RowControls(row, label, send);
    }

    private void Send(ushort region)
    {
        if (_host is not { IsRunning: true } host || _lodge == EntityId.None)
        {
            return;
        }

        if (_note is not null)
        {
            _note.Visible = false;
        }

        host.Enqueue(new StartExpedition(_lodge, region));
        _secondsSinceRefresh = double.MaxValue;
        GD.Print($"[Evenmere] Expedition: enqueued StartExpedition lodge={_lodge.Value} region={region}.");
    }

    /// <summary>
    /// Finds the expedition targets for a lodge: the adjacent fogged regions from the pure helper, each
    /// with the ETA from the sim's own helper (never a duplicated formula).
    /// </summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="lodge">The lodge sending the expedition.</param>
    /// <param name="building">The lodge's building info.</param>
    /// <returns>The candidates with their ETAs.</returns>
    internal static List<Candidate> ComputeCandidates(SimHost host, EntityId lodge, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(host);

        var revealed = new HashSet<ushort>(host.Query.Exploration.RevealedRegions());
        List<ExpeditionTarget> targets = ExpeditionCandidates.Find(
            host.Query.Map.Width,
            host.Query.Map.Height,
            (x, y) => host.Query.Map.GetTile(x, y).Region,
            (x, y) => host.Query.Map.GetTile(x, y).Elevation,
            revealed,
            building.Origin.X,
            building.Origin.Y);
        int bonus = host.Query.Progression.BonusPercent(Stat.ExpeditionSpeed, ModifierContext.ForBuilding(lodge, building.Type));
        var candidates = new List<Candidate>(targets.Count);
        foreach (ExpeditionTarget target in targets)
        {
            // DurationDays is the sim's own helper, so the picker can never drift from the countdown.
            candidates.Add(new Candidate(target.Region, ExpeditionMath.DurationDays(target.Nearest, target.AverageElevation, bonus)));
        }

        return candidates;
    }

    private static bool HasTag(System.Collections.Immutable.ImmutableArray<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildingDisplayName(ContentDb content, UiStrings strings, BuildingTypeId type)
    {
        string key = $"building.{content.NameOf(type)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(type) : text;
    }

    /// <summary>Tests whether a finished building can send expeditions: the expedition_lodge tag, never a content ID.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="building">The building to test.</param>
    /// <returns><see langword="true"/> for a finished lodge.</returns>
    internal static bool IsLodge(ContentDb content, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(content);
        return building.IsComplete && HasTag(content.Get(building.Type).Tags, LodgeTag);
    }

    private sealed record RowControls(HBoxContainer Row, Label Label, Button Send);

    /// <summary>One fogged neighbour with the days the expedition would take.</summary>
    /// <param name="Region">The fogged region number.</param>
    /// <param name="Days">The ETA in days, from <see cref="ExpeditionMath.DurationDays"/>.</param>
    internal sealed record Candidate(ushort Region, int Days);

    // Debug screenshots only (--expedition-stage=picker|underway|reveal|card): raise a Scout's Lodge near the
    // hall through the real commands (research, place, build), then stage the requested moment. Each step runs
    // one frame apart so the scene settles; failures log and stop the machine without failing the run.
    private void RunStaging(SimHost host)
    {
        try
        {
            switch (_stageStep)
            {
                case 0:
                    if (_stageMode is "hills" or "dissolve")
                    {
                        StageHills(host, _stageMode == "dissolve");
                    }
                    else
                    {
                        StageEnsureLodge(host);
                    }

                    break;
                case 4:
                    StageDissolve(host);
                    _stageStep = -1;
                    break;
                case 1:
                    StageOpenOrSend(host, send: _stageMode is "underway" or "reveal" or "card");
                    break;
                case 2:
                    StageAdvance(host);
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Expedition stage '{_stageMode}' failed: {ex.Message.ReplaceLineEndings(" ")}");
            _stageStep = -1;
        }
    }

    // Debug screenshots only: frame the highest fogged hill near the island middle. Dissolve mode
    // reveals the framed region one frame after the sheets build, so a screenshot catches the dissolve.
    private ushort _dissolveRegion;

    private void StageHills(SimHost host, bool dissolve)
    {
        MapDef generated = IslandGenerator.Generate(7, 320, host.Content);
        host.StartNewGame(7, generated);
        int bestX = 0;
        int bestY = 0;
        int bestElevation = -1;
        var revealed = new HashSet<ushort>(host.Query.Exploration.RevealedRegions());
        for (int y = 0; y < host.Query.Map.Height; y++)
        {
            for (int x = 0; x < host.Query.Map.Width; x++)
            {
                TileInfo tile = host.Query.Map.GetTile(x, y);
                bool central = Math.Abs(x - host.Query.Map.Width / 2) < host.Query.Map.Width / 4
                    && Math.Abs(y - host.Query.Map.Height / 2) < host.Query.Map.Height / 4;
                if (central && !revealed.Contains(tile.Region) && tile.Elevation > bestElevation)
                {
                    bestElevation = tile.Elevation;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        if (GetNodeOrNull<Evenmere.Game.Rendering.Camera.CameraRig>(RigPath) is { } rig)
        {
            // The rig still holds the previous island's bounds until the terrain rebuilds; without this
            // the focus clamps to the old map and the screenshots frame the wrong tiles entirely.
            rig.SetMapBounds(host.Query.Map.Width, host.Query.Map.Height);
            rig.FocusOnTile(new Vector2I(bestX, bestY));
            rig.SetZoom(40f, true);
            Godot.Vector3 focused = rig.FocusPoint;
            GD.Print($"[Evenmere] Expedition stage: camera at ({focused.X}, {focused.Z}).");
        }

        host.Speed = SimSpeed.Paused;
        TileInfo best = host.Query.Map.GetTile(bestX, bestY);
        GD.Print($"[Evenmere] Expedition stage: hills framed at ({bestX}, {bestY}) elevation {bestElevation} region {best.Region}.");

        // The reveal waits a frame so the fog sheets exist first; the dissolve then plays on real patches.
        _dissolveRegion = best.Region;
        _stageStep = dissolve ? 4 : -1;
    }

    private void StageDissolve(SimHost host)
    {
        host.Enqueue(new CheatRevealAll(_dissolveRegion));
        host.RunTicksNow(1);
        GD.Print($"[Evenmere] Expedition stage: dissolving region {_dissolveRegion}.");
    }

    // Discoveries only exist on generated islands: the reveal and card stages restart on one (seed 7,
    // the scenario seed) so they can reach a card. The fog sheets rebuild from the new map dimensions
    // on the next frame. Picker stages stay on the starter island, whose map ships region names.
    private void StageEnsureLodge(SimHost host)
    {
        if (_stageMode is "reveal" or "card")
        {
            MapDef generated = IslandGenerator.Generate(7, 320, host.Content);
            host.StartNewGame(7, generated);
            GD.Print("[Evenmere] Expedition stage: generated island ready.");
        }
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (host.Content.Buildings.NameOf(building.Type) == "scouts_lodge" && building.IsComplete)
            {
                GD.Print($"[Evenmere] Expedition stage: reusing lodge {building.Id.Value}.");
                _stageStep = 1;
                return;
            }
        }

        TechId pathfinding = host.Content.Techs.IdOf("pathfinding");
        if (!host.Query.Research.IsDone(pathfinding))
        {
            // Debug runs enable cheats, so complete the tech directly; without them, research it for real.
            host.Enqueue(new CheatCompleteNode(pathfinding, null));
            host.RunTicksNow(1);
            if (!host.Query.Research.IsDone(pathfinding))
            {
                host.Enqueue(new SetResearchTarget(pathfinding));
                int days = 0;
                while (!host.Query.Research.IsDone(pathfinding) && days < 400)
                {
                    host.RunTicksNow(GameTime.TicksPerDay);
                    days++;
                }
            }

            GD.Print($"[Evenmere] Expedition stage: pathfinding done={host.Query.Research.IsDone(pathfinding)}.");
        }

        // The lodge bill is small, but a fresh island may hold no timber yet; grant the raising crew its wood.
        host.Enqueue(new CheatGrantGoods(host.Content.Goods.IdOf("timber"), Amount.FromUnits(20)));
        host.RunTicksNow(1);

        if (!TryFindHall(host, out int hallX, out int hallY))
        {
            throw new InvalidOperationException("no founders hall for the lodge.");
        }

        // The Founders' Hall is the island's first storehouse (reach 24), so the lodge must rise close to
        // it; the road connector below joins the hall network for the delivery carts.
        EntityId lodge = StageRaise(host, "scouts_lodge", hallX, hallY, 20);
        GD.Print($"[Evenmere] Expedition stage: lodge {lodge.Value} raised.");
        _stageStep = 1;
    }

    // Raises a building near a point through the real commands: place, road to the hall network, then
    // wait for the crew to finish. Throws when no spot validates or the site stalls.
    private static EntityId StageRaise(SimHost host, string typeName, int nearX, int nearY, int maxRing)
    {
        BuildingTypeId type = host.Content.Buildings.IdOf(typeName);
        for (int r = 2; r <= maxRing; r++)
        {
            for (int y = nearY - r; y <= nearY + r; y++)
            {
                for (int x = nearX - r; x <= nearX + r; x++)
                {
                    if (Math.Max(Math.Abs(x - nearX), Math.Abs(y - nearY)) != r)
                    {
                        continue;
                    }

                    if (!host.Query.Placement.Validate(type, x, y, SimRotation.Deg0).CanPlace)
                    {
                        continue;
                    }

                    host.Enqueue(new PlaceBuilding(type, x, y, SimRotation.Deg0));
                    host.RunTicksNow(1);
                    EntityId id = host.Query.Map.BuildingAt(x, y);
                    if (id == EntityId.None)
                    {
                        continue;
                    }

                    if (!StageConnectRoad(host, id, new TileCoord(x, y), nearX, nearY))
                    {
                        host.Enqueue(new Demolish(id));
                        host.RunTicksNow(1);
                        continue;
                    }

                    int days = 0;
                    while (days < 40)
                    {
                        if (host.Query.Map.TryGetBuilding(id, out BuildingInfo raised) && raised.IsComplete)
                        {
                            return id;
                        }

                        host.RunTicksNow(GameTime.TicksPerDay);
                        days++;
                        if (days == 5 && host.Query.Construction.TryGetSite(id, out SiteInfo site))
                        {
                            string waiting = site.WaitingFor is { } good ? host.Content.Goods.NameOf(good) : "none";
                            GD.Print($"[Evenmere] Expedition stage: {typeName} {id.Value} stall={site.Stall} waiting={waiting} progress={site.Progress}.");
                        }
                    }

                    throw new InvalidOperationException($"{typeName} {id.Value} never finished.");
                }
            }
        }

        throw new InvalidOperationException($"no valid spot for {typeName}.");
    }

    private void StageOpenOrSend(SimHost host, bool send)
    {
        EntityId lodge = EntityId.None;
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (host.Content.Buildings.NameOf(building.Type) == "scouts_lodge" && building.IsComplete)
            {
                lodge = building.Id;
                break;
            }
        }

        if (lodge == EntityId.None)
        {
            throw new InvalidOperationException("the lodge is gone.");
        }

        Open(lodge);
        if (send)
        {
            if (!host.Query.Map.TryGetBuilding(lodge, out BuildingInfo building))
            {
                throw new InvalidOperationException("the lodge is gone.");
            }

            List<Candidate> candidates = ComputeCandidates(host, lodge, building);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("no fogged neighbours to send to.");
            }

            var discoveryRegions = new HashSet<ushort>();
            foreach (DiscoveryView view in host.Query.Exploration.Discoveries())
            {
                discoveryRegions.Add(view.Region);
                GD.Print($"[Evenmere] Expedition stage: discovery {view.Kind} in region {view.Region} at ({view.X}, {view.Y}).");
            }

            // The reveal and card shots want a discovery: prefer a candidate that holds one.
            Candidate target = candidates[0];
            foreach (Candidate candidate in candidates)
            {
                if (discoveryRegions.Contains(candidate.Region))
                {
                    target = candidate;
                    break;
                }
            }

            host.Enqueue(new StartExpedition(lodge, target.Region));
            host.RunTicksNow(1);
            ExpeditionView? check = host.Query.Exploration.ExpeditionForLodge(lodge);
            GD.Print($"[Evenmere] Expedition stage: sent to region {target.Region} ETA {target.Days} day(s); underway={(check is { } c ? $"region {c.TargetRegion} {c.DaysLeft}d" : "none")}.");
        }

        host.Speed = SimSpeed.Paused;
        _secondsSinceRefresh = double.MaxValue;
        Refresh();
        _stageStep = 2;
    }

    private void StageAdvance(SimHost host)
    {
        _stageStep = -1;
        if (_stageMode is not ("reveal" or "card"))
        {
            return;
        }

        ExpeditionView? underway = host.Query.Exploration.ExpeditionForLodge(_lodge);
        if (underway is not { } active)
        {
            GD.Print("[Evenmere] Expedition stage: nothing under way to advance.");
            return;
        }

        // Land just past the reveal's day start: the RegionRevealed event drains, the fog starts
        // dissolving and the discovery card opens, all before the next rendered frame. The picker closes
        // so the reveal reads. Card mode settles two days further on, after the haze has dissolved.
        int ticks = active.DaysLeft * GameTime.TicksPerDay;
        if (_stageMode == "reveal")
        {
            ticks += 5;
        }
        else
        {
            ticks += GameTime.TicksPerDay * 2;
        }

        Visible = false;
        host.Speed = SimSpeed.Paused;
        host.RunTicksNow(ticks);
        _secondsSinceRefresh = double.MaxValue;
        Refresh();
        GD.Print($"[Evenmere] Expedition stage '{_stageMode}': advanced {ticks} ticks.");
    }

    // The raising crew needs a road to the hall network, like the slice demo's pattern: a BFS over tiles
    // that each validate as a dirt road on their own, deterministic in north-east-south-west order.
    private static bool StageConnectRoad(SimHost host, EntityId id, TileCoord origin, int hallX, int hallY)
    {
        if (!host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
        {
            return false;
        }

        BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
        var network = new HashSet<TileCoord>();
        foreach (BuildingInfo other in host.Query.Map.Buildings)
        {
            if (host.Content.Buildings.NameOf(other.Type) == "founders_hall")
            {
                for (int y = other.Origin.Y; y < other.Origin.Y + other.Height; y++)
                {
                    for (int x = other.Origin.X; x < other.Origin.X + other.Width; x++)
                    {
                        network.Add(new TileCoord(x, y));
                    }
                }
            }
        }

        var starts = new List<TileCoord>();
        for (int y = origin.Y - 1; y <= origin.Y + building.Height; y++)
        {
            for (int x = origin.X - 1; x <= origin.X + building.Width; x++)
            {
                bool edge = (x == origin.X - 1 || x == origin.X + building.Width) != (y == origin.Y - 1 || y == origin.Y + building.Height);
                var tile = new TileCoord(x, y);
                if (edge && tile.IsInside(host.Query.Map.Width, host.Query.Map.Height)
                    && host.Query.Placement.ValidateRoad([tile], roadType).CanPlace)
                {
                    starts.Add(tile);
                }
            }
        }

        TileCoord[] offsets = [new TileCoord(0, -1), new TileCoord(1, 0), new TileCoord(0, 1), new TileCoord(-1, 0)];
        var parent = new Dictionary<TileCoord, TileCoord>();
        var queue = new Queue<TileCoord>(starts);
        foreach (TileCoord start in starts)
        {
            parent[start] = start;
        }

        TileCoord? goal = null;
        while (queue.Count > 0 && goal is null)
        {
            TileCoord current = queue.Dequeue();
            if (network.Contains(current))
            {
                goal = current;
                break;
            }

            foreach (TileCoord offset in offsets)
            {
                TileCoord next = current.Offset(offset.X, offset.Y);
                if (parent.ContainsKey(next))
                {
                    continue;
                }

                if (network.Contains(next))
                {
                    parent[next] = current;
                    goal = next;
                    break;
                }

                if (!next.IsInside(host.Query.Map.Width, host.Query.Map.Height)
                    || !host.Query.Placement.ValidateRoad([next], roadType).CanPlace)
                {
                    continue;
                }

                parent[next] = current;
                queue.Enqueue(next);
            }
        }

        if (goal is null)
        {
            GD.Print($"[Evenmere] Expedition stage: no road route to lodge {id.Value}.");
            return false;
        }

        var reversed = new List<TileCoord>();
        TileCoord step = parent[goal.Value];
        while (!parent[step].Equals(step))
        {
            reversed.Add(step);
            step = parent[step];
        }

        // A site already touching the hall still needs a road tile beside it: the reach field only
        // covers buildings through road tiles, so a zero-length connector leaves the site uncovered.
        // The stub must avoid network tiles (a start can sit on the hall itself), which stay untouched.
        if (reversed.Count == 0)
        {
            foreach (TileCoord candidate in starts)
            {
                if (!network.Contains(candidate))
                {
                    reversed.Add(candidate);
                    break;
                }
            }
        }

        reversed.Reverse();
        host.Enqueue(new PlaceRoad([.. reversed], roadType));
        // The reach field only covers buildings through road tiles chained back toward the storehouse:
        // a bare connector leaves gaps, so every valid edge tile also gets its own stub.
        foreach (TileCoord candidate in starts)
        {
            if (!network.Contains(candidate) && !reversed.Contains(candidate))
            {
                host.Enqueue(new PlaceRoad([candidate], roadType));
            }
        }

        host.RunTicksNow(1);
        bool reached = host.Query.Roads.IsInStorehouseReach(id);
        GD.Print($"[Evenmere] Expedition stage: road to lodge {id.Value} ({reversed.Count} tiles) reach={reached}.");
        return true;
    }

    private static bool TryFindHall(SimHost host, out int x, out int y)
    {
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (host.Content.Buildings.NameOf(building.Type) == "founders_hall")
            {
                x = building.Origin.X;
                y = building.Origin.Y;
                return true;
            }
        }

        x = 0;
        y = 0;
        return false;
    }
}
