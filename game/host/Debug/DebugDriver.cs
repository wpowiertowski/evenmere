using System.Globalization;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Game.Ui.Tools;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Godot;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// Runs the debug command-line paths (<see cref="DebugLaunchOptions"/>) next to <see cref="Main"/>: camera presets,
/// the demo road, the picking check and the frame bench. It only ever exists in debug builds; release builds never
/// construct it, so the flags are inert there.
/// </summary>
internal sealed class DebugDriver
{
    private readonly DebugLaunchOptions _options;
    private readonly double[] _benchTimes;
    private readonly Vector2 _checkPickPoint;

    private bool _benchVsyncOff;
    private DisplayServer.VSyncMode _benchVsyncSaved = DisplayServer.VSyncMode.Enabled;

    private int _benchTaken;
    private int _benchWarmup;
    private bool _checkPickDone;
    private CameraRig? _rig;
    private TerrainRenderer? _terrain;
    private SimHost? _host;

    private bool _hasDemo;
    private bool _overlay;
    private int _overlayMatches;
    private long _overlayLoggedTick = -480;
    private readonly Dictionary<EntityId, long> _predictedEnds = new();
    private readonly HashSet<long> _seenPredictions = new();
    private Vector2I _demoCluster = new(30, 96);
    private Vector2I _demoSite = new(30, 96);
    private Vector2I _demoPause = new(30, 96);
    private EntityId _demoSawpit = EntityId.None;
    private TileCoord _demoSawpitOrigin = new(0, 0);
    private int _demoFrames;
    private bool _lateDemoDone;
    private int _pendingHudTipFrames;

