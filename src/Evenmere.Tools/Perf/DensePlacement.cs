using System.Collections.Immutable;
using System.Text;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Perf;

/// <summary>
/// How much <see cref="DensePlacement"/> builds: the seed and island size, the house target, the upfront
/// grants that keep sites and residents supplied, and how long the town settles before the save is written.
/// </summary>
/// <param name="Seed">The world seed: the same seed and plan give byte-identical saves.</param>
/// <param name="MapSize">The generated island size: 256, 320 or 384.</param>
/// <param name="Houses">How many settler houses to place (six residents each once filled).</param>
/// <param name="Storehouses">How many storehouses to spread on the lattice.</param>
/// <param name="Markets">How many markets to spread on the lattice.</param>
/// <param name="Woodcutters">How many woodcutter huts to place.</param>
/// <param name="Sawpits">How many sawpits to place.</param>
/// <param name="TimberGrant">How many whole units of timber to grant up front for all construction.</param>
/// <param name="FishGrant">How many whole units of fish to grant up front for the settle run and beyond.</param>
/// <param name="WoodGrant">How many whole units of wood to grant up front for the settle run and beyond.</param>
/// <param name="SettleTicks">How many ticks to run after the residents move in.</param>
public sealed record DensePlacementPlan(
    ulong Seed,
    int MapSize,
    int Houses,
    int Storehouses,
    int Markets,
    int Woodcutters,
    int Sawpits,
    int TimberGrant,
    int FishGrant,
    int WoodGrant,
    int SettleTicks);

/// <summary>How a <see cref="DensePlacement"/> run ended, for tuning and for the perf report.</summary>
/// <param name="HousesPlaced">How many <c>BuildingPlaced</c> events the house batches raised.</param>
/// <param name="ProducersPlaced">How many <c>BuildingPlaced</c> events the producer batches raised.</param>
/// <param name="ServicesPlaced">How many <c>BuildingPlaced</c> events the service batches raised.</param>
/// <param name="Rejections">The rejection histogram over the whole build: <c>"Code: detail"</c> to count, in ordinal order.</param>
/// <param name="SitesLeft">How many construction sites were still unfinished when the settle run ended.</param>
/// <param name="FinalTick">The tick the settled simulation stopped at.</param>
/// <param name="FinalResidents">The total residents at the end.</param>
/// <param name="FinalHash">The state hash at the end, 16 lowercase hex digits.</param>
public sealed record DensePlacementReport(
    int HousesPlaced,
    int ProducersPlaced,
    int ServicesPlaced,
    ImmutableArray<(string Reason, int Count)> Rejections,
    int SitesLeft,
    long FinalTick,
    int FinalResidents,
    string FinalHash);

/// <summary>
/// Builds the perf reference saves: a dense district placed through real commands
/// (<see cref="PlaceBuilding"/> and <see cref="PlaceRoad"/>) on a real new game, with materials granted
/// up front, houses filled, and the town ticked until it settles. The saves are synthetic stress fixtures
/// (their header says provisional), not grown towns: M2-17b replaces <c>perf-2k</c> with a bot-grown save.
/// The same seed and plan give byte-identical saves: the scan orders are fixed, tick counts are fixed,
/// grant amounts are fixed, and no wall-clock input feeds the state.
/// </summary>
public static class DensePlacement
{
    /// <summary>Gets the world seed the <c>perf-2k</c> save is built with.</summary>
    public const ulong Perf2kSeed = 42;

    /// <summary>Gets the island size the <c>perf-2k</c> save is built on.</summary>
    public const int Perf2kSize = 256;

    /// <summary>Gets the map ID the synthesised 2k island is loaded under.</summary>
    public const string Perf2kMapId = "perf_2k_island";

    /// <summary>Gets the world seed the <c>perf-20k</c> save is built with: the largest central landmass in seeds 1..200.</summary>
    public const ulong Perf20kSeed = 104;

    /// <summary>Gets the island size the <c>perf-20k</c> save is built on: the largest generated size (ADR-0019).</summary>
    public const int Perf20kSize = 384;

    /// <summary>Gets the map ID the synthesised 20k island is loaded under.</summary>
    public const string Perf20kMapId = "perf_20k_island";

    /// <summary>
    /// Gets the game-version text the perf saves are written with. Both reference saves are provisional
    /// (M2-17b replaces <c>perf-2k</c> with a bot-grown save), which the file names cannot say, so the
    /// metadata says it. The saves also carry the cheats flag, honestly: they were built with cheats.
    /// </summary>
    public const string PerfGameVersion = "M2-16-provisional";

    /// <summary>How many placements go into one tick: small enough that the rejection histogram reads clearly.</summary>
    internal const int BatchSize = 25;

    /// <summary>Gets the plan the <c>perf-2k</c> save (about 2,000 residents) is built with.</summary>
    public static DensePlacementPlan Perf2kPlan { get; } = new(
        Seed: Perf2kSeed,
        MapSize: Perf2kSize,
        Houses: 360,
        Storehouses: 24,
        Markets: 24,
        Woodcutters: 10,
        Sawpits: 8,
        TimberGrant: 8000,
        FishGrant: 8000,
        WoodGrant: 8000,
        SettleTicks: 1200);

