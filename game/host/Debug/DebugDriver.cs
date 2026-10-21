using System.Globalization;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
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

        if (_options.DemoBuildings)
        {
            RunDemoBuildings(host);
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
            default:
                GD.PushWarning($"[Evenmere] unknown --camera-view '{_options.CameraView}'; want 'overview', 'landing', 'slopes' or 'mouth'.");
                break;
        }

        if (_options.DemoDirty)
        {
            LayDemoRoad(host);
        }
    }

    /// <summary>Runs the per-frame debug paths: the picking check, then the frame bench.</summary>
    /// <param name="main">The scene root, used to quit once a check prints.</param>
    /// <param name="delta">The real time since the last frame.</param>
    public void Process(Main main, double delta)
    {
        RunCheckPick(main);
        RunLateDemo();
        RunBench(main, delta);
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
}