    /// <summary>Initializes a new instance of the <see cref="DebugDriver"/> class.</summary>
    /// <param name="options">The parsed debug options.</param>
    public DebugDriver(DebugLaunchOptions options)
    {
        _options = options;

        _benchTimes = [];
        _checkPickPoint = new Vector2(-1f, -1f);
        if (options.BenchFrames is { } benchFrames)
        {
            // The first frames compile shaders and build the island; they are rendered but left out of the stats.
            _benchWarmup = Math.Min(30, benchFrames / 2);
            _benchTimes = new double[Math.Max(1, benchFrames - _benchWarmup)];
        }

        if (!string.IsNullOrEmpty(options.CheckPick))
        {
            string[] parts = options.CheckPick.Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy))
            {
                _checkPickPoint = new Vector2(sx, sy);
            }
            else
            {
                GD.PushWarning($"[Evenmere] ignoring malformed --check-pick '{options.CheckPick}'; want 'sx,sy'.");
            }
        }
    }

    /// <summary>Applies the one-shot debug paths once the world is running: the camera preset, the grid flag and the demo road.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="rig">The camera rig, when the scene has one.</param>
    /// <param name="terrain">The terrain renderer, when the scene has one.</param>
    public void Ready(SimHost host, CameraRig? rig, TerrainRenderer? terrain)
    {
        _rig = rig;
        _terrain = terrain;
        _host = host;

        if (_options.CartOverlay)
        {
            // Subscribed before the demos run, so the jobs their setups create are observed from the first tick.
            _overlay = true;
            host.EventsDrained += OnOverlayEvents;
            GD.Print("[Evenmere] cart-overlay: armed; logging predicted arrivals and visual-vs-event matches.");
        }

        if (!string.IsNullOrEmpty(_options.LoadSave))
        {
            LoadDebugSave(host, _options.LoadSave);
        }

        if (_options.DemoNotifications)
        {
            RunDemoNotifications(host);
        }

        if (_options.DemoBuildings)
        {
            RunDemoBuildings(host);
        }

        if (!string.IsNullOrEmpty(_options.DemoExplain))
        {
            RunDemoExplain(host, rig, _options.DemoExplain);
        }

        if (_options.CartOverlay)
        {
            RunCartFixture(host);
            host.Speed = SimSpeed.X4;
        }

        if (rig is null)
        {
            return;
        }

        if (_options.StaticCamera)
        {
            // Scripted runs share the machine's resting cursor, whose edge would otherwise pan the view by a
            // different amount every run. A still camera frames every screenshot identically.
            rig.InputEnabled = false;
            rig.EdgeScrollEnabled = false;
        }

        if (terrain is not null && _options.ShowGrid)
        {
            terrain.SetGridVisible(true);
        }

        switch (_options.CameraView)
        {
            case "":
                if (_hasDemo)
                {
                    rig.FocusOnTile(new Vector2I(_demoCluster.X, _demoCluster.Y));
                    rig.SetZoom(40f, true);
                }

                break;
            case "demo":
                if (RequireDemo())
                {
                    rig.FocusOnTile(new Vector2I(_demoCluster.X, _demoCluster.Y));
                    rig.SetZoom(40f, true);
                }

                break;
            case "demosite":
                if (RequireDemo())
                {
                    rig.FocusOnTile(new Vector2I(_demoSite.X, _demoSite.Y));
                    rig.SetZoom(16f, true);
                }

                break;
            case "demopause":
                if (RequireDemo())
                {
                    rig.FocusOnTile(new Vector2I(_demoPause.X, _demoPause.Y));
                    rig.SetZoom(16f, true);
                }

                break;
            case "overview":
                rig.FocusOnTile(new Vector2I(64, 62));
                rig.SetZoom(190f, true);
                break;
            case "landing":
                rig.FocusOnTile(new Vector2I(52, 86));
                rig.SetZoom(55f, true);
                break;
            case "slopes":
                rig.FocusOnTile(new Vector2I(72, 48));
                rig.SetZoom(55f, true);
                break;
            case "mouth":
                rig.FocusOnTile(new Vector2I(16, 74));
                rig.SetZoom(42f, true);
                break;
            case "hall":
                rig.FocusOnTile(new Vector2I(34, 97));
                rig.SetZoom(18f, true);
                break;
            default:
                GD.PushWarning($"[Evenmere] unknown --camera-view '{_options.CameraView}'; want 'overview', 'landing', 'slopes' or 'mouth'.");
                break;
        }

        if (_options.DemoDirty)
        {
            LayDemoRoad(host);
        }

        ApplyToolOptions(host);
        ApplyFeedOptions(host);
    }

    /// <summary>Runs the per-frame debug paths: the picking check, then the frame bench.</summary>
    /// <param name="main">The scene root, used to quit once a check prints.</param>
    /// <param name="delta">The real time since the last frame.</param>
    public void Process(Main main, double delta)
    {
        RunCheckPick(main);
        RunLateDemo();
        RunPendingHudTip();
        RunBench(main, delta);
    }

    // The HUD builds its chips a frame after the world starts, so the one-shot show in
    // RunDemoExplain happens too early; re-show the bread tip every frame until the screenshot.
    private void RunPendingHudTip()
    {
        if (_pendingHudTipFrames <= 0 || _host is null)
        {
            return;
        }

        _pendingHudTipFrames--;
        Node? hud = _host.GetTree().CurrentScene?.GetNodeOrNull<Node>("Hud/HudRoot");
        hud?.GetType().GetMethod("DebugShowGoodTip")?.Invoke(hud, ["bread"]);
    }

    // Picks once at a screen point and prints the tile and its rendered surface height, proving the camera's picking
    // lands on the visible terrain (the height comes from the same function the terrain renderer installed).
    private void RunCheckPick(Main main)
    {
        if (_checkPickDone || _checkPickPoint.X < 0f)
        {
            return;
        }

        // Frame 60: the terrain, water and camera have all settled (the island builds on frame 1).
        if (Engine.GetProcessFrames() < 60)
        {
            return;
        }

        _checkPickDone = true;
        if (_rig is null || _terrain is null)
        {
            GD.Print("[Evenmere] PICK no camera or terrain");
        }
        else if (_rig.TryPickTile(_checkPickPoint, out Vector2I tile))
        {
            GD.Print($"[Evenmere] PICK tile=({tile.X}, {tile.Y}) height={_terrain.GetSurfaceHeight(tile.X, tile.Y):F3}");
        }
        else
        {
            GD.Print("[Evenmere] PICK miss");
        }

        main.GetTree().Quit(0);
    }

    private void RunBench(Main main, double delta)
    {
        if (_benchTimes.Length == 0 || _benchTaken >= _benchTimes.Length)
        {
            return;
        }

        if (!_benchVsyncOff)
        {
            // The frame-time baseline must not depend on the display in the room: measure uncapped, then restore.
            _benchVsyncOff = true;
            if (DisplayServer.GetName() != "headless")
            {
                _benchVsyncSaved = (DisplayServer.VSyncMode)DisplayServer.WindowGetVsyncMode((int)DisplayServer.MainWindowId);
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled, (int)DisplayServer.MainWindowId);
            }
        }

        if (_benchWarmup > 0)
        {
            _benchWarmup--;
            return;
        }

        _benchTimes[_benchTaken++] = delta;
        if (_benchTaken < _benchTimes.Length)
        {
            return;
        }

        double[] sorted = (double[])_benchTimes.Clone();
        Array.Sort(sorted);
        double sum = 0.0;
        foreach (double sample in _benchTimes)
        {
            sum += sample;
        }

        double avgMs = (sum / sorted.Length) * 1000.0;
        double p99Ms = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))] * 1000.0;
        double fps = avgMs > 0.0 ? 1000.0 / avgMs : 0.0;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(_benchVsyncSaved, (int)DisplayServer.MainWindowId);
        }

        GD.Print($"BENCH frames={sorted.Length} avg_ms={avgMs:F2} p99_ms={p99Ms:F2} fps={fps:F1}");
        main.GetTree().Quit(0);
    }

    // The cart overlay (M1-22 evidence): every job's predicted arrival tick is its PhaseEndTick, so sampling the active
    // jobs after each tick batch records it, and a TransportJobCompleted whose last recorded end equals its event tick
    // proves the replayed cart arrived on the sim's tick. Completions without an exact match print nothing, so a short
    // phase that a coarse batch stepped over never logs a misleading line.
    private void OnOverlayEvents(IReadOnlyList<SimEvent> events)
    {
        if (!_overlay || _host is null)
        {
            return;
        }

        foreach (TransportJobView job in _host.Query.Logistics.ActiveJobs())
        {
            long packed = ((long)job.Id.Value << 8) | (byte)job.Phase;
            if (_seenPredictions.Add(packed))
            {
                GD.Print($"[Evenmere] cart-overlay: job {job.Id.Value} phase {job.Phase} predicted arrival tick {job.PhaseEndTick}");
            }

            _predictedEnds[job.Id] = job.PhaseEndTick;
        }

        foreach (SimEvent ev in events)
        {
            if (ev is not TransportJobCompleted completed)
            {
                continue;
            }

            if (_predictedEnds.TryGetValue(completed.Job, out long visual) && visual == completed.Tick)
            {
                GD.Print($"[Evenmere] cart-overlay: cart {completed.Job.Value} arrived visually at tick {visual} (event tick {completed.Tick})");
                _overlayMatches++;
            }

            _predictedEnds.Remove(completed.Job);
        }

        // Periodic status so an empty match list still says whether the world ticks and works.
        if (_host.CurrentTick - _overlayLoggedTick >= 480)
        {
            _overlayLoggedTick = _host.CurrentTick;
            int carts = 0;
            foreach (TransportJobView job in _host.Query.Logistics.ActiveJobs())
            {
                carts++;
            }

            GoodId fish = _host.Content.Goods.IdOf("fish");
            GoodId wood = _host.Content.Goods.IdOf("wood");
            GD.Print($"[Evenmere] cart-overlay: tick {_host.CurrentTick} residents={_host.Query.Population.TotalResidents} jobs={carts} matches={_overlayMatches} fishStock={_host.Query.Storage.Stock(fish).Milli} woodStock={_host.Query.Storage.Stock(wood).Milli} fishMade={_host.Query.Production.ProducedYesterday(fish).Milli}.");
        }
    }

    // Minimal logistics fixture for the overlay (M1-22 evidence): the demo's houses advance to Villagers, whose
    // workforce cannot staff the slice producers, so no carts ever run there. Refund three stalled chain buildings,
    // house the refund in locked Settlers beside a fishing hut, and carts keep running. Demo-only, behind --cart-overlay.
    private static void RunCartFixture(SimHost host)
    {
        try
        {
            if (!TryFindHall(host, out TileCoord hallOrigin, out int hallWidth, out int hallHeight))
            {
                GD.Print("[Evenmere] Life: no founders hall; skipping the cart fixture.");
                return;
            }

            var centre = new TileCoord(hallOrigin.X + (hallWidth / 2), hallOrigin.Y + (hallHeight / 2));
            BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
            var network = new HashSet<TileCoord>();
            for (int y = hallOrigin.Y; y < hallOrigin.Y + hallHeight; y++)
            {
                for (int x = hallOrigin.X; x <= hallOrigin.X + hallWidth - 1; x++)
                {
                    network.Add(new TileCoord(x, y));
                }
            }

            for (int y = centre.Y - 45; y <= centre.Y + 45; y++)
            {
                for (int x = centre.X - 45; x <= centre.X + 45; x++)
                {
                    if (host.Query.Roads.RoadAt(x, y) is not null)
                    {
                        network.Add(new TileCoord(x, y));
                    }
                }
            }

            int freed = 0;
            foreach (BuildingInfo building in host.Query.Map.ActiveBuildings)
            {
                if (freed >= 3)
                {
                    break;
                }

                string name = host.Content.Buildings.NameOf(building.Type);
                if (name is "windmill" or "bakery" or "weavers_cottage")
                {
                    host.Enqueue(new Demolish(building.Id));
                    freed++;
                }
            }

            host.RunTicksNow(1);

            if (TryPlaceNear(host, "fishing_hut", centre, network, out EntityId fish, out TileCoord fishOrigin))
            {
                ConnectBuilding(host, fish, fishOrigin, roadType, network);
            }

            int houses = 0;
            for (int n = 0; n < 8 && houses < 5; n++)
            {
                if (!TryPlaceNear(host, "settler_house", centre, network, out EntityId house, out TileCoord houseOrigin))
                {
                    break;
                }

                ConnectBuilding(host, house, houseOrigin, roadType, network);
                if (!host.Query.Roads.IsInStorehouseReach(house))
                {
                    host.Enqueue(new Demolish(house));
                    host.RunTicksNow(1);
                    continue;
                }

                houses++;
            }

            host.RunTicksNow(480);
            int locked = 0;
            foreach (BuildingInfo building in host.Query.Map.ActiveBuildings)
            {
                if (host.Query.Population.TryGetHouse(building.Id, out HouseInfo house) && !house.UpgradeLocked)
                {
                    host.Enqueue(new SetHouseUpgradeLock(building.Id, true));
                    locked++;
                }
            }

            host.RunTicksNow(1);
            GD.Print($"[Evenmere] Life: cart fixture: {houses} new house(s), {locked} locked, fishing hut {fish.Value}.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Life: cart fixture failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private bool RequireDemo()
    {
        if (_hasDemo)
        {
            return true;
        }

        GD.PushWarning("[Evenmere] --camera-view demo views need --demo-buildings; keeping the default view.");
        return false;
    }

    // One of each slice building near the hall with connecting roads (M1-20 evidence): unlocks the slice techs and
    // civic through the real research path, places the buildings cheapest-first through the placement query (so the
    // last ones stall as sites on the 40-timber start stock), connects each with a BFS road planner, pauses one
    // house next to an active one, and pauses the world so screenshots are deterministic.
    private void RunDemoBuildings(SimHost host)
    {
        try
        {
            if (!TryFindHall(host, out TileCoord hallOrigin, out int hallWidth, out int hallHeight))
            {
                GD.Print("[Evenmere] Demo: no founders hall; skipping --demo-buildings.");
                return;
            }

            var centre = new TileCoord(hallOrigin.X + (hallWidth / 2), hallOrigin.Y + (hallHeight / 2));
            UnlockSlice(host);

            BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
            var network = new HashSet<TileCoord>();
            for (int y = hallOrigin.Y; y < hallOrigin.Y + hallHeight; y++)
            {
                for (int x = hallOrigin.X; x < hallOrigin.X + hallWidth; x++)
                {
                    network.Add(new TileCoord(x, y));
                }
            }

            // The pattern goes down first, while the hall surroundings are still empty, so it always lands adjacent
            // to the hall and every later connector joins one connected network.
            LayDemoPattern(host, roadType, hallOrigin, hallWidth, hallHeight, network);

            string[] order =
            [
                "settler_house", "settler_house", "woodcutters_hut", "woodcutters_hut", "sawpit", "fishing_hut",
                "storehouse", "sheep_farm", "weavers_cottage", "grain_farm", "market",
                "windmill", "bakery", "shrine",
            ];
            var placed = new List<(string Type, EntityId Id, TileCoord Origin)>();
            foreach (string typeName in order)
            {
                if (TryPlaceNear(host, typeName, centre, network, out EntityId id, out TileCoord origin))
                {
                    placed.Add((typeName, id, origin));
                    if (typeName == "sawpit" && _demoSawpit.IsNone)
                    {
                        _demoSawpit = id;
                        _demoSawpitOrigin = origin;
                    }
                }
            }

            foreach ((string _, EntityId id, TileCoord origin) in placed)
            {
                ConnectBuilding(host, id, origin, roadType, network);
            }

            host.RunTicksNow(30 * 120);
            PauseSecondHouse(host);
            RecordDemoFocus(host, placed, centre);
            host.Speed = SimSpeed.Paused;
            GD.Print($"[Evenmere] Demo: placed {placed.Count} slice building(s); hash={host.ComputeStateHash():x16}.");
            _hasDemo = true;
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Demo: --demo-buildings failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    // Researches the slice techs and adopts the slice civic through the real commands, in dependency order.
    private static void UnlockSlice(SimHost host)
    {
        CivicId civic = host.Content.Civics.IdOf("hearth_and_shrine");
        if (!host.Query.Culture.IsDone(civic))
        {
            host.Enqueue(new SetCivicTarget(civic));
            int days = 0;
            while (!host.Query.Culture.IsDone(civic) && days < 40)
            {
                host.RunTicksNow(120);
                days++;
            }

            GD.Print($"[Evenmere] Demo: civic hearth_and_shrine done={host.Query.Culture.IsDone(civic)} after {days} day(s).");
        }

        string[] techs = ["animal_husbandry", "weaving", "cultivation", "milling"];
        foreach (string name in techs)
        {
            TechId tech = host.Content.Techs.IdOf(name);
            if (host.Query.Research.IsDone(tech))
            {
                continue;
            }

            host.Enqueue(new SetResearchTarget(tech));
            int days = 0;
            while (!host.Query.Research.IsDone(tech) && days < 200)
            {
                host.RunTicksNow(120);
                days++;
            }

            GD.Print($"[Evenmere] Demo: tech {name} done={host.Query.Research.IsDone(tech)} after {days} day(s).");
        }
    }

    private static bool TryFindHall(SimHost host, out TileCoord origin, out int width, out int height)
    {
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (host.Content.Buildings.NameOf(building.Type) == "founders_hall")
            {
                origin = building.Origin;
                width = building.Width;
                height = building.Height;
                return true;
            }
        }

        origin = default;
        width = 0;
        height = 0;
        return false;
    }

    // Spiral-places one building near a centre through the placement query, cheapest warnings preferred by trying
    // every rotation on each ring tile in order. Runs one tick so the next placement sees this one's occupancy.
    private static bool TryPlaceNear(
        SimHost host, string typeName, TileCoord centre, HashSet<TileCoord> network, out EntityId id, out TileCoord origin)
    {
        BuildingTypeId type = host.Content.Buildings.IdOf(typeName);
        for (int r = 5; r <= 48; r++)
        {
            for (int y = centre.Y - r; y <= centre.Y + r; y++)
            {
                for (int x = centre.X - r; x <= centre.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)) != r)
                    {
                        continue;
                    }

                    for (int rot = 0; rot < 4; rot++)
                    {
                        var rotation = (Rotation)rot;
                        if (!host.Query.Placement.Validate(type, x, y, rotation).CanPlace)
                        {
                            continue;
                        }

                        host.Enqueue(new PlaceBuilding(type, x, y, rotation));
                        host.RunTicksNow(1);
                        id = host.Query.Map.BuildingAt(x, y);
                        origin = new TileCoord(x, y);
                        GD.Print($"[Evenmere] Demo: placed {typeName} at ({x}, {y}) rot={rot} id={id.Value}.");
                        return true;
                    }
                }
            }
        }

        GD.Print($"[Evenmere] Demo: no valid spot for {typeName} near ({centre.X}, {centre.Y}).");
        id = EntityId.None;
        origin = default;
        return false;
    }

    // Lays a cross, a T and a corner right by the hall, so the screenshot shows every auto-connection shape and every
    // building can reach the storehouse over short roads. The row below the hall comes first, then a column crossing
    // it, a branch making the T, and an L off one end of the row; each takes the first valid offset.
    private static void LayDemoPattern(
        SimHost host, BuildingTypeId roadType, TileCoord hallOrigin, int hallWidth, int hallHeight, HashSet<TileCoord> network)
    {
        int hx0 = hallOrigin.X;
        int hy0 = hallOrigin.Y;
        int hx1 = hx0 + hallWidth - 1;
        int hy1 = hy0 + hallHeight - 1;

        TileCoord[]? row = null;
        int rowY = 0;
        for (int dy = 1; dy <= 8 && row is null; dy++)
        {
            TileCoord[] candidate = Row(hx0 - 2, hx1 + 2, hy1 + dy);
            if (host.Query.Placement.ValidateRoad(candidate, roadType).CanPlace)
            {
                row = candidate;
                rowY = hy1 + dy;
            }
        }

        if (row is null)
        {
            GD.Print("[Evenmere] Demo: no room for the pattern cross row by the hall.");
            return;
        }

        TileCoord[]? column = null;
        int colX = 0;
        for (int dx = 1; dx <= 8 && column is null; dx++)
        {
            TileCoord[] candidate = Column(hx1 + dx, hy0 - 2, rowY);
            if (host.Query.Placement.ValidateRoad(candidate, roadType).CanPlace)
            {
                column = candidate;
                colX = hx1 + dx;
            }
        }

        LayPath(host, row, roadType, "pattern cross row", network);
        if (column is null)
        {
            GD.Print("[Evenmere] Demo: no room for the pattern cross column by the hall.");
            return;
        }

        LayPath(host, column, roadType, "pattern cross column", network);

        for (int mx = row[0].X; mx <= row[^1].X; mx++)
        {
            if (mx == colX)
            {
                continue;
            }

            TileCoord[] tee = [new TileCoord(mx, rowY + 1), new TileCoord(mx, rowY + 2), new TileCoord(mx, rowY + 3)];
            if (host.Query.Placement.ValidateRoad(tee, roadType).CanPlace)
            {
                LayPath(host, tee, roadType, "pattern T branch", network);
                break;
            }
        }

        TileCoord[][] corners =
        [
            [new TileCoord(row[0].X - 1, rowY), new TileCoord(row[0].X - 2, rowY), new TileCoord(row[0].X - 2, rowY - 1), new TileCoord(row[0].X - 2, rowY - 2)],
            [new TileCoord(row[^1].X + 1, rowY), new TileCoord(row[^1].X + 2, rowY), new TileCoord(row[^1].X + 2, rowY - 1), new TileCoord(row[^1].X + 2, rowY - 2)],
        ];
        foreach (TileCoord[] corner in corners)
        {
            if (host.Query.Placement.ValidateRoad(corner, roadType).CanPlace)
            {
                LayPath(host, corner, roadType, "pattern corner", network);
                return;
            }
        }

        GD.Print("[Evenmere] Demo: no room for the pattern corner by the hall.");
    }

    private static TileCoord[] Row(int x0, int x1, int y)
    {
        var path = new TileCoord[x1 - x0 + 1];
        for (int i = 0; i < path.Length; i++)
        {
            path[i] = new TileCoord(x0 + i, y);
        }

        return path;
    }

    private static TileCoord[] Column(int x, int y0, int y1)
    {
        var path = new TileCoord[y1 - y0 + 1];
        for (int i = 0; i < path.Length; i++)
        {
            path[i] = new TileCoord(x, y0 + i);
        }

        return path;
    }

    private static void LayPath(SimHost host, TileCoord[] path, BuildingTypeId roadType, string what, HashSet<TileCoord> network, bool joinNetwork = true)
    {
        if (path.Length == 0 || !host.Query.Placement.ValidateRoad(path, roadType).CanPlace)
        {
            GD.Print($"[Evenmere] Demo: could not lay {what}.");
            return;
        }

        host.Enqueue(new PlaceRoad(path, roadType));
        host.RunTicksNow(1);
        if (joinNetwork)
        {
            foreach (TileCoord tile in path)
            {
                network.Add(tile);
            }
        }

        GD.Print($"[Evenmere] Demo: laid {what} ({path.Length} tiles).");
    }

    // Connects one building to the road network with a BFS over tiles that each validate as a road on their own.
    // Neighbours are visited north, east, south, west, so the layout is deterministic.
    private static void ConnectBuilding(SimHost host, EntityId id, TileCoord origin, BuildingTypeId roadType, HashSet<TileCoord> network)
    {
        if (!host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
        {
            return;
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

        foreach (TileCoord start in starts)
        {
            if (network.Contains(start))
            {
                GD.Print($"[Evenmere] Demo: building {id.Value} already touches the road network.");
                return;
            }
        }

        ConnectStarts(host, starts, roadType, network, $"connector to building {id.Value}");
    }

    // Connects road starts to the hall network with a BFS over tiles that each validate as a road on their own.
    // Neighbours are visited north, east, south, west, so the layout is deterministic. The path stops before the
    // network tile (a road or a hall tile), which stays untouched.
    private static bool ConnectStarts(SimHost host, List<TileCoord> starts, BuildingTypeId roadType, HashSet<TileCoord> network, string what)
    {
        var queue = new Queue<TileCoord>(starts);
        var parent = new Dictionary<TileCoord, TileCoord>();
        foreach (TileCoord start in starts)
        {
            parent[start] = start;
        }

        TileCoord[] offsets = [new TileCoord(0, -1), new TileCoord(1, 0), new TileCoord(0, 1), new TileCoord(-1, 0)];
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
                if (parent.ContainsKey(next) || network.Contains(next))
                {
                    if (network.Contains(next) && !parent.ContainsKey(next))
                    {
                        parent[next] = current;
                        goal = next;
                        break;
                    }

                    continue;
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
            GD.Print($"[Evenmere] Demo: no road route for {what}.");
            return false;
        }

        // Walk back from the goal's parent: the goal itself is network (a road or a hall tile) and stays untouched.
        var reversed = new List<TileCoord>();
        TileCoord step = parent[goal.Value];
        while (!parent[step].Equals(step))
        {
            reversed.Add(step);
            step = parent[step];
        }

        reversed.Add(step);
        reversed.Reverse();
        LayPath(host, reversed.ToArray(), roadType, what, network);
        return true;
    }

    // Pauses the second active woodcutter's hut, so the screenshot shows a paused building next to an active one.
    private void PauseSecondHouse(SimHost host)
    {
        var huts = new List<EntityId>();
        foreach (BuildingInfo building in host.Query.Map.ActiveBuildings)
        {
            if (host.Content.Buildings.NameOf(building.Type) == "woodcutters_hut" && building.State == BuildingState.Active)
            {
                huts.Add(building.Id);
            }
        }

        if (huts.Count < 2)
        {
            GD.Print("[Evenmere] Demo: fewer than two active woodcutters huts; skipping the pause contrast.");
            return;
        }

        host.Enqueue(new SetBuildingPaused(huts[1], true));
        host.RunTicksNow(1);
        if (host.Query.Map.TryGetBuilding(huts[1], out BuildingInfo paused))
        {
            _demoPause = new Vector2I(paused.Origin.X, paused.Origin.Y);
            GD.Print($"[Evenmere] Demo: paused woodcutters hut {huts[1].Value} at ({paused.Origin.X}, {paused.Origin.Y}).");
        }
    }

    private void RecordDemoFocus(SimHost host, List<(string Type, EntityId Id, TileCoord Origin)> placed, TileCoord centre)
    {
        int active = 0;
        int sites = 0;
        int paused = 0;
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (building.State == BuildingState.Active)
            {
                active++;
            }
            else if (building.State == BuildingState.Site)
            {
                sites++;
                if (host.Query.Construction.TryGetSite(building.Id, out SiteInfo site))
                {
                    GD.Print($"[Evenmere] Demo: site {host.Content.Buildings.NameOf(building.Type)} {building.Id.Value} stage {site.Stage}/{site.Steps} progress {site.Progress.PerMille / 10}% stall {site.Stall}.");
                }
            }
            else if (building.State == BuildingState.Paused)
            {
                paused++;
            }
        }

        GoodId timber = host.Content.Goods.IdOf("timber");
        GD.Print($"[Evenmere] Demo: states active={active} sites={sites} paused={paused} timber stock={host.Query.Storage.Stock(timber).Milli}.");
        foreach ((string type, EntityId id, TileCoord _) in placed)
        {
            bool access = host.Query.Roads.HasRoadAccess(id);
            bool reach = host.Query.Roads.IsInStorehouseReach(id);
            string cost = host.Query.Roads.TryNearestStorehouse(id, out _, out int c) ? c.ToString() : "none";
            GD.Print($"[Evenmere] Demo: {type} {id.Value} roadAccess={access} reach={reach} cost={cost}.");
        }
        long sumX = centre.X;
        long sumY = centre.Y;
        int count = 1;
        foreach ((string _, EntityId id, TileCoord _) in placed)
        {
            if (host.Query.Map.TryGetBuilding(id, out BuildingInfo building))
            {
                sumX += building.Origin.X;
                sumY += building.Origin.Y;
                count++;
            }
        }

        _demoCluster = new Vector2I((int)(sumX / count), (int)(sumY / count));
        bool foundSite = false;
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (building.State == BuildingState.Site)
            {
                _demoSite = new Vector2I(building.Origin.X, building.Origin.Y);
                foundSite = true;
                break;
            }
        }

        if (!foundSite && !_demoSawpitOrigin.Equals(default(TileCoord)))
        {
            // The late demo rebuilds the sawpit on the spot as the partway site, after the camera is set.
            _demoSite = new Vector2I(_demoSawpitOrigin.X, _demoSawpitOrigin.Y);
        }
    }

    // The M1-24 explain demo: a stalled bakery (no flour because the windmill has no grain) and a
    // house blocked by the bread shortage, for the inspector and HUD screenshots. It builds through
    // SimHost.Enqueue only, pauses the world, then opens the inspector on the bakery, the house,
    // or shows the HUD bread tooltip, per the --demo-explain value.
    private void RunDemoExplain(SimHost host, CameraRig? rig, string mode)
    {
        try
        {
            if (!TryFindHall(host, out TileCoord hallOrigin, out int hallWidth, out int hallHeight))
            {
                GD.Print("[Evenmere] Demo: no founders hall; skipping --demo-explain.");
                return;
            }

            var centre = new TileCoord(hallOrigin.X + (hallWidth / 2), hallOrigin.Y + (hallHeight / 2));
            UnlockSlice(host);

            BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
            var network = new HashSet<TileCoord>();
            for (int y = hallOrigin.Y; y < hallOrigin.Y + hallHeight; y++)
            {
                for (int x = hallOrigin.X; x < hallOrigin.X + hallWidth; x++)
                {
                    network.Add(new TileCoord(x, y));
                }
            }

            LayDemoPattern(host, roadType, hallOrigin, hallWidth, hallHeight, network);

            string[] first = ["woodcutters_hut", "sawpit", "fishing_hut", "storehouse"];
            var placed = new List<(string Type, EntityId Id, TileCoord Origin)>();
            foreach (string typeName in first)
            {
                if (TryPlaceNear(host, typeName, centre, network, out EntityId id, out TileCoord origin))
                {
                    placed.Add((typeName, id, origin));
                    ConnectBuilding(host, id, origin, roadType, network);
                }
            }

            host.RunTicksNow(10 * 120);

            string[] second = ["market", "settler_house", "settler_house", "bakery", "windmill"];
            foreach (string typeName in second)
            {
                if (TryPlaceNear(host, typeName, centre, network, out EntityId id, out TileCoord origin))
                {
                    placed.Add((typeName, id, origin));
                    ConnectBuilding(host, id, origin, roadType, network);
                }
            }

            host.RunTicksNow(10 * 120);

            string[] third = ["shrine", "sheep_farm", "weavers_cottage"];
            foreach (string typeName in third)
            {
                if (TryPlaceNear(host, typeName, centre, network, out EntityId id, out TileCoord origin))
                {
                    placed.Add((typeName, id, origin));
                    ConnectBuilding(host, id, origin, roadType, network);
                }
            }

            // No grain farm on purpose: the windmill starves on grain, the bakery starves on flour,
            // and the houses stall on bread. Twenty more days let the sites finish and the chain stall.
            host.RunTicksNow(20 * 120);

            EntityId bakeryId = EntityId.None;
            EntityId houseId = EntityId.None;
            foreach ((string type, EntityId id, TileCoord _) in placed)
            {
                if (type == "bakery" && bakeryId.IsNone)
                {
                    bakeryId = id;
                }

                if (type == "settler_house" && houseId.IsNone)
                {
                    houseId = id;
                }
            }

            GoodId bread = host.Content.Goods.IdOf("bread");
            GoodId flour = host.Content.Goods.IdOf("flour");
            Explanation breadExplain = host.Query.Explain.ExplainGood(bread);
            GD.Print($"[Evenmere] Demo: bread {breadExplain.Root.Key} children={breadExplain.Root.Children.Length}.");
            foreach (ExplanationLine child in breadExplain.Root.Children)
            {
                GD.Print($"[Evenmere] Demo: bread child {child.Key} args={child.Args.Length} subjects={child.Subjects.Length}.");
            }

            if (host.Query.Production.TryGet(bakeryId, out ProductionInfo production))
            {
                GD.Print($"[Evenmere] Demo: bakery {bakeryId.Value} stall={production.Stall} productivity={production.Productivity.PerMille}.");
            }

            if (host.Query.Population.TryGetHouse(houseId, out HouseInfo house))
            {
                GD.Print($"[Evenmere] Demo: house {houseId.Value} tier={host.Content.NameOf(house.Tier)} residents={house.Residents} blocker={house.AdvanceBlocker} decline={house.DeclineTimerDays}.");
            }

            host.Speed = SimSpeed.Paused;
            _hasDemo = true;

            Node? scene = host.GetTree().CurrentScene;
            Node? inspector = scene?.GetNodeOrNull<Node>("InspectorPanel");
            System.Reflection.MethodInfo? inspectorOpen = inspector?.GetType().GetMethod("Open");
            if (mode == "house" && !houseId.IsNone)
            {
                if (host.Query.Map.TryGetBuilding(houseId, out BuildingInfo home) && rig is not null)
                {
                    rig.FocusOnTile(new Vector2I(home.Origin.X, home.Origin.Y));
                    rig.SetZoom(16f, true);
                }

                inspectorOpen?.Invoke(inspector, [houseId]);
                GD.Print($"[Evenmere] Demo: subject link building {houseId.Value} followed; camera focused, inspector opened.");
            }
            else if (mode == "hud")
            {
                if (host.Query.Map.TryGetBuilding(bakeryId, out BuildingInfo bakery) && rig is not null)
                {
                    rig.FocusOnTile(new Vector2I(bakery.Origin.X, bakery.Origin.Y));
                    rig.SetZoom(40f, true);
                }

                Node? hud = scene?.GetNodeOrNull<Node>("Hud/HudRoot");
                hud?.GetType().GetMethod("DebugShowGoodTip")?.Invoke(hud, ["bread"]);
                // The HUD builds its chips a frame after the world starts, so keep showing the tip
                // until the screenshot frame; each call re-reads the live explanation.
                _pendingHudTipFrames = 300;
                // Follow one good subject link in the log: flour explains the bakery stall.
                Explanation flourExplain = host.Query.Explain.ExplainGood(flour);
                GD.Print($"[Evenmere] Demo: subject link good flour followed; ExplainGood(flour) root={flourExplain.Root.Key} children={flourExplain.Root.Children.Length}.");
            }
            else
            {
                if (host.Query.Map.TryGetBuilding(bakeryId, out BuildingInfo bakery) && rig is not null)
                {
                    rig.FocusOnTile(new Vector2I(bakery.Origin.X, bakery.Origin.Y));
                    rig.SetZoom(16f, true);
                }

                inspectorOpen?.Invoke(inspector, [bakeryId]);
                Explanation flourExplain2 = host.Query.Explain.ExplainGood(flour);
                GD.Print($"[Evenmere] Demo: subject link good flour followed; ExplainGood(flour) root={flourExplain2.Root.Key} children={flourExplain2.Root.Children.Length}.");
            }

            GD.Print($"[Evenmere] Demo: --demo-explain={mode} ready; hash={host.ComputeStateHash():x16}.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Demo: --demo-explain failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    // After the first frames rendered (so the incremental rebuilds log), demolishes the sawpit for a half refund and
    // rebuilds it on the spot: the pool cannot cover the full bill, so it stalls partway as the evidence site with
    // scaffolding and a half-scaled body. Skipped while benching so the frame times stay a steady state.
    private void RunLateDemo()
    {
        if (!_hasDemo || _lateDemoDone || _benchTimes.Length > 0 || _host is null)
        {
            return;
        }

        _demoFrames++;
        if (_demoFrames < 50)
        {
            return;
        }

        _lateDemoDone = true;
        try
        {
            if (_demoSawpit.IsNone || !_host.Query.Map.TryGetBuilding(_demoSawpit, out _))
            {
                GD.Print("[Evenmere] Demo: no spare sawpit to rebuild as a site.");
                return;
            }

            _host.Enqueue(new Evenmere.Sim.Systems.Construction.Commands.Demolish(_demoSawpit));
            _host.RunTicksNow(1);
            GD.Print($"[Evenmere] Demo: demolished spare sawpit {_demoSawpit.Value} for its refund.");

            BuildingTypeId sawpit = _host.Content.Buildings.IdOf("sawpit");
            _host.Enqueue(new PlaceBuilding(sawpit, _demoSawpitOrigin.X, _demoSawpitOrigin.Y, Rotation.Deg0));
            _host.RunTicksNow(1);
            if (RebuiltIdAt(_host, _demoSawpitOrigin) is { } rebuilt)
            {
                GD.Print($"[Evenmere] Demo: rebuilding sawpit {rebuilt.Value} stalls partway on the half refund.");
            }

            LayDemoStub(_demoSawpitOrigin.X, _demoSawpitOrigin.Y);
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Demo: late demo failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private static EntityId? RebuiltIdAt(SimHost host, TileCoord tile)
    {
        EntityId id = host.Query.Map.BuildingAt(tile.X, tile.Y);
        return id.IsNone ? null : id;
    }

    // Lays a short stub road by the rebuilt sawpit, so the log shows an incremental road-chunk rebuild too.
    private void LayDemoStub(int x, int y)
    {
        if (_host is null)
        {
            return;
        }

        BuildingTypeId road = _host.Content.Buildings.IdOf("dirt_road");
        TileCoord[][] candidates =
        [
            [new TileCoord(x, y - 1), new TileCoord(x + 1, y - 1), new TileCoord(x + 2, y - 1)],
            [new TileCoord(x, y + 2), new TileCoord(x + 1, y + 2), new TileCoord(x + 2, y + 2)],
            [new TileCoord(x - 1, y), new TileCoord(x - 1, y + 1), new TileCoord(x - 1, y + 2)],
            [new TileCoord(x + 2, y), new TileCoord(x + 2, y + 1), new TileCoord(x + 2, y + 2)],
        ];

        foreach (TileCoord[] stub in candidates)
        {
            if (!_host.Query.Placement.ValidateRoad(stub, road).CanPlace)
            {
                continue;
            }

            _host.Enqueue(new PlaceRoad(stub, road));
            _host.RunTicksNow(1);
            GD.Print("[Evenmere] Demo: laid late stub road by the rebuilt sawpit.");
            return;
        }

        GD.Print("[Evenmere] Demo: no room for the late stub road.");
    }

    // Lays a short debug road on free plains near the hall, so a dirty rebuild can be watched in the log. It searches
    // few candidate rows through the placement query and places the first fully valid path.
    private static void LayDemoRoad(SimHost host)
    {
        try
        {
            BuildingTypeId roadType = host.Content.Buildings.IdOf("dirt_road");
            for (int y = 88; y <= 100; y++)
            {
                for (int x = 34; x <= 52; x++)
                {
                    var path = new TileCoord[] { new(x, y), new(x + 1, y), new(x + 2, y), new(x + 3, y), new(x + 4, y), new(x + 5, y) };
                    if (host.Query.Placement.ValidateRoad(path, roadType).CanPlace)
                    {
                        host.Enqueue(new Evenmere.Sim.Systems.Roads.Commands.PlaceRoad(path, roadType));
                        GD.Print($"[Evenmere] Demo: placed debug road at ({x}, {y}).");
                        return;
                    }
                }
            }

            GD.Print("[Evenmere] Demo: no valid debug road found near the Landing.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Demo: debug road failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    // Starts the placement tools for the M1-21 screenshots: `--tool-build=ID`, `--tool-road=ID` or `--tool-demolish`
    // picks the tool, `--tool-tile` / `--tool-anchor` pin the hover and the drag, and `--tool-spot` finds a tile by
    // itself (valid, invalid or warning). `--tool-invalid` forces a rejection for the toast shot and `--tool-gamepad`
    // shows the virtual cursor ring with no pad attached.
    private void ApplyToolOptions(SimHost host)
    {
        bool wantsTool = !string.IsNullOrEmpty(_options.ToolBuild) || !string.IsNullOrEmpty(_options.ToolRoad)
            || _options.ToolDemolish || !string.IsNullOrEmpty(_options.ToolSpot) || _options.ToolInvalid || _options.ToolGamepad;
        if (!wantsTool && string.IsNullOrEmpty(_options.ToolTile) && string.IsNullOrEmpty(_options.ToolAnchor))
        {
            return;
        }

        if (host.GetTree().CurrentScene?.GetNodeOrNull<ToolController>("Tools") is not { } tools)
        {
            GD.Print("[Evenmere] Tools: no Tools node; skipping --tool-* flags.");
            return;
        }

        BuildingTypeId buildType = DefaultDebugBuildType(host);
        bool roadMode = false;
        if (!string.IsNullOrEmpty(_options.ToolBuild) && TryBuildingId(host, _options.ToolBuild, out BuildingTypeId build))
        {
            tools.BeginBuild(build);
            buildType = build;
        }
        else if (!string.IsNullOrEmpty(_options.ToolRoad) && TryBuildingId(host, _options.ToolRoad, out BuildingTypeId road))
        {
            tools.BeginRoad(road);
            roadMode = true;
        }
        else if (_options.ToolDemolish || (!string.IsNullOrEmpty(_options.ToolSpot) && string.IsNullOrEmpty(_options.ToolBuild) && string.IsNullOrEmpty(_options.ToolRoad)))
        {
            tools.BeginDemolish();
        }

        if (!string.IsNullOrEmpty(_options.ToolSpot))
        {
            ApplyToolSpot(host, tools, buildType, roadMode);
        }
        else
        {
            if (TryParseTile(_options.ToolTile, out TileCoord tile))
            {
                tools.DebugPinHover(tile);
                GD.Print($"[Evenmere] Tools: pinned hover ({tile.X}, {tile.Y}).");
            }

            if (roadMode && TryParseTile(_options.ToolAnchor, out TileCoord anchor) && TryParseTile(_options.ToolTile, out TileCoord current))
            {
                tools.DebugSetRoadDrag(anchor, current);
                GD.Print($"[Evenmere] Tools: road drag ({anchor.X}, {anchor.Y})..({current.X}, {current.Y}).");
            }
        }

        if (_options.ToolInvalid)
        {
            tools.DebugDemoReject();
        }

        if (_options.ToolGamepad)
        {
            VirtualCursor? cursor = host.GetTree().CurrentScene?.GetNodeOrNull<VirtualCursor>("CameraRig/VirtualCursor");
            if (cursor is null)
            {
                GD.Print("[Evenmere] Tools: no virtual cursor; skipping --tool-gamepad.");
            }
            else
            {
                cursor.SetActive(true);
                GD.Print(cursor.TryGetTile(out Vector2I cursorTile)
                    ? $"[Evenmere] Tools: gamepad cursor at ({cursorTile.X}, {cursorTile.Y})."
                    : "[Evenmere] Tools: gamepad cursor shown but off the map.");
            }
        }
    }

    // Pins the hover (and the road drag) from a want instead of coordinates: with the demolish tool it pins the
    // Founders' Hall, whose hover already says it is protected; with the road tool it finds a draggable L path.
    private void ApplyToolSpot(SimHost host, ToolController tools, BuildingTypeId buildType, bool roadMode)
    {
        if (tools.Mode == ToolMode.Demolish)
        {
            if (TryFindHall(host, out TileCoord origin, out _, out _))
            {
                tools.DebugPinHover(origin);
                GD.Print($"[Evenmere] Tools: pinned demolish hover on the hall ({origin.X}, {origin.Y}).");
            }

            return;
        }

        if (roadMode)
        {
            if (FindRoadSpot(host, out TileCoord anchor, out TileCoord current))
            {
                tools.DebugSetRoadDrag(anchor, current);
                tools.DebugPinHover(current);
                GD.Print($"[Evenmere] Tools: road drag ({anchor.X}, {anchor.Y})..({current.X}, {current.Y}).");
            }
            else
            {
                GD.Print("[Evenmere] Tools: no draggable road spot; pass --tool-anchor and --tool-tile instead.");
            }

            return;
        }

        if (FindBuildSpot(host, buildType, _options.ToolSpot, out TileCoord tile))
        {
            tools.DebugPinHover(tile);
            GD.Print($"[Evenmere] Tools: pinned {_options.ToolSpot} spot ({tile.X}, {tile.Y}).");
        }
        else
        {
            GD.Print($"[Evenmere] Tools: no {_options.ToolSpot} spot; pass --tool-tile instead.");
        }
    }

    // The first buildable the island lists, for the invalid-placement toast when no build tool is running.
    private static BuildingTypeId DefaultDebugBuildType(SimHost host)
    {
        foreach (string name in host.Content.Buildings.Names)
        {
            BuildingTypeId id = host.Content.Buildings.IdOf(name);
            if (!host.Content.Buildings.Get(id).RoadCost.HasValue)
            {
                return id;
            }
        }

        return host.Content.Buildings.IdOf(host.Content.Buildings.Names[0]);
    }

    private static bool TryBuildingId(SimHost host, string name, out BuildingTypeId id)
    {
        try
        {
            id = host.Content.Buildings.IdOf(name);
            return true;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
        {
            GD.Print($"[Evenmere] Tools: unknown building '{name}'; skipping the tool flag.");
            id = default;
            return false;
        }
    }

    private static bool TryParseTile(string text, out TileCoord tile)
    {
        tile = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
        {
            tile = new TileCoord(x, y);
            return true;
        }

        GD.Print($"[Evenmere] Tools: ignoring malformed tile '{text}'; want 'x,y'.");
        return false;
    }

    // Spirals out from the hall for the first tile that matches the want: placeable with no warnings (valid),
    // refused (refused), or placeable with a warning, preferring no road access (warning). An invalid search prefers
    // open ground so the red ghost reads, falling back to an occupied tile. A valid search that finds no warning-free
    // tile relaxes to the nearest placeable tile. A forest search takes the first placeable tile on forest terrain,
    // for the ghost-on-forest shot. Rotations are tried in order, so the run frames the same tile every time.
    private static bool FindBuildSpot(SimHost host, BuildingTypeId type, string want, out TileCoord tile)
    {
        tile = default;
        TileCoord centre = FindHallCentre(host);
        TileCoord fallback = default;
        bool hasFallback = false;
        for (int r = 0; r <= 64; r++)
        {
            for (int y = centre.Y - r; y <= centre.Y + r; y++)
            {
                for (int x = centre.X - r; x <= centre.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)) != r)
                    {
                        continue;
                    }

                    for (int rot = 0; rot < 4; rot++)
                    {
                        var rotation = (Rotation)rot;
                        PlacementResult result = host.Query.Placement.Validate(type, x, y, rotation);
                        bool match = want switch
                        {
                            "valid" => result.CanPlace && result.Warnings.IsEmpty,
                            "invalid" => !result.CanPlace && host.Query.Map.BuildingAt(x, y).IsNone,
                            "warning" => result.CanPlace && !result.Warnings.IsEmpty && result.Has(PlacementIssue.NoRoadAccess),
                            "forest" => result.CanPlace && result.Warnings.IsEmpty && IsForest(host, x, y),
                            _ => false,
                        };
                        if (match)
                        {
                            tile = new TileCoord(x, y);
                            return true;
                        }

                        if (!hasFallback)
                        {
                            if (result.CanPlace && !result.Warnings.IsEmpty)
                            {
                                fallback = new TileCoord(x, y);
                                hasFallback = true;
                            }
                            else if (want == "invalid" && !result.CanPlace)
                            {
                                fallback = new TileCoord(x, y);
                                hasFallback = true;
                            }
                            else if (want == "forest" && result.CanPlace && IsForest(host, x, y))
                            {
                                fallback = new TileCoord(x, y);
                                hasFallback = true;
                            }
                        }
                    }
                }
            }
        }

        // A warning anywhere beats no warning, and an occupied tile beats no red ghost: a valid search relaxes to
        // the nearest warned tile, a warning search keeps the first warned tile it saw, and an invalid search keeps
        // the first refused tile.
        if (hasFallback && want is "valid" or "warning" or "invalid" or "forest")
        {
            tile = fallback;
            return true;
        }

        return false;
    }

    // The first start near the hall whose east-six, south-four L path validates as road. It first tries a fixed
    // patch south of the hall, which the hall mesh never occludes from the camera, then spirals out.
    private static bool FindRoadSpot(SimHost host, out TileCoord anchor, out TileCoord current)
    {
        anchor = default;
        current = default;
        BuildingTypeId road = host.Content.Buildings.IdOf("dirt_road");
        TileCoord centre = FindHallCentre(host);
        var path = new TileCoord[16];
        if (TryFindHall(host, out TileCoord hallOrigin, out _, out _))
        {
            var south = new TileCoord(hallOrigin.X - 2, hallOrigin.Y + 4);
            var southEnd = new TileCoord(south.X + 6, south.Y + 4);
            if (southEnd.IsInside(host.Query.Map.Width, host.Query.Map.Height)
                && ToolPaths.BuildLPath(south, southEnd, false, path.AsSpan()) == 11
                && host.Query.Placement.ValidateRoad(path.AsSpan(0, 11), road).CanPlace)
            {
                anchor = south;
                current = southEnd;
                return true;
            }
        }

        for (int r = 4; r <= 64; r++)
        {
            for (int y = centre.Y - r; y <= centre.Y + r; y++)
            {
                for (int x = centre.X - r; x <= centre.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)) != r)
                    {
                        continue;
                    }

                    var start = new TileCoord(x, y);
                    var end = new TileCoord(x + 6, y + 4);
                    if (!end.IsInside(host.Query.Map.Width, host.Query.Map.Height))
                    {
                        continue;
                    }

                    int count = ToolPaths.BuildLPath(start, end, false, path.AsSpan());
                    if (count == 11 && host.Query.Placement.ValidateRoad(path.AsSpan(0, count), road).CanPlace)
                    {
                        anchor = start;
                        current = end;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsForest(SimHost host, int x, int y)
    {
        if (x < 0 || y < 0 || x >= host.Query.Map.Width || y >= host.Query.Map.Height)
        {
            return false;
        }

        TileInfo tile = host.Query.Map.GetTile(x, y);
        return host.Content.Terrains.NameOf(tile.Terrain) == "forest";
    }

    private static TileCoord FindHallCentre(SimHost host)
    {
        if (TryFindHall(host, out TileCoord origin, out int width, out int height))
        {
            return new TileCoord(origin.X + (width / 2), origin.Y + (height / 2));
        }

        return new TileCoord(host.Query.Map.Width / 2, host.Query.Map.Height / 2);
    }

    // Loads a save in-game for the evidence runs (M1-26): --load-save=tests/fixtures/saves/v1-slice.evsave.
    // A relative path is tried as given, then against the ancestors of the working directory, so the flag works
    // whether Godot runs from game/ or from the repo root.
    private static void LoadDebugSave(SimHost host, string path)
    {
        try
        {
            string resolved = ResolveDebugPath(path);
            SaveReport report = host.LoadGame(resolved);
            GD.Print($"[Evenmere] Debug: loaded save '{resolved}' hash={host.ComputeStateHash():x16} remapped={report.Remapped.Count} ruins={report.Ruins.Count} dropped={report.Dropped.Count}.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Debug: --load-save failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private static string ResolveDebugPath(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path))
        {
            return path;
        }

        string? dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 4 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return path;
    }

    // The M1-26 evidence setup: three houses (one batched completion toast) plus a sawpit with no road access
    // (a NoRoadAccess advice pin), then the oldest advice dismissed for the dismissal evidence. The world is paused
    // afterwards so the toasts stay up for the screenshots.
    private static void RunDemoNotifications(SimHost host)
    {
        try
        {
            TileCoord centre = FindHallCentre(host);
            BuildingTypeId road = host.Content.Buildings.IdOf("dirt_road");
            var network = new HashSet<TileCoord>();
            if (TryFindHall(host, out TileCoord hallOrigin, out int hallWidth, out int hallHeight))
            {
                for (int y = hallOrigin.Y; y < hallOrigin.Y + hallHeight; y++)
                {
                    for (int x = hallOrigin.X; x < hallOrigin.X + hallWidth; x++)
                    {
                        network.Add(new TileCoord(x, y));
                    }
                }
            }

            // The houses join the road network (sites only build in storehouse reach); the sawpit stays
            // road-less on purpose, so it raises advice.
            var houses = new List<EntityId>();
            for (int i = 0; i < 3; i++)
            {
                if (TryPlaceNear(host, "settler_house", centre, network, out EntityId id, out TileCoord origin))
                {
                    houses.Add(id);
                    ConnectBuilding(host, id, origin, road, network);
                }
            }

            int placed = houses.Count;

            // The sawpit goes far from the hall's roads on purpose: without road access it raises advice.
            TileCoord far = new(centre.X, centre.Y);
            BuildingTypeId sawpit = host.Content.Buildings.IdOf("sawpit");
            bool sawpitPlaced = false;
            for (int r = 20; r <= 60 && !sawpitPlaced; r++)
            {
                for (int y = centre.Y - r; y <= centre.Y + r && !sawpitPlaced; y++)
                {
                    for (int x = centre.X - r; x <= centre.X + r && !sawpitPlaced; x++)
                    {
                        if (Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)) != r)
                        {
                            continue;
                        }

                        if (host.Query.Placement.Validate(sawpit, x, y, Rotation.Deg0).CanPlace)
                        {
                            host.Enqueue(new PlaceBuilding(sawpit, x, y, Rotation.Deg0));
                            host.RunTicksNow(1);
                            far = new TileCoord(x, y);
                            sawpitPlaced = true;
                            GD.Print($"[Evenmere] Debug: placed road-less sawpit at ({x}, {y}) for advice evidence.");
                        }
                    }
                }
            }

            // Run days until the new sites finish (same-day completions batch into one toast), then two more days
            // so the advisors notice the road-less sawpit.
            int waited = 0;
            while (waited < 20 && CountSites(host) > 0)
            {
                host.RunTicksNow(120);
                waited++;
            }

            foreach (BuildingInfo building in host.Query.Map.Buildings)
            {
                if (building.State == BuildingState.Site)
                {
                    string stall = host.Query.Construction.TryGetSite(building.Id, out SiteInfo site) ? site.Stall.ToString() : "unknown";
                    GD.Print($"[Evenmere] Debug: leftover site {host.Content.NameOf(building.Type)} {building.Id.Value} stall={stall} reach={host.Query.Roads.IsInStorehouseReach(building.Id)}.");
                }
            }

            host.RunTicksNow(2 * 120);

            int active = 0;
            int oldest = -1;
            foreach (AdviceView advice in host.Query.Advisors.Active())
            {
                active++;
                if (oldest < 0 || advice.Id < oldest)
                {
                    oldest = advice.Id;
                }
            }

            GD.Print($"[Evenmere] Debug: --demo-notifications placed {placed} house(s) sawpit={sawpitPlaced} after {waited} day(s); advice active={active}.");

            if (oldest >= 0)
            {
                host.Enqueue(new DismissAdvice(oldest));
                host.RunTicksNow(1);
                int left = 0;
                foreach (AdviceView _ in host.Query.Advisors.Active())
                {
                    left++;
                }

                GD.Print($"[Evenmere] Debug: DismissAdvice {oldest} enqueued; note gone, {left} advice note(s) left.");
            }

            host.Speed = SimSpeed.Paused;
            GD.Print($"[Evenmere] Debug: --demo-notifications ready; hash={host.ComputeStateHash():x16}.");
        }
        catch (Exception ex)
        {
            GD.Print($"[Evenmere] Debug: --demo-notifications failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private static int CountSites(SimHost host)
    {
        int sites = 0;
        foreach (BuildingInfo building in host.Query.Map.Buildings)
        {
            if (building.State == BuildingState.Site)
            {
                sites++;
            }
        }

        return sites;
    }

    // Scripts the feed for the evidence screenshots: history:<filter> opens the history with a filter applied,
    // and click focuses the newest building notification (logging the inspector open).
    private void ApplyFeedOptions(SimHost host)
    {
        if (string.IsNullOrEmpty(_options.DemoFeed))
        {
            return;
        }

        if (host.GetTree().CurrentScene?.GetNodeOrNull<Node>("NotificationFeed") is not { } feed)
        {
            GD.Print("[Evenmere] Debug: no NotificationFeed node; skipping --demo-feed.");
            return;
        }

        string mode = _options.DemoFeed;
        if (mode == "click")
        {
            feed.GetType().GetMethod("DebugClickNewestBuilding")?.Invoke(feed, []);
        }
        else if (mode == "collapse")
        {
            feed.GetType().GetMethod("DebugSetAdviceCollapsed")?.Invoke(feed, [true]);
        }
        else if (mode.StartsWith("history:", StringComparison.Ordinal))
        {
            feed.GetType().GetMethod("DebugShowHistory")?.Invoke(feed, [mode["history:".Length..]]);
        }
        else
        {
            GD.Print($"[Evenmere] Debug: ignoring malformed --demo-feed '{mode}'; want 'history:<filter>', 'click' or 'collapse'.");
        }
    }
}