    /// <summary>Gets the plan the <c>perf-20k</c> save is built with (residents as the island allows, up to about 20,000).</summary>
    public static DensePlacementPlan Perf20kPlan { get; } = new(
        Seed: Perf20kSeed,
        MapSize: Perf20kSize,
        Houses: 3350,
        Storehouses: 480,
        Markets: 480,
        Woodcutters: 40,
        Sawpits: 30,
        TimberGrant: 40000,
        FishGrant: 60000,
        WoodGrant: 60000,
        SettleTicks: 1200);

    /// <summary>Builds the <c>perf-2k</c> world: a synthesised 256 island grown to about 2,000 residents.</summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="scratchRoot">A writable folder that receives the throwaway map folder (one subfolder).</param>
    /// <returns>The settled simulation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static Simulation Build2k(ContentDb content, string scratchRoot) =>
        Build2kDetailed(content, scratchRoot).Simulation;

    /// <summary>Builds the <c>perf-2k</c> world and reports what got built.</summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="scratchRoot">A writable folder that receives the throwaway map folder (one subfolder).</param>
    /// <returns>The settled simulation and the build report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static (Simulation Simulation, DensePlacementReport Report) Build2kDetailed(ContentDb content, string scratchRoot)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(scratchRoot);
        return BuildCustom(content, scratchRoot, Perf2kPlan, Perf2kMapId, flattenHalf: 60);
    }

    /// <summary>Builds the <c>perf-20k</c> world: a synthesised 384 island grown to as many residents as it holds.</summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="scratchRoot">A writable folder that receives the throwaway map folder (one subfolder).</param>
    /// <returns>The settled simulation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static Simulation Build20k(ContentDb content, string scratchRoot) =>
        Build20kDetailed(content, scratchRoot).Simulation;

    /// <summary>Builds the <c>perf-20k</c> world and reports what got built.</summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="scratchRoot">A writable folder that receives the throwaway map folder (one subfolder).</param>
    /// <returns>The settled simulation and the build report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static (Simulation Simulation, DensePlacementReport Report) Build20kDetailed(ContentDb content, string scratchRoot)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(scratchRoot);
        return BuildCustom(content, scratchRoot, Perf20kPlan, Perf20kMapId, flattenHalf: 175);
    }

    /// <summary>
    /// Builds a custom district: the generated island at the plan's seed and size, flattened around the
    /// most open district-sized site, then settled by <see cref="BuildDetailed"/>. The same plan gives
    /// byte-identical saves; the reference saves above are this function with fixed plans.
    /// </summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="scratchRoot">A writable folder that receives the throwaway map folder (one subfolder).</param>
    /// <param name="plan">How much to build.</param>
    /// <param name="mapId">The map ID the synthesised island is loaded under.</param>
    /// <param name="flattenHalf">The plateau half-size in tiles around the start site.</param>
    /// <returns>The settled simulation and the build report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static (Simulation Simulation, DensePlacementReport Report) BuildCustom(
        ContentDb content, string scratchRoot, DensePlacementPlan plan, string mapId, int flattenHalf)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(scratchRoot);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        GeneratedHeightmap heightmap = IslandGenerator.GenerateHeightmap(unchecked((int)plan.Seed), plan.MapSize, content);
        int half = Builder.DistrictHalf(plan.Houses);
        (int hx, int hy, _) = ProbeSite(content, heightmap, half);
        MapDef map = WriteAndLoadIsland(content, scratchRoot, heightmap, plan.MapSize, mapId, [(hx, hy, flattenHalf)]);
        return BuildDetailed(content, map, plan);
    }
    /// <summary>
    /// Builds a dense district on a map: a road grid, then services on a lattice, then houses in the
    /// lattice cells, all through real commands in small batches (one tick per batch, rejections tallied
    /// from the drained events). Sites tick to completion, houses lock at Settlers, cheats fill the beds,
    /// and granted food stocks keep the town stable for the settle run.
    /// </summary>
    /// <param name="content">The loaded game content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="plan">How much to build.</param>
    /// <returns>The settled simulation and the build report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static (Simulation Simulation, DensePlacementReport Report) BuildDetailed(ContentDb content, MapDef map, DensePlacementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(plan);

        var simulation = Simulation.CreateNew(content, new NewGameOptions { Seed = plan.Seed, Map = map });
        simulation.CheatsUsed = true;
        simulation.DrainEvents();

        var builder = new Builder(simulation, content, map);
        builder.GrantTimber(plan.TimberGrant);
        (int hx, int hy, int hw, int hh) = HallRect(content, map);
        int half = Builder.DistrictHalf(plan.Houses);
        builder.LayRoadGridInBox(hx, hy, hw, hh, half);
        builder.LogPhase("roads");
        builder.PlaceLattice(plan.Storehouses, builder.Ids.Storehouse, 2, 2, requireReach: true, spacing: plan.Storehouses > 100 ? 12 : 16, half: half, offsetX: 0, offsetY: 0, center: (hx, hy, hw, hh));
        builder.WaitForSites();
        builder.LogPhase("storehouses");
        builder.PlaceLattice(plan.Markets, builder.Ids.Market, 3, 3, requireReach: true, spacing: 8, half: half, offsetX: 4, offsetY: 0, spread: true, center: (hx, hy, hw, hh));
        builder.WaitForSites();
        builder.LogPhase("markets");
        builder.SnapshotCoverage();
        int housesOk = builder.PlaceHouses(plan.Houses);
        builder.LogSkips("houses");
        builder.LogPhase("houses");        builder.PlaceScattered(plan.Woodcutters, builder.Ids.Woodcutter, 2, 2);
        builder.PlaceScattered(plan.Sawpits, builder.Ids.Sawpit, 2, 2);
        builder.LogSkips("producers");
        builder.WaitForSites();
        builder.LockHouses();
        builder.FillBeds();
        builder.LogPhase("filled");
        builder.Grant(new GoodId[] { builder.FishId, builder.WoodId }, new[] { plan.FishGrant, plan.WoodGrant });
        builder.TickN(plan.SettleTicks);
        builder.LogPhase("settled");

        var report = new DensePlacementReport(
            housesOk, builder.ProducersPlaced, builder.ServicesPlaced,
            builder.Histogram(), builder.CountSites(),
            simulation.CurrentTick, simulation.Query.Population.TotalResidents,
            Hex.Format(simulation.ComputeStateHash()));
        return (simulation, report);
    }

    /// <summary>
    /// Writes a simulation as a version-2 save through the current <see cref="SaveGame"/> writer.
    /// </summary>
    /// <param name="simulation">The settled simulation, between ticks.</param>
    /// <param name="content">The content the simulation runs on.</param>
    /// <param name="mapId">The island ID for the header.</param>
    /// <param name="path">The file to write, created or overwritten.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/> or empty.</exception>
    public static void WriteSave(Simulation simulation, ContentDb content, string mapId, string path)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        SaveGame.Write(stream, simulation, new SaveMeta(content, PerfGameVersion, mapId));
    }

    // One build's worth of batching: enqueue up to BatchSize commands, tick once, and tally the drained
    // events. Commands in a tick apply oldest-first, each seeing the earlier ones, so the scan below
    // claims every cell it hands out and batches stay disjoint by construction.
    private sealed class Builder
    {
        private readonly Simulation _sim;
        private readonly TextWriter _log;
        private readonly SortedDictionary<string, int> _rejections = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _skips = new(StringComparer.Ordinal);
        private readonly HashSet<int> _claimed = [];
        private int _scanCursor;

        public Builder(Simulation simulation, ContentDb content, MapDef map)
        {
            _sim = simulation;
            _log = Console.Error;
            Content = content;
            Map = map;
            Ids = new Ids(content);
            FishId = content.IdOf<GoodId>("fish");
            WoodId = content.IdOf<GoodId>("wood");
            int width = simulation.Query.Map.Width;
            int height = simulation.Query.Map.Height;
            (int hx, int hy, int hw, int hh) = HallRect(content, map);
            int cx = hx + (hw / 2);
            int cy = hy + (hh / 2);
            // Row-major cells over the whole map, nearest the hall first: every scan below walks this one
            // order, so the same plan always hands out the same cells.
            var cells = new List<(int Ring, int X, int Y)>(width * height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    cells.Add((Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)), x, y));
                }
            }

            cells.Sort(static (a, b) =>
            {
                int ring = a.Ring.CompareTo(b.Ring);
                if (ring != 0)
                {
                    return ring;
                }

                int y = a.Y.CompareTo(b.Y);
                return y != 0 ? y : a.X.CompareTo(b.X);
            });
            Cells = cells;
        }

        public ContentDb Content { get; }

        public MapDef Map { get; }

        public Ids Ids { get; }

        public GoodId FishId { get; }

        public GoodId WoodId { get; }

        public List<(int Ring, int X, int Y)> Cells { get; }

        public byte[] CoverageSnapshot { get; private set; } = [];

        // The market-coverage layer once the services are final: sites placed later cannot move it
        // (fields track finished buildings, and the roads are done), so one snapshot gates every house.
        public void SnapshotCoverage() => CoverageSnapshot = _sim.Query.Overlay.MarketCoverage();

        // A 2x2 house counts as covered when any footprint tile would cover a 1x1 house there: the
        // inner corner never touches a road, so requiring all four could never pass by construction.
        public bool IsCoveredCell(int x, int y)
        {
            int width = _sim.Query.Map.Width;
            int height = _sim.Query.Map.Height;
            if (CoverageSnapshot.Length != width * height || x + 1 >= width || y + 1 >= height)
            {
                return false;
            }

            return CoverageSnapshot[y * width + x] != 0
                || CoverageSnapshot[y * width + x + 1] != 0
                || CoverageSnapshot[(y + 1) * width + x] != 0
                || CoverageSnapshot[(y + 1) * width + x + 1] != 0;
        }

        public (int X, int Y) Origin { get; private set; }

        // The pitch-cell test, relative to the district origin the roads were laid from: one past
        // a road line in both axes, so a 2x2 footprint sits between roads without touching them.
        public bool IsPitchCell(int x, int y) =>
            ((x - Origin.X) & 3) == 1 && ((y - Origin.Y) & 3) == 1;

        public int ServicesPlaced { get; private set; }

        public int ProducersPlaced { get; private set; }

        public void LogPhase(string phase)
        {
            var active = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (BuildingInfo info in _sim.Query.Map.ActiveBuildings)
            {
                string name = Content.Buildings.NameOf(info.Type);
                active[name] = active.TryGetValue(name, out int count) ? count + 1 : 1;
            }

            int roads = 0;
            for (int y = 0; y < _sim.Query.Map.Height; y++)
            {
                for (int x = 0; x < _sim.Query.Map.Width; x++)
                {
                    if (_sim.Query.Roads.RoadAt(x, y) is not null)
                    {
                        roads++;
                    }
                }
            }

            int inReach = 0;
            int pitch = 0;
            foreach ((int _, int x, int y) in Cells)
            {
                if (!IsPitchCell(x, y))
                {
                    continue;
                }

                pitch++;
                if (_sim.Query.Roads.IsFootprintInStorehouseReach(x, y, 2, 2))
                {
                    inReach++;
                }
            }

            byte[] layer = _sim.Query.Overlay.StorehouseReach();
            int layerReach = 0;
            foreach (byte cell in layer)
            {
                if (cell != 0)
                {
                    layerReach++;
                }
            }

            byte[] coverage = _sim.Query.Overlay.MarketCoverage();
            int coveredPitch = 0;
            int mapWidth = _sim.Query.Map.Width;
            int mapHeight = _sim.Query.Map.Height;
            foreach ((int _, int x, int y) in Cells)
            {
                if (!IsPitchCell(x, y) || x + 1 >= mapWidth || y + 1 >= mapHeight)
                {
                    continue;
                }

                if (coverage[y * mapWidth + x] != 0
                    || coverage[y * mapWidth + x + 1] != 0
                    || coverage[(y + 1) * mapWidth + x] != 0
                    || coverage[(y + 1) * mapWidth + x + 1] != 0)
                {
                    coveredPitch++;
                }
            }

            _log.WriteLine($"evenmere perf: phase {phase}: roads {roads}, active {{{string.Join(", ", active.Select(kv => $"{kv.Key}={kv.Value}"))}}}, pitch cells {pitch}, in reach {inReach}, reach layer {layerReach}, covered pitch {coveredPitch}");
            int residents = _sim.Query.Population.TotalResidents;
            int full = 0;
            int houses = 0;
            foreach (BuildingInfo info in _sim.Query.Map.ActiveBuildings)
            {
                if (info.Type == Ids.House && _sim.Query.Population.TryGetHouse(info.Id, out HouseInfo house))
                {
                    houses++;
                    if (house.Residents >= house.Capacity)
                    {
                        full++;
                    }
                }
            }

            _log.WriteLine($"evenmere perf: phase {phase}: residents {residents}, houses {houses}, full {full}");
            byte[] marketLayer = _sim.Query.Overlay.MarketCoverage();
            int marketCovered = 0;
            foreach (byte cell in marketLayer)
            {
                if (cell != 0)
                {
                    marketCovered++;
                }
            }

            _log.WriteLine($"evenmere perf: phase {phase}: fulfil fish {_sim.Query.Needs.Fulfilment(FishId)}, wood {_sim.Query.Needs.Fulfilment(WoodId)}, market tiles {marketCovered}");
            if (phase == "settled")
            {
                var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int sampled = 0;
                foreach (BuildingInfo info in _sim.Query.Map.ActiveBuildings)
                {
                    if (sampled >= 200 || houses == 0)
                    {
                        break;
                    }

                    if (info.Type == Ids.House
                        && _sim.Query.Population.TryGetHouse(info.Id, out HouseInfo house)
                        && house.Residents < house.Capacity
                        && _sim.Query.Needs.TryGetHouseNeeds(info.Id, out HouseNeedsInfo needs))
                    {
                        sampled++;
                        foreach (NeedStatus need in needs.Needs)
                        {
                            if (!need.Met)
                            {
                                string key = need.Good.HasValue
                                    ? $"good:{Content.Goods.NameOf(need.Good.Value)}:{need.Reason}"
                                    : $"service:{need.Service}:{need.Reason}";
                                reasons[key] = reasons.TryGetValue(key, out int count) ? count + 1 : 1;
                            }
                        }
                    }
                }

                foreach ((string reason, int count) in reasons)
                {
                    _log.WriteLine($"evenmere perf: unmet among {sampled} non-full houses: {count}x {reason}");
                }
            }
            _log.WriteLine($"evenmere perf: phase {phase}: sites {CountSites()}, timber {Content.Goods.NameOf(Content.IdOf<GoodId>("timber"))}={_sim.Query.Storage.Stock(Content.IdOf<GoodId>("timber"))}");
        }

        public void LogSkips(string phase)
        {
            foreach ((string reason, int count) in _skips)
            {
                _log.WriteLine($"evenmere perf: skips after {phase}: {count}x {reason}");
            }

            _skips.Clear();
        }

        public void GrantTimber(int units) =>
            Flush([new CheatGrantGoods(Content.IdOf<GoodId>("timber"), Amount.FromUnits(units))]);

        public void Grant(GoodId[] goods, int[] units)
        {
            var commands = new List<Command>(goods.Length);
            for (int i = 0; i < goods.Length; i++)
            {
                commands.Add(new CheatGrantGoods(goods[i], Amount.FromUnits(units[i])));
            }

            Flush(commands);
        }

        // A road every fourth row and column across a district box. Water and other
        // bad tiles would reject a whole line (a path places only its valid prefix, and a bad first
        // tile rejects the command), so each line is laid as runs of valid tiles: one command per run.
        // Later lines may cross earlier roads; same-type crossings either join or truncate the run, and
        // the histogram shows which. Either way the mesh stays connected through the rows.
        public void LayRoadGridInBox(int hx, int hy, int hw, int hh, int half)
        {
            int x0 = Math.Max(0, hx - half);
            int y0 = Math.Max(0, hy - half);
            Origin = (x0, y0);
            int x1 = Math.Min(_sim.Query.Map.Width - 1, hx + hw + half);
            int y1 = Math.Min(_sim.Query.Map.Height - 1, hy + hh + half);

            var rows = new List<Command>();
            for (int y = y0; y <= y1; y += 4)
            {
                int x = x0;
                while (x <= x1)
                {
                    while (x <= x1 && !RoadTileValid(x, y))
                    {
                        x++;
                    }

                    int end = x;
                    while (end + 1 <= x1 && RoadTileValid(end + 1, y))
                    {
                        end++;
                    }

                    if (end >= x)
                    {
                        var path = new TileCoord[end - x + 1];
                        for (int i = x; i <= end; i++)
                        {
                            path[i - x] = new TileCoord(i, y);
                        }

                        rows.Add(new PlaceRoad(path, Ids.Road));
                    }

                    x = end + 1;
                }
            }

            Flush(rows);

            var cols = new List<Command>();
            for (int x = x0; x <= x1; x += 4)
            {
                int y = y0;
                while (y <= y1)
                {
                    while (y <= y1 && !RoadTileValid(x, y))
                    {
                        y++;
                    }

                    int end = y;
                    while (end + 1 <= y1 && RoadTileValid(x, end + 1))
                    {
                        end++;
                    }

                    if (end >= y)
                    {
                        var path = new TileCoord[end - y + 1];
                        for (int i = y; i <= end; i++)
                        {
                            path[i - y] = new TileCoord(x, i);
                        }

                        cols.Add(new PlaceRoad(path, Ids.Road));
                    }

                    y = end + 1;
                }
            }

            Flush(cols);
        }

        private bool RoadTileValid(int x, int y) =>
            _sim.Query.Placement.ValidateRoad([new TileCoord(x, y)], Ids.Road).CanPlace;

        // Services on a real lattice over the district box: grid points `spacing` apart, offset one
        // past the district origin so 2x2 and 3x3 footprints never touch a road line (spacings stay
        // multiples of the road pitch, so the offset holds everywhere). Row-major from the corner is
        // deterministic; the hall-near points complete first and the reach wave propagates outward, so
        // later sites simply wait for the mesh (sites advance only in reach). Waves rescan the fixed
        // candidate order and place what the current reach allows; three waves with no placement stop
        // the loop, so a stranded pocket ends the expansion instead of hanging the build. The lattice
        // origin is offset past the district origin (multiples of the road pitch, so footprints never
        // touch roads); services with different offsets interleave instead of colliding. With spread,
        // three strided passes cover the whole candidate list instead of clustering near the hall, so
        // late-placed services (markets, once reach is final) blanket the district.
        public void PlaceLattice(int count, BuildingTypeId type, int w, int h, bool requireReach, int spacing, int half, int offsetX, int offsetY, (int X, int Y, int W, int H) center, bool spread = false)
        {
            (int hx, int hy, int hw, int hh) = center;
            int x0 = Math.Max(0, hx - half);
            int y0 = Math.Max(0, hy - half);
            int x1 = Math.Min(_sim.Query.Map.Width - w, hx + hw + half);
            int y1 = Math.Min(_sim.Query.Map.Height - h, hy + hh + half);

            var candidates = new List<(int Ring, int X, int Y)>();
            int cx = hx + (hw / 2);
            int cy = hy + (hh / 2);
            for (int y = y0 + 1 + offsetY; y <= y1; y += spacing)
            {
                for (int x = x0 + 1 + offsetX; x <= x1; x += spacing)
                {
                    candidates.Add((Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)), x, y));
                }
            }

            candidates.Sort(static (a, b) =>
            {
                int ring = a.Ring.CompareTo(b.Ring);
                if (ring != 0)
                {
                    return ring;
                }

                int y = a.Y.CompareTo(b.Y);
                return y != 0 ? y : a.X.CompareTo(b.X);
            });

            int placed = 0;
            int stagnant = 0;
            int wave = 0;
            while (placed < count && stagnant < 3)
            {
                int before = placed;
                var batch = new List<Command>(BatchSize);
                // One wave: three strided passes when spreading (offsets 0, 1, 2 cover every candidate),
                // otherwise a single ring-ordered pass for reach chaining.
                for (int stride = 0; stride < (spread ? 3 : 1) && placed < count; stride++)
                {
                    int index = 0;
                    foreach ((int _, int x, int y) in candidates)
                    {
                        if (placed >= count || batch.Count == BatchSize)
                        {
                            break;
                        }

                        if (spread && index++ % 3 != stride)
                        {
                            continue;
                        }

                        if (TryClaim(type, x, y, w, h, requireReach))
                        {
                            batch.Add(new PlaceBuilding(type, x, y, Rotation.Deg0));
                            placed++;
                        }
                    }

                    Flush(batch);
                    batch = new List<Command>(BatchSize);
                }

                wave++;
                _log.WriteLine($"evenmere perf: wave {wave} {Content.Buildings.NameOf(type)}: placed {placed - before} (total {placed})");
                LogSkips($"{Content.Buildings.NameOf(type)} wave {wave}");
                if (placed == before)
                {
                    stagnant++;
                }
                else
                {
                    stagnant = 0;
                    WaitForSites();
                }
            }

            ServicesPlaced += placed;
        }

        // Houses fill the pitch-4 grid cells between the roads: top-lefts with both coordinates one past
        // a road line, each beside a road and (once the mesh is live) in some storehouse's reach.
        public int PlaceHouses(int count)
        {
            int placed = 0;
            var batch = new List<Command>(BatchSize);
            while (placed < count)
            {
                int before = placed;
                while (_scanCursor < Cells.Count && placed < count && batch.Count < BatchSize)
                {
                    (int _, int x, int y) = Cells[_scanCursor++];
                    if (!IsPitchCell(x, y) || !IsCoveredCell(x, y))
                    {
                        continue;
                    }

                    if (TryClaim(Ids.House, x, y, 2, 2, requireReach: true))
                    {
                        batch.Add(new PlaceBuilding(Ids.House, x, y, Rotation.Deg0));
                        placed++;
                    }
                }

                Flush(batch);
                batch = new List<Command>(BatchSize);
                if (placed == before)
                {
                    break;
                }
            }

            Flush(batch);
            LogSkips("house");
            return placed;
        }

        // Producers take any unclaimed validating cell in reach, scanning from the hall again: the
        // houses already hold the pitch cells, so producers fill the gaps between them.
        public void PlaceScattered(int count, BuildingTypeId type, int w, int h)
        {
            int placed = 0;
            int cursor = 0;
            var batch = new List<Command>(BatchSize);
            while (placed < count)
            {
                int before = placed;
                while (cursor < Cells.Count && placed < count && batch.Count < BatchSize)
                {
                    (int _, int x, int y) = Cells[cursor++];
                    if (TryClaim(type, x, y, w, h, requireReach: true))
                    {
                        batch.Add(new PlaceBuilding(type, x, y, Rotation.Deg0));
                        placed++;
                    }
                }

                Flush(batch);
                batch = new List<Command>(BatchSize);
                if (placed == before)
                {
                    break;
                }
            }

            Flush(batch);
            ProducersPlaced += placed;
            LogSkips(Content.Buildings.NameOf(type));
        }

        // Every house at Settlers: Villagers would cap at four without bread, and the perf town has none.
        public void LockHouses()
        {
            var batch = new List<Command>(BatchSize);
            foreach (BuildingInfo info in _sim.Query.Map.ActiveBuildings)
            {
                if (info.Type == Ids.House
                    && _sim.Query.Population.TryGetHouse(info.Id, out HouseInfo house)
                    && !house.UpgradeLocked)
                {
                    batch.Add(new SetHouseUpgradeLock(info.Id, true));
                    if (batch.Count == BatchSize)
                    {
                        Flush(batch);
                        batch = new List<Command>(BatchSize);
                    }
                }
            }

            Flush(batch);
        }

        // Fill every free bed exactly: the cheat is all-or-nothing, so the count must fit.
        public void FillBeds()
        {
            int freeBeds = 0;
            foreach (BuildingInfo info in _sim.Query.Map.ActiveBuildings)
            {
                if (info.Type == Ids.House
                    && _sim.Query.Population.TryGetHouse(info.Id, out HouseInfo house))
                {
                    freeBeds += house.Capacity - house.Residents;
                }
            }

            if (freeBeds > 0)
            {
                Flush([new CheatAddResidents(freeBeds)]);
            }
        }

        public void WaitForSites()
        {
            for (int day = 0; day < 60 && CountSites() > 0; day++)
            {
                TickN(120);
            }
        }

        public void TickN(long ticks)
        {
            for (long i = 0; i < ticks; i++)
            {
                _sim.Tick();
                if ((i + 1) % 600 == 0)
                {
                    int declined = 0;
                    foreach (SimEvent @event in _sim.DrainEvents())
                    {
                        if (@event.GetType().Name.Contains("Declined", StringComparison.Ordinal))
                        {
                            declined++;
                        }
                    }

                    _log.WriteLine($"evenmere perf: settle tick {_sim.CurrentTick}: residents {_sim.Query.Population.TotalResidents}, declined events {declined}");
                }
            }

            // Settle ticks raise no commands, so nothing can reject; still, drain so the next batch's
            // histogram starts clean.
            _sim.DrainEvents();
        }

        public int CountSites()
        {
            int sites = 0;
            foreach (BuildingInfo info in _sim.Query.Map.Buildings)
            {
                if (info.State == BuildingState.Site)
                {
                    sites++;
                }
            }

            return sites;
        }

        public ImmutableArray<(string Reason, int Count)> Histogram()
        {
            var histogram = new List<(string Reason, int Count)>(_rejections.Count);
            foreach ((string reason, int count) in _rejections)
            {
                histogram.Add((reason, count));
            }

            return [.. histogram];
        }

        // A footprint the sim would accept, on tiles this build has not handed out yet. The set only
        // grows, validation reads static map data plus the finished buildings, and a claimed cell never
        // frees: a cell skipped this scan stays skipped, so scans terminate. Skips are tallied by
        // reason so the build log shows why a scan starved.
        private bool TryClaim(BuildingTypeId type, int x, int y, int w, int h, bool requireReach)
        {
            int width = _sim.Query.Map.Width;
            int height = _sim.Query.Map.Height;
            if (x < 0 || y < 0 || x + w > width || y + h > height)
            {
                Skip("bounds");
                return false;
            }

            // Occupancy first, without claiming: a cell that fails a later check must stay free for
            // the wave that brings reach to it. Only a cell that passes everything is claimed.
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    if (_sim.Query.Map.BuildingAt(x + dx, y + dy) != EntityId.None
                        || _sim.Query.Roads.RoadAt(x + dx, y + dy) is not null
                        || _claimed.Contains(((y + dy) * width) + x + dx))
                    {
                        Skip("taken");
                        return false;
                    }
                }
            }

            if (requireReach && !_sim.Query.Roads.IsFootprintInStorehouseReach(x, y, w, h))
            {
                Skip("no-reach");
                return false;
            }

            PlacementResult validation = _sim.Query.Placement.Validate(type, x, y, Rotation.Deg0);
            if (!validation.CanPlace)
            {
                Skip(validation.Errors.IsDefaultOrEmpty
                    ? "invalid" : $"invalid:{validation.Errors[0].Issue}");
                return false;
            }

            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    _claimed.Add(((y + dy) * width) + x + dx);
                }
            }

            return true;
        }

        private void Skip(string reason) =>
            _skips[reason] = _skips.TryGetValue(reason, out int count) ? count + 1 : 1;

        private void Flush(List<Command> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            foreach (Command command in batch)
            {
                _sim.Enqueue(command);
            }

            batch.Clear();
            _sim.Tick();
            foreach (SimEvent @event in _sim.DrainEvents())
            {
                if (@event is CommandRejected rejected)
                {
                    string key = $"{rejected.Code}: {rejected.Detail}";
                    _rejections[key] = _rejections.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            }
        }

        internal static int DistrictHalf(int houses)
        {
            // Each house wants a 4x4 pitch cell; roads, services and gaps add about half again.
            int side = (int)Math.Ceiling(Math.Sqrt(houses * 16 * 1.6));
            return (side / 2) + 8;
        }
    }

    private sealed class Ids
    {
        public Ids(ContentDb content)
        {
            House = content.IdOf<BuildingTypeId>("settler_house");
            Storehouse = content.IdOf<BuildingTypeId>("storehouse");
            Market = content.IdOf<BuildingTypeId>("market");
            Woodcutter = content.IdOf<BuildingTypeId>("woodcutters_hut");
            Sawpit = content.IdOf<BuildingTypeId>("sawpit");
            Road = content.Required.DirtRoad;
        }

        public BuildingTypeId House { get; }

        public BuildingTypeId Storehouse { get; }

        public BuildingTypeId Market { get; }

        public BuildingTypeId Woodcutter { get; }

        public BuildingTypeId Sawpit { get; }

        public BuildingTypeId Road { get; }
    }

    private static (int X, int Y, int Width, int Height) HallRect(ContentDb content, MapDef map)
    {
        Footprint footprint = content.Get(content.Required.FoundersHall).Footprint;
        bool swaps = (map.StartSite.Rotation & 1) == 1;
        return (map.StartSite.X, map.StartSite.Y, swaps ? footprint.Height : footprint.Width, swaps ? footprint.Width : footprint.Height);
    }

    // Synthesises the generated island the perf saves are built on: terrain and elevation from the integer
    // generator, written as map files to a throwaway folder and loaded like any authored map. Regions,
    // deposits and fertility stay zeroed (M2-02's layers); the island needs none of them for a settler district.
    // Each site flattens its own plateau to open plains; the island itself is still the seeded generator
    // output everywhere else, and the start site is the first entry.
    private static MapDef WriteAndLoadIsland(ContentDb content, string scratchRoot, GeneratedHeightmap heightmap, int size, string mapId, List<(int X, int Y, int Flatten)> sites)
    {
        // Flatten each district plateau to open plains: every land tile becomes buildable with no
        // access locks, so the road grids connect. Water stays water. This is the synthetic part of
        // the provisional saves (a real start guarantee, M2-02b, would do this properly); the island
        // itself is still the seeded generator output everywhere else.
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId deepWater = content.IdOf<TerrainId>("deep_water");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        foreach ((int hx, int hy, int flattenHalfSize) in sites)
        {
            for (int y = Math.Max(0, hy - flattenHalfSize); y <= Math.Min(size - 1, hy + flattenHalfSize); y++)
            {
                for (int x = Math.Max(0, hx - flattenHalfSize); x <= Math.Min(size - 1, hx + flattenHalfSize); x++)
                {
                    TerrainId tile = heightmap.Terrain[(y * size) + x];
                    if (tile == sea || tile == deepWater || tile == reef)
                    {
                        continue;
                    }

                    // Peaks stay hills (relief without access locks); rivers join the plateau as plains so
                    // no waterway cuts the road mesh in two. Everything else becomes plains.
                    TerrainId flat = tile == content.IdOf<TerrainId>("peaks") ? hills : plains;
                    heightmap.Terrain[(y * size) + x] = flat;
                    heightmap.Elevation[(y * size) + x] = 5;
                }
            }
        }

        string folder = Path.Combine(scratchRoot, "maps", mapId);
        Directory.CreateDirectory(folder);

        var legend = new Dictionary<string, char>(StringComparer.Ordinal);
        foreach ((char symbol, string terrainName) in MapTerrainRules.DefaultLegend)
        {
            legend[terrainName] = symbol;
        }

        var terrain = new StringBuilder(size * (size + 1));
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                string name = content.Terrains.NameOf(heightmap.Terrain[(y * size) + x]);
                if (!legend.TryGetValue(name, out char symbol))
                {
                    throw new InvalidOperationException($"The generated island paints '{name}', which has no map legend character.");
                }

                terrain.Append(symbol);
            }

            terrain.Append('\n');
        }

        File.WriteAllText(Path.Combine(folder, "terrain.txt"), terrain.ToString());

        var elevation = new StringBuilder(size * (size + 1));
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                elevation.Append("0123456789ABCDEF"[heightmap.Elevation[(y * size) + x]]);
            }

            elevation.Append('\n');
        }

        File.WriteAllText(Path.Combine(folder, "elevation.txt"), elevation.ToString());

        File.WriteAllText(
            Path.Combine(folder, "map.json"),
            "{\n"
            + $"  \"width\": {size},\n"
            + $"  \"height\": {size},\n"
            + $"  \"startSite\": {{ \"x\": {sites[0].X}, \"y\": {sites[0].Y}, \"rotation\": 0 }},\n"
            + "  \"startStock\": { \"timber\": 40, \"fish\": 20, \"wood\": 10 }\n"
            + "}\n");

        MapLoadResult loaded = MapLoader.Load([scratchRoot], mapId, content);
        if (loaded.Map is null)
        {
            var details = new StringBuilder();
            foreach (ContentDiagnostic diagnostic in loaded.Diagnostics)
            {
                details.AppendLine(diagnostic.ToString());
            }

            throw new InvalidOperationException($"The synthesised {size} island does not load: {details}");
        }

        return loaded.Map;
    }

    /// <summary>
    /// The open-land tiles in the best district-sized window for a seed: a temporary probe for
    /// sizing the 20k island. Returns the hall position and the open count.
    /// </summary>
    public static (int X, int Y, int Open) ProbeSite(ContentDb content, GeneratedHeightmap heightmap, int windowRadius)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(heightmap);
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId forest = content.IdOf<TerrainId>("forest");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId river = content.IdOf<TerrainId>("river");
        int size = heightmap.Size;
        int centre = size / 2;
        int[] integral = new int[(size + 1) * (size + 1)];
        for (int y = 0; y < size; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < size; x++)
            {
                TerrainId tile = heightmap.Terrain[(y * size) + x];
                if (tile == plains || tile == forest || tile == hills || tile == river)
                {
                    rowSum++;
                }

                integral[((y + 1) * (size + 1)) + x + 1] = integral[(y * (size + 1)) + x + 1] + rowSum;
            }
        }

        int WindowSum(int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(size, x1);
            y1 = Math.Min(size, y1);
            return integral[(y1 * (size + 1)) + x1] - integral[(y0 * (size + 1)) + x1]
                - integral[(y1 * (size + 1)) + x0] + integral[(y0 * (size + 1)) + x0];
        }

        int bestOpen = -1;
        int bestX = 0;
        int bestY = 0;
        long bestDistance = long.MaxValue;
        for (int y = 0; y <= size - 4; y++)
        {
            for (int x = 0; x <= size - 4; x++)
            {
                bool open = true;
                for (int dy = 0; dy < 4 && open; dy++)
                {
                    for (int dx = 0; dx < 4; dx++)
                    {
                        TerrainId tile = heightmap.Terrain[((y + dy) * size) + x + dx];
                        if (tile != plains && tile != forest && tile != hills && tile != river)
                        {
                            open = false;
                            break;
                        }
                    }
                }

                if (!open)
                {
                    continue;
                }

                int openCount = WindowSum(x + 2 - windowRadius, y + 2 - windowRadius, x + 2 + windowRadius, y + 2 + windowRadius);
                long distance = ((long)(x + 2 - centre) * (x + 2 - centre)) + ((long)(y + 2 - centre) * (y + 2 - centre));
                if (openCount > bestOpen || (openCount == bestOpen && distance < bestDistance))
                {
                    bestOpen = openCount;
                    bestDistance = distance;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return (bestX, bestY, bestOpen);
    }
}
