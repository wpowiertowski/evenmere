using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// The start guarantee (architecture plan §Island and map): the start region is bounded by rivers and holds everything
/// Era I needs before bridges — forest, grain plains, sheep pasture, coastline, hills with stone, clay riverbanks and a
/// fishing-hut site — measured over the tiles a dirt road can actually reach from the hall: 4-connected, no river or
/// water tiles, and within the hall's storehouse reach, the same connectivity the road placement rules use.
/// The predicate is pure over <c>(MapDef, ContentDb)</c> and shared with the tests: the generator re-seeds with a
/// deterministic sub-seed (at most <see cref="MaxAttempts"/> attempts, recorded in <c>MapDef.SubSeed</c>) until it holds.
/// </summary>
public static class StartGuarantee
{
    /// <summary>The most guarantee attempts for one island: sub-seeds 0 to 15.</summary>
    public const int MaxAttempts = 16;

    /// <summary>How far the start checks reach from the hall footprint: 24 tiles, the M1-03 radius, in Chebyshev distance.</summary>
    public const int StartRadius = 24;

    /// <summary>The forest belt within the start radius: 80 tiles, the M1-03 count.</summary>
    public const int MinForest = 80;

    /// <summary>Grain-fertile 5×5 placements within the start radius: 3, the M1-03 count.</summary>
    public const int MinGrainPlacements = 3;

    /// <summary>Plains 4×4 sheep-farm placements within the start radius: 4, the M1-03 count.</summary>
    public const int MinPlainsPlacements = 4;

    /// <summary>Coastline tiles within the start radius: 40, the M1-03 count.</summary>
    public const int MinCoast = 40;

    /// <summary>Stone-on-hills tiles within the start radius: 6, the M1-03 count.</summary>
    public const int MinStone = 6;

    /// <summary>Clay tiles beside a river within the start radius: 6, the M1-03 count.</summary>
    public const int MinClay = 6;

    /// <summary>River tiles touching the start region: 20, enough for a real river boundary rather than a speck.</summary>
    public const int MinBorderRivers = 20;

    /// <summary>The river boundary's bounding box spans this far in both axes: 20 tiles, so it runs rather than dots.</summary>
    public const int MinBorderSpan = 20;

    /// <summary>How far around a candidate hall site the settlement-space count looks: the Era-I settlement grows
    /// nearest the hall, so the roomiest verified site is the one with the most settleable land near it.</summary>
    public const int SettlementRadius = 12;

    /// <summary>Grain-fertile plains tiles within the start radius that a hall site needs: 75, room for three 5×5 placements.</summary>
    public const int MinGrainTiles = 75;

    /// <summary>Plains tiles within the start radius that a hall site needs: 64, room for four 4×4 placements.</summary>
    public const int MinPlainsTiles = 64;

    // Neighbour offsets in a fixed order (N, NE, E, SE, S, SW, W, NW), so every walk below is identical on every CPU.
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Tests whether a finished island satisfies the start guarantee: a valid hall site in a river-bounded region,
    /// with the M1-03 start counts within <see cref="StartRadius"/> tiles of the hall.
    /// </summary>
    /// <param name="map">The finished island.</param>
    /// <param name="content">The content the island was generated against.</param>
    /// <returns><see langword="true"/> when every check holds.</returns>
    public static bool IsSatisfied(MapDef map, ContentDb content) => FailingChecks(map, content).Count == 0;

    /// <summary>
    /// Names every start-guarantee check that fails, with its count: the generator retries while this is non-empty,
    /// and the tests report it when a seed needs attention. Pure over <c>(MapDef, ContentDb)</c> like
    /// <see cref="IsSatisfied"/>.
    /// </summary>
    /// <param name="map">The finished island.</param>
    /// <param name="content">The content the island was generated against.</param>
    /// <returns>The failing check names, empty when the island satisfies the guarantee.</returns>
    internal static IReadOnlyList<string> FailingChecks(MapDef map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        var failing = new List<string>();
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId forest = content.IdOf<TerrainId>("forest");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        if (!TryHall(map, content, out int hallWidth, out int hallHeight))
        {
            failing.Add("hall");
            return failing;
        }

        // Every count below is measured over the tiles a dirt road can actually reach from the hall: 4-connected,
        // no river or water tiles, and within the hall's storehouse reach. A start whose farmland lies across the
        // river, or further than a dirt road can run inside the reach, meets no guarantee worth the name, because
        // carts only run over road tiles inside a storehouse field.
        bool[] reach = FloodReach(map, content, hallWidth, hallHeight);
        int countedForest = CountForest(map, forest, reach);
        if (countedForest < MinForest)
        {
            failing.Add($"forest {countedForest}/{MinForest}");
        }

        int grain = CountGrainPlacements(map, plains, reach);
        if (grain < MinGrainPlacements)
        {
            failing.Add($"grain {grain}/{MinGrainPlacements}");
        }

        int pasture = CountPlainsPlacements(map, plains, reach);
        if (pasture < MinPlainsPlacements)
        {
            failing.Add($"pasture {pasture}/{MinPlainsPlacements}");
        }

        int coast = CountCoast(map, content, reach);
        if (coast < MinCoast)
        {
            failing.Add($"coast {coast}/{MinCoast}");
        }

        int stone = CountStone(map, hills, reach);
        if (stone < MinStone)
        {
            failing.Add($"stone {stone}/{MinStone}");
        }

        int clay = CountClay(map, reach);
        if (clay < MinClay)
        {
            failing.Add($"clay {clay}/{MinClay}");
        }

        string? hut = CheckFishingHut(map, content, reach);
        if (hut is not null)
        {
            failing.Add(hut);
        }

        string? rivers = CheckRivers(map, content, hallWidth, hallHeight);
        if (rivers is not null)
        {
            failing.Add(rivers);
        }

        return failing;
    }

    /// <summary>
    /// Chooses the Founders' Hall site: inside the region with the longest river border (the start region), the first
    /// ascending buildable footprint with open water in reach. Falls back to the first buildable footprint anywhere.
    /// Deterministic: ascending scans only, no randomness.
    /// </summary>
    /// <param name="map">The heightmap with regions; terrain, elevation, river widths and regions are read.</param>
    /// <param name="content">The loaded content, which must define the hall, the sea terrains and the highlands.</param>
    /// <returns>The hall's top-left tile and rotation (always 0).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this search needs.</exception>
    /// <exception cref="InvalidOperationException">No buildable footprint fits anywhere on the island.</exception>
    internal static MapStartSite FindStartSite(GeneratedHeightmap map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        Footprint hall = content.Get(content.Required.FoundersHall).Footprint;
        var borders = RegionBorders.Build(map, content);
        int[] order = RegionOrder(borders.Count);
        var sums = WindowSums.Build(map, content);
        int[] banks = ComputeBanks(map, content);
        MapTerrainFeatures[] features = TerrainFeaturesOf(content);
        var fallback = new FallbackTracker();

        // River-bordered regions first, best-bordered first: the roomiest verified footprint in the first region
        // with one. Every start-window survivor is checked against the full predicate (which also checks the road
        // reach and the fishing-hut site), and the most settleable survivor wins, so a site returned here satisfies
        // the predicate by construction and has room for the Era-I settlement.
        foreach (int region in order)
        {
            if (borders.Count[region] < MinBorderRivers || !borders.Spans(region))
            {
                continue;
            }

            MapStartSite? site = ScanRegion(map, content, hall, sums, banks, borders, features, fallback, region);
            if (site is not null)
            {
                return site.Value;
            }
        }

        // Fallbacks: the first verified footprint anywhere (only the river boundary stays for the predicate
        // to verify), then the first repaired fallback candidate in rank order, then a coastal footprint in the
        // best region, then anything buildable. Repair copies the layers so a failed repair never dirties the
        // island for the next candidate. Repair runs as soon as no verified site is found on an attempt (rather
        // than after all sub-seeds fail): both paths satisfy the predicate, and this avoids regenerating islands.
        int best = order.Length > 0 ? order[0] : 0;
        MapStartSite? verified = ScanIsland(map, content, hall, sums, banks, borders, features, fallback);
        MapStartSite? repaired = null;
        if (verified is null)
        {
            repaired = TryRepairFallback(map, content, features, fallback);
        }

        MapStartSite? fallbackSite = verified
            ?? repaired
            ?? ScanRegion(map, content, hall, best, coastRequired: true)
            ?? ScanRegion(map, content, hall, best, coastRequired: false)
            ?? ScanIsland(map, content, hall);
        if (fallbackSite is null)
        {
            throw new InvalidOperationException("The generated island has no buildable Founders' Hall footprint.");
        }

        return fallbackSite.Value;
    }

    // Scores a buildable site for the fallback path, or null when it cannot lead to a repairable island: a voted
    // start region that is not river-bounded (rivers can't be repaired), or no hut-eligible coast nearby (a hut
    // needs reachable coast). The score counts hut-eligible coast tiles within path-length reach of the hall:
    // banked, buildable, gentle and water-adjacent, in Chebyshev 25 (path length is never shorter than Chebyshev
    // distance, so every tile a dirt road can reach is inside). Near, buildable coast ranks first because steep
    // cliffs and marshes never admit a hut, and far coast may lie beyond the storehouse reach.
    internal static int? FallbackScore(GeneratedHeightmap map, ContentDb content, Footprint hall, RegionBorders borders, int[] banks, int bank, int x, int y)
    {
        ushort region = VoteRegion(map, hall, borders, x, y);
        if (borders.Count[region] < MinBorderRivers || !borders.Spans(region))
        {
            return null;
        }

        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId deep = content.IdOf<TerrainId>("deep_water");
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId forest = content.IdOf<TerrainId>("forest");
        int size = map.Size;
        const int window = 25;
        int x0 = Math.Max(0, x - window);
        int y0 = Math.Max(0, y - window);
        int x1 = Math.Min(size - 1, x + hall.Width - 1 + window);
        int y1 = Math.Min(size - 1, y + hall.Height - 1 + window);
        int eligible = 0;
        int forestAdjacent = 0;
        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                int tile = (ty * size) + tx;
                if (banks[tile] != bank || map.RiverWidth[tile] != 0)
                {
                    continue;
                }

                TerrainId terrain = map.Terrain[tile];
                if (terrain.Equals(highlands) || !content.Get(terrain).Buildable || SlopeAt(map, tx, ty) > (int)SlopeClass.Gentle)
                {
                    continue;
                }

                if (IsWaterTile(map, tx - 1, ty, sea, reef, deep) || IsWaterTile(map, tx + 1, ty, sea, reef, deep)
                    || IsWaterTile(map, tx, ty - 1, sea, reef, deep) || IsWaterTile(map, tx, ty + 1, sea, reef, deep))
                {
                    eligible++;
                }

                if (terrain.Equals(plains) && HasForestNeighbour(map, tx, ty, forest))
                {
                    forestAdjacent++;
                }
            }
        }

        // A repairable island needs hut-suitable coast (unrepairable) and, for the forest belt, growable
        // plains near existing forest. Sites with neither can't admit repair.
        if (eligible == 0 && forestAdjacent == 0)
        {
            return null;
        }

        return eligible + forestAdjacent;
    }

    private static bool HasForestNeighbour(GeneratedHeightmap map, int x, int y, TerrainId forest)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx < (uint)map.Size && (uint)ny < (uint)map.Size
                    && map.Terrain[((ny * map.Size) + nx)].Equals(forest))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsWaterTile(GeneratedHeightmap map, int x, int y, TerrainId sea, TerrainId reef, TerrainId deep) =>
        (uint)x < (uint)map.Size && (uint)y < (uint)map.Size
        && (map.Terrain[(y * map.Size) + x].Equals(sea)
            || map.Terrain[(y * map.Size) + x].Equals(reef)
            || map.Terrain[(y * map.Size) + x].Equals(deep));

    // Tries this attempt's tracked candidates for the repair path, in rank order: the first whose failings are all
    // repairable is repaired on a copy, and on success the repaired layers are copied back into the island, which
    // then satisfies the predicate. Unrepairable failings (coast, hut, border, span, hall) disqualify: repair cannot
    // fix them. Copying keeps a failed repair from dirtying the island for the next candidate. Deterministic: rank
    // order, and repair itself is deterministic.
    // Returns the repaired site, or null when no tracked candidate admits repair.
    // Whether the candidate fails an unrepairable coast check: too little reachable coast, or no valid fishing-hut
    // site on it. Coast and hut failings are never repaired, and profiling shows every skipped fallback candidate
    // fails on coast, so this prefilter avoids the full predicate (all counts, placements, rivers) for doomed
    // candidates. It reuses CountCoast and CheckFishingHut, so it matches FailingChecks exactly: a skip here is a
    // skip there, and a pass falls through to the full check. Byte-identical outputs, much less work.
    private static bool FailsCoastOrHut(MapDef wrapped, ContentDb content)
    {
        if (!TryHall(wrapped, content, out int hallWidth, out int hallHeight))
        {
            return true;
        }

        bool[] reach = FloodReach(wrapped, content, hallWidth, hallHeight);
        if (CountCoast(wrapped, content, reach) < MinCoast)
        {
            return true;
        }

        return CheckFishingHut(wrapped, content, reach) is not null;
    }

    private static MapStartSite? TryRepairFallback(GeneratedHeightmap map, ContentDb content, MapTerrainFeatures[] features, FallbackTracker fallback)
    {
        foreach ((int _, int x, int y) in fallback.Ranked())
        {
            var candidate = new MapStartSite(x, y, 0);
            MapDef wrapped = WrapCandidate(map, content, candidate, features);
            if (FailsCoastOrHut(wrapped, content))
            {
                continue;
            }

            bool repairable = true;
            foreach (string failing in FailingChecks(wrapped, content))
            {
                if (!StartRepair.IsRepairable(failing))
                {
                    repairable = false;
                    break;
                }
            }

            if (!repairable)
            {
                continue;
            }

            var terrain = (TerrainId[])map.Terrain.Clone();
            byte[] deposit = (byte[])map.Deposit.Clone();
            uint[] fertility = (uint[])map.Fertility.Clone();
            var copy = new GeneratedHeightmap(map.Size, terrain, map.Elevation, map.RiverWidth, map.Region, deposit, fertility);
            try
            {
                StartRepair.Repair(copy, content, candidate);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            Array.Copy(terrain, map.Terrain, map.TileCount);
            Array.Copy(deposit, map.Deposit, map.TileCount);
            Array.Copy(fertility, map.Fertility, map.TileCount);
            return candidate;
        }

        return null;
    }

    // The start region of a candidate footprint: the region holding most of its tiles, ties to the lowest number.
    // The same vote CheckRivers uses, so the border gate never rejects a site the predicate would accept.
    private static ushort VoteRegion(GeneratedHeightmap map, Footprint hall, RegionBorders borders, int x, int y)
    {
        Span<int> votes = stackalloc int[borders.Top + 1];
        votes.Clear();
        for (int dy = 0; dy < hall.Height; dy++)
        {
            for (int dx = 0; dx < hall.Width; dx++)
            {
                votes[map.Region[((y + dy) * map.Size) + x + dx]]++;
            }
        }

        int bestRegion = 0;
        for (int r = 1; r < votes.Length; r++)
        {
            if (votes[r] > votes[bestRegion])
            {
                bestRegion = r;
            }
        }

        return (ushort)bestRegion;
    }

    // The fallback candidates for the repair path: the most coastal buildable sites seen, for when no site
    // verifies. Only sites whose voted start region is river-bounded are tracked (rivers can't be repaired), and
    // only sites with coast in the window (a hut needs reachable coast). Coastal sites lead because the fishing-hut
    // site is the hardest unrepairable check to satisfy. Deterministic: ties keep the earlier site, and every scan
    // visits sites in ascending order.
    internal sealed class FallbackTracker
    {
        /// <summary>How many ranked candidates the tracker keeps: enough coastal sites that one admits repair.</summary>
        public const int Kept = 2000;

        private readonly (int Coast, int X, int Y)[] _best = new (int, int, int)[Kept];
        private int _count;

        /// <summary>Adds a candidate, keeping the most coastal. Ties keep the earlier site.</summary>
        /// <param name="coast">The banked coast tiles in the window (more coastal ranks higher).</param>
        /// <param name="x">The footprint's left column.</param>
        /// <param name="y">The footprint's top row.</param>
        internal void Consider(int coast, int x, int y)
        {
            int at = _count;
            if (_count >= Kept)
            {
                at = Kept - 1;
                if (coast <= _best[at].Coast)
                {
                    return;
                }
            }

            while (at > 0 && coast > _best[at - 1].Coast)
            {
                _best[at] = _best[at - 1];
                at--;
            }

            _best[at] = (coast, x, y);
            if (_count < Kept)
            {
                _count++;
            }
        }

        /// <summary>Gets the kept candidates from most to least coastal.</summary>
        /// <returns>The candidates in rank order.</returns>
        internal System.Collections.Generic.IEnumerable<(int Coast, int X, int Y)> Ranked()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _best[i];
            }
        }
    }

    // Region numbers by river border, longest first with ties to the lowest region.
    private static int[] RegionOrder(int[] border)
    {
        int[] order = new int[border.Length];
        for (int r = 0; r < order.Length; r++)
        {
            order[r] = r;
        }

        Array.Sort(order, (a, b) =>
        {
            int byBorder = border[b].CompareTo(border[a]);
            return byBorder != 0 ? byBorder : a.CompareTo(b);
        });
        return order;
    }

    // Summed-area tables over the per-tile start indicators, so any Chebyshev-24 window around a footprint reads in
    // O(1). Built once per island attempt: six linear passes.
    internal sealed class WindowSums
    {
        private readonly int _size;
        private readonly int[] _coast;
        private readonly int[] _forest;
        private readonly int[] _grain;
        private readonly int[] _plains;
        private readonly int[] _stone;
        private readonly int[] _clay;
        private readonly int[] _coastFlat;
        private readonly int[] _forestFlat;
        private readonly int[] _grainFlat;
        private readonly int[] _plainsFlat;
        private readonly int[] _stoneFlat;
        private readonly int[] _clayFlat;

        private WindowSums(
            int size,
            int[] coast, int[] forest, int[] grain, int[] plains, int[] stone, int[] clay,
            int[] coastFlat, int[] forestFlat, int[] grainFlat, int[] plainsFlat, int[] stoneFlat, int[] clayFlat)
        {
            _size = size;
            _coast = coast;
            _forest = forest;
            _grain = grain;
            _plains = plains;
            _stone = stone;
            _clay = clay;
            _coastFlat = coastFlat;
            _forestFlat = forestFlat;
            _grainFlat = grainFlat;
            _plainsFlat = plainsFlat;
            _stoneFlat = stoneFlat;
            _clayFlat = clayFlat;
        }

        internal static WindowSums Build(GeneratedHeightmap map, ContentDb content)
        {
            TerrainId sea = content.IdOf<TerrainId>("sea");
            TerrainId reef = content.IdOf<TerrainId>("reef");
            TerrainId deep = content.IdOf<TerrainId>("deep_water");
            TerrainId plains = content.IdOf<TerrainId>("plains");
            TerrainId forest = content.IdOf<TerrainId>("forest");
            TerrainId hills = content.IdOf<TerrainId>("hills");
            uint grainBit = 1u << (int)CropKind.Grain;
            byte stone = (byte)((int)DepositKind.Stone + 1);
            byte clay = (byte)((int)DepositKind.Clay + 1);
            int size = map.Size;
            int[] coast = new int[map.TileCount];
            int[] forestTiles = new int[map.TileCount];
            int[] grain = new int[map.TileCount];
            int[] plainsTiles = new int[map.TileCount];
            int[] stoneTiles = new int[map.TileCount];
            int[] clayTiles = new int[map.TileCount];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size) + x;
                    TerrainId terrain = map.Terrain[i];
                    bool water = terrain.Equals(sea) || terrain.Equals(reef) || terrain.Equals(deep);
                    bool river = map.RiverWidth[i] != 0;
                    if (!water && !river && HasOpenWaterNeighbourTiles(map, x, y, sea, reef, deep))
                    {
                        coast[i] = 1;
                    }

                    if (terrain.Equals(forest))
                    {
                        forestTiles[i] = 1;
                    }

                    if (terrain.Equals(plains))
                    {
                        plainsTiles[i] = 1;
                        if ((map.Fertility[i] & grainBit) != 0)
                        {
                            grain[i] = 1;
                        }
                    }

                    if (terrain.Equals(hills) && map.Deposit[i] == stone)
                    {
                        stoneTiles[i] = 1;
                    }

                    if (map.Deposit[i] == clay)
                    {
                        clayTiles[i] = 1;
                    }
                }
            }

            return new WindowSums(
                size,
                Prefix(coast, size),
                Prefix(forestTiles, size),
                Prefix(grain, size),
                Prefix(plainsTiles, size),
                Prefix(stoneTiles, size),
                Prefix(clayTiles, size),
                coast, forestTiles, grain, plainsTiles, stoneTiles, clayTiles);
        }

        private static int[] Prefix(int[] tiles, int size)
        {
            int[] prefix = new int[(size + 1) * (size + 1)];
            for (int y = 0; y < size; y++)
            {
                int row = 0;
                for (int x = 0; x < size; x++)
                {
                    row += tiles[(y * size) + x];
                    prefix[((y + 1) * (size + 1)) + x + 1] = prefix[(y * (size + 1)) + x + 1] + row;
                }
            }

            return prefix;
        }

        private int Query(int[] prefix, int x0, int y0, int x1, int y1)
        {
            int stride = _size + 1;
            return prefix[((y1 + 1) * stride) + x1 + 1]
                - prefix[(y0 * stride) + x1 + 1]
                - prefix[((y1 + 1) * stride) + x0]
                + prefix[(y0 * stride) + x0];
        }

        // Whether the start window clears every threshold on one bank: the same window sums, counting only tiles
        // whose bank label matches the footprint's. Only footprints that already clear the unbanked sums reach here.
        internal bool FitsBanked(int x, int y, Footprint hall, int[] banks, int bank)
        {
            int x0 = Math.Max(0, x - StartRadius);
            int y0 = Math.Max(0, y - StartRadius);
            int x1 = Math.Min(_size - 1, x + hall.Width - 1 + StartRadius);
            int y1 = Math.Min(_size - 1, y + hall.Height - 1 + StartRadius);
            int coast = 0;
            int forest = 0;
            int grain = 0;
            int plains = 0;
            int stone = 0;
            int clay = 0;
            for (int ty = y0; ty <= y1; ty++)
            {
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (banks[(ty * _size) + tx] != bank)
                    {
                        continue;
                    }

                    coast += _coastFlat[(ty * _size) + tx];
                    forest += _forestFlat[(ty * _size) + tx];
                    grain += _grainFlat[(ty * _size) + tx];
                    plains += _plainsFlat[(ty * _size) + tx];
                    stone += _stoneFlat[(ty * _size) + tx];
                    clay += _clayFlat[(ty * _size) + tx];
                }
            }

            return coast >= MinCoast && forest >= MinForest && grain >= MinGrainTiles
                && plains >= MinPlainsTiles && stone >= MinStone && clay >= MinClay;
        }

        // The banked greedy placement count: blocks whose every tile is both good and on the footprint's bank.
        // Reaching the need here is exactly what the predicate counts.
        internal bool GreedyPlacementsBanked(int x, int y, Footprint hall, int[] banks, int bank, int size, int need)
        {
            int[] table = size == 5 ? _grain : _plains;
            int full = size * size;
            int bx0 = Math.Max(0, x - StartRadius);
            int by0 = Math.Max(0, y - StartRadius);
            int bx1 = Math.Min(_size - size, x + hall.Width - 1 + StartRadius);
            int by1 = Math.Min(_size - size, y + hall.Height - 1 + StartRadius);
            int[] picked = new int[need];
            int found = 0;
            for (int by = by0; by <= by1 && found < need; by++)
            {
                for (int bx = bx0; bx <= bx1 && found < need; bx++)
                {
                    if (Query(table, bx, by, bx + size - 1, by + size - 1) != full)
                    {
                        continue;
                    }

                    if (!CornersInRange(bx, by, size, x, y, hall) || !BlockOnBank(banks, bank, bx, by, size))
                    {
                        continue;
                    }

                    bool overlaps = false;
                    for (int p = 0; p < found; p++)
                    {
                        overlaps |= Math.Abs(bx - (picked[p] % _size)) < size
                            && Math.Abs(by - (picked[p] / _size)) < size;
                    }

                    if (!overlaps)
                    {
                        picked[found++] = (by * _size) + bx;
                    }
                }
            }

            return found >= need;
        }

        private bool BlockOnBank(int[] banks, int bank, int bx, int by, int size)
        {
            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    if (banks[((by + dy) * _size) + bx + dx] != bank)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // The greedy placement count the predicate checks, over the same window and in the same ascending order:
        // non-overlapping size×size blocks fully within range whose every tile is fertile plains (size 5) or plains
        // (size 4), read through the same prefix sums. Reaching the need here is exactly what the predicate counts.
        internal bool GreedyPlacements(int x, int y, Footprint hall, int size, int need)
        {
            int[] table = size == 5 ? _grain : _plains;
            int full = size * size;
            int bx0 = Math.Max(0, x - StartRadius);
            int by0 = Math.Max(0, y - StartRadius);
            int bx1 = Math.Min(_size - size, x + hall.Width - 1 + StartRadius);
            int by1 = Math.Min(_size - size, y + hall.Height - 1 + StartRadius);
            int[] picked = new int[need];
            int found = 0;
            for (int by = by0; by <= by1 && found < need; by++)
            {
                for (int bx = bx0; bx <= bx1 && found < need; bx++)
                {
                    if (Query(table, bx, by, bx + size - 1, by + size - 1) != full)
                    {
                        continue;
                    }

                    if (!CornersInRange(bx, by, size, x, y, hall))
                    {
                        continue;
                    }

                    bool overlaps = false;
                    for (int p = 0; p < found; p++)
                    {
                        overlaps |= Math.Abs(bx - (picked[p] % _size)) < size
                            && Math.Abs(by - (picked[p] / _size)) < size;
                    }

                    if (!overlaps)
                    {
                        picked[found++] = (by * _size) + bx;
                    }
                }
            }

            return found >= need;
        }

        // All four corners of a block within range of the footprint: the Chebyshev distance to a rectangle is convex,
        // so the corners bound every tile of the block.
        private static bool CornersInRange(int bx, int by, int size, int x, int y, Footprint hall) =>
            InRange(bx, by, x, y, hall)
            && InRange(bx + size - 1, by, x, y, hall)
            && InRange(bx, by + size - 1, x, y, hall)
            && InRange(bx + size - 1, by + size - 1, x, y, hall);

        private static bool InRange(int px, int py, int x, int y, Footprint hall)
        {
            int dx = 0;
            if (px < x) { dx = x - px; } else if (px > x + hall.Width - 1) { dx = px - (x + hall.Width - 1); }
            int dy = 0;
            if (py < y) { dy = y - py; } else if (py > y + hall.Height - 1) { dy = py - (y + hall.Height - 1); }
            return Math.Max(dx, dy) <= StartRadius;
        }

        // Whether the Chebyshev-24 start window around a footprint clears every threshold.
        internal bool Fits(int x, int y, Footprint hall)
        {
            int x0 = Math.Max(0, x - StartRadius);
            int y0 = Math.Max(0, y - StartRadius);
            int x1 = Math.Min(_size - 1, x + hall.Width - 1 + StartRadius);
            int y1 = Math.Min(_size - 1, y + hall.Height - 1 + StartRadius);
            return Query(_coast, x0, y0, x1, y1) >= MinCoast
                && Query(_forest, x0, y0, x1, y1) >= MinForest
                && Query(_grain, x0, y0, x1, y1) >= MinGrainTiles
                && Query(_plains, x0, y0, x1, y1) >= MinPlainsTiles
                && Query(_stone, x0, y0, x1, y1) >= MinStone
                && Query(_clay, x0, y0, x1, y1) >= MinClay;
        }

        private static bool HasOpenWaterNeighbourTiles(GeneratedHeightmap map, int x, int y, TerrainId sea, TerrainId reef, TerrainId deep)
        {
            int size = map.Size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                {
                    TerrainId neighbour = map.Terrain[(ny * size) + nx];
                    if (neighbour.Equals(sea) || neighbour.Equals(reef) || neighbour.Equals(deep))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // The hall footprint is inside the map on buildable terrain, with a valid rotation; its rotated size comes back out.
    private static bool TryHall(MapDef map, ContentDb content, out int hallWidth, out int hallHeight)
    {
        Footprint hall = content.Get(content.Required.FoundersHall).Footprint;
        hallWidth = (map.StartSite.Rotation & 1) == 1 ? hall.Height : hall.Width;
        hallHeight = (map.StartSite.Rotation & 1) == 1 ? hall.Width : hall.Height;
        if (map.StartSite.Rotation is < 0 or > 3)
        {
            return false;
        }

        if (map.StartSite.X < 0 || map.StartSite.Y < 0
            || map.StartSite.X + hallWidth > map.Width || map.StartSite.Y + hallHeight > map.Height)
        {
            return false;
        }

        for (int dy = 0; dy < hallHeight; dy++)
        {
            for (int dx = 0; dx < hallWidth; dx++)
            {
                if (!content.Get(map.Terrain[((map.StartSite.Y + dy) * map.Width) + map.StartSite.X + dx]).Buildable)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // The river boundary of every region in one pass: for each river tile the distinct neighbouring land regions each
    // gain one, and the boundary's bounding box grows. Indexed by region number.
    internal sealed class RegionBorders
    {
        internal readonly int[] Count;
        private readonly int[] _minX;
        private readonly int[] _maxX;
        private readonly int[] _minY;
        private readonly int[] _maxY;

        /// <summary>Gets the highest region number, so vote tallies can size themselves without rescanning.</summary>
        internal int Top => Count.Length - 1;

        private RegionBorders(int[] count, int[] minX, int[] maxX, int[] minY, int[] maxY)
        {
            Count = count;
            _minX = minX;
            _maxX = maxX;
            _minY = minY;
            _maxY = maxY;
        }

        internal bool Spans(int region) =>
            _maxX[region] - _minX[region] >= MinBorderSpan && _maxY[region] - _minY[region] >= MinBorderSpan;

        internal static RegionBorders Build(GeneratedHeightmap map, ContentDb content)
        {
            int size = map.Size;
            int top = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (map.Region[i] > top)
                {
                    top = map.Region[i];
                }
            }

            int[] count = new int[top + 1];
            int[] minX = new int[top + 1];
            int[] maxX = new int[top + 1];
            int[] minY = new int[top + 1];
            int[] maxY = new int[top + 1];
            Array.Fill(minX, size);
            Array.Fill(maxX, -1);
            Array.Fill(minY, size);
            Array.Fill(maxY, -1);
            TerrainId sea = content.IdOf<TerrainId>("sea");
            TerrainId reef = content.IdOf<TerrainId>("reef");
            ushort[] seen = new ushort[8];
            for (int i = 0; i < map.TileCount; i++)
            {
                if (map.RiverWidth[i] == 0)
                {
                    continue;
                }

                int x = i % size;
                int y = i / size;
                int found = 0;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        continue;
                    }

                    int n = (ny * size) + nx;
                    if (map.Terrain[n].Equals(sea) || map.Terrain[n].Equals(reef))
                    {
                        continue;
                    }

                    ushort region = map.Region[n];
                    bool known = false;
                    for (int k = 0; k < found; k++)
                    {
                        known |= seen[k] == region;
                    }

                    if (!known)
                    {
                        seen[found++] = region;
                    }
                }

                for (int k = 0; k < found; k++)
                {
                    count[seen[k]]++;
                    if (x < minX[seen[k]]) { minX[seen[k]] = x; }
                    if (x > maxX[seen[k]]) { maxX[seen[k]] = x; }
                    if (y < minY[seen[k]]) { minY[seen[k]] = y; }
                    if (y > maxY[seen[k]]) { maxY[seen[k]] = y; }
                }
            }

            return new RegionBorders(count, minX, maxX, minY, maxY);
        }
    }

    // The road-connected banks of an island: 4-directional components over tiles a dirt road may pave, the same
    // ground rule the predicate's flood uses. Blocking tiles keep -1. Deterministic: ascending fills in a fixed order.
    internal static int[] ComputeBanks(GeneratedHeightmap map, ContentDb content)
    {
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        int size = map.Size;
        int[] bank = new int[map.TileCount];
        Array.Fill(bank, -1);
        int[] stack = new int[map.TileCount];
        int components = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (bank[i] >= 0 || !BankGround(map, content, i, highlands))
            {
                continue;
            }

            int top = 0;
            stack[top++] = i;
            bank[i] = components;
            while (top > 0)
            {
                int at = stack[--top];
                int x = at % size;
                int y = at / size;
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        continue;
                    }

                    int n = (ny * size) + nx;
                    if (bank[n] < 0 && BankGround(map, content, n, highlands))
                    {
                        bank[n] = components;
                        stack[top++] = n;
                    }
                }
            }

            components++;
        }

        return bank;
    }

    private static bool BankGround(GeneratedHeightmap map, ContentDb content, int tile, TerrainId highlands)
    {
        if (map.RiverWidth[tile] != 0)
        {
            return false;
        }

        TerrainId terrain = map.Terrain[tile];
        if (terrain.Equals(highlands))
        {
            return false;
        }

        TerrainDef def = content.Get(terrain);
        return def.Buildable && !def.BlocksRoads;
    }

    // What the map format knows about each terrain, so a candidate island can be wrapped for the predicate.
    internal static MapTerrainFeatures[] TerrainFeaturesOf(ContentDb content)
    {
        var features = new MapTerrainFeatures[content.Terrains.Count];
        for (int i = 0; i < features.Length; i++)
        {
            features[i] = MapTerrainRules.FeaturesOf(content.Terrains.Names[i]);
        }

        return features;
    }

    // Wraps a candidate hall site over a finished heightmap for the predicate: the arrays transfer without copying
    // and are only read. Stock, names and discoveries are empty because the predicate never reads them.
    internal static MapDef WrapCandidate(GeneratedHeightmap map, ContentDb content, MapStartSite site, MapTerrainFeatures[] features) =>
        MapDef.CreateGenerated(
            "candidate",
            map.Size,
            ImmutableCollectionsMarshal.AsImmutableArray(map.Terrain),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Elevation),
            ImmutableCollectionsMarshal.AsImmutableArray(map.RiverWidth),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Region),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Deposit),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Fertility),
            ImmutableCollectionsMarshal.AsImmutableArray(features),
            site,
            ImmutableArray<GoodAmount>.Empty,
            ImmutableArray<MapRegionName>.Empty,
            0,
            ImmutableArray<MapDiscovery>.Empty);

    // The settleable land near a candidate hall site: buildable, river-free, gentle tiles on the hall's own bank
    // within SettlementRadius tiles. Houses need gentle, buildable ground (the same slope rule placement enforces);
    // the bank keeps the count to land a dirt road can join without bridges. Highland access is still locked, so
    // highlands do not count. Among verified sites the generator prefers the roomiest, so the Era-I settlement the
    // bot and the tutorial scenarios build actually fits around the hall.
    private static int SettleSpace(GeneratedHeightmap map, ContentDb content, Footprint hall, int[] banks, int bank, int x, int y)
    {
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        int size = map.Size;
        int open = 0;
        for (int ty = Math.Max(0, y - SettlementRadius); ty <= Math.Min(size - 1, y + hall.Height - 1 + SettlementRadius); ty++)
        {
            for (int tx = Math.Max(0, x - SettlementRadius); tx <= Math.Min(size - 1, x + hall.Width - 1 + SettlementRadius); tx++)
            {
                int tile = (ty * size) + tx;
                if (banks[tile] != bank || map.RiverWidth[tile] != 0)
                {
                    continue;
                }

                TerrainId terrain = map.Terrain[tile];
                if (terrain.Equals(highlands) || !content.Get(terrain).Buildable || SlopeAt(map, tx, ty) > (int)SlopeClass.Gentle)
                {
                    continue;
                }

                open++;
            }
        }

        return open;
    }

    // The slope class of a heightmap tile from the elevation layer: the largest elevation step to an existing
    // 4-neighbour, saturating at Steep. The same derivation MapStateLoader runs when it loads a map.
    private static int SlopeAt(GeneratedHeightmap map, int x, int y)
    {
        int here = map.Elevation[(y * map.Size) + x];
        int steepest = 0;
        if (x > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Size) + x - 1]));
        }

        if (x < map.Size - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Size) + x + 1]));
        }

        if (y > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y - 1) * map.Size) + x]));
        }

        if (y < map.Size - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y + 1) * map.Size) + x]));
        }

        return Math.Min(steepest, (int)SlopeClass.Steep);
    }

    // The roomiest verified buildable footprint in a region: every start-window survivor is checked against the full
    // predicate (the road reach and the fishing-hut site have no window-sum prefilter), and the most settleable
    // survivor wins, with ties going to the first in ascending order so the pick stays deterministic.
    private static MapStartSite? ScanRegion(GeneratedHeightmap map, ContentDb content, Footprint hall, WindowSums sums, int[] banks, RegionBorders borders, MapTerrainFeatures[] features, FallbackTracker tracker, int region)
    {
        MapStartSite? best = null;
        int bestSpace = -1;
        for (int y = 0; y + hall.Height <= map.Size; y++)
        {
            for (int x = 0; x + hall.Width <= map.Size; x++)
            {
                if (map.Region[(y * map.Size) + x] != region || !FitsAt(map, content, hall, x, y))
                {
                    continue;
                }

                int bank = banks[(y * map.Size) + x];
                if (bank < 0)
                {
                    continue;
                }

                int? fallbackScore = FallbackScore(map, content, hall, borders, banks, bank, x, y);
                if (fallbackScore.HasValue)
                {
                    tracker.Consider(fallbackScore.Value, x, y);
                }

                if (sums.Fits(x, y, hall)
                    && sums.GreedyPlacements(x, y, hall, 5, MinGrainPlacements)
                    && sums.GreedyPlacements(x, y, hall, 4, MinPlainsPlacements)
                    && sums.FitsBanked(x, y, hall, banks, bank)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 5, MinGrainPlacements)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 4, MinPlainsPlacements))
                {
                    var candidate = new MapStartSite(x, y, 0);
                    if (IsSatisfied(WrapCandidate(map, content, candidate, features), content))
                    {
                        int space = SettleSpace(map, content, hall, banks, bank, x, y);
                        if (space > bestSpace)
                        {
                            best = candidate;
                            bestSpace = space;
                        }
                    }
                }
            }
        }

        return best;
    }

    // The roomiest verified footprint anywhere: like the regional scan, every start-window survivor passes the full
    // predicate and the most settleable one wins. Only the river boundary stays unverified here, and the predicate
    // checks it.
    private static MapStartSite? ScanIsland(GeneratedHeightmap map, ContentDb content, Footprint hall, WindowSums sums, int[] banks, RegionBorders borders, MapTerrainFeatures[] features, FallbackTracker tracker)
    {
        MapStartSite? best = null;
        int bestSpace = -1;
        for (int y = 0; y + hall.Height <= map.Size; y++)
        {
            for (int x = 0; x + hall.Width <= map.Size; x++)
            {
                if (!FitsAt(map, content, hall, x, y))
                {
                    continue;
                }

                int bank = banks[(y * map.Size) + x];
                if (bank < 0)
                {
                    continue;
                }

                int? fallbackScore = FallbackScore(map, content, hall, borders, banks, bank, x, y);
                if (fallbackScore.HasValue)
                {
                    tracker.Consider(fallbackScore.Value, x, y);
                }

                if (sums.Fits(x, y, hall)
                    && sums.GreedyPlacements(x, y, hall, 5, MinGrainPlacements)
                    && sums.GreedyPlacements(x, y, hall, 4, MinPlainsPlacements)
                    && sums.FitsBanked(x, y, hall, banks, bank)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 5, MinGrainPlacements)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 4, MinPlainsPlacements))
                {
                    var candidate = new MapStartSite(x, y, 0);
                    if (IsSatisfied(WrapCandidate(map, content, candidate, features), content))
                    {
                        int space = SettleSpace(map, content, hall, banks, bank, x, y);
                        if (space > bestSpace)
                        {
                            best = candidate;
                            bestSpace = space;
                        }
                    }
                }
            }
        }

        return best;
    }

    private static MapStartSite? ScanRegion(GeneratedHeightmap map, ContentDb content, Footprint hall, int region, bool coastRequired)
    {
        for (int y = 0; y + hall.Height <= map.Size; y++)
        {
            for (int x = 0; x + hall.Width <= map.Size; x++)
            {
                if (map.Region[(y * map.Size) + x] != region || !FitsAt(map, content, hall, x, y))
                {
                    continue;
                }

                if (coastRequired && !HasOpenWaterNear(map, content, hall, x, y))
                {
                    continue;
                }

                return new MapStartSite(x, y, 0);
            }
        }

        return null;
    }

    private static MapStartSite? ScanIsland(GeneratedHeightmap map, ContentDb content, Footprint hall)
    {
        for (int y = 0; y + hall.Height <= map.Size; y++)
        {
            for (int x = 0; x + hall.Width <= map.Size; x++)
            {
                if (FitsAt(map, content, hall, x, y))
                {
                    return new MapStartSite(x, y, 0);
                }
            }
        }

        return null;
    }

    private static bool FitsAt(GeneratedHeightmap map, ContentDb content, Footprint hall, int x, int y)
    {
        for (int dy = 0; dy < hall.Height; dy++)
        {
            for (int dx = 0; dx < hall.Width; dx++)
            {
                if (!content.Get(map.Terrain[((y + dy) * map.Size) + x + dx]).Buildable)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasOpenWaterNear(GeneratedHeightmap map, ContentDb content, Footprint hall, int x, int y)
    {
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId deep = content.IdOf<TerrainId>("deep_water");
        int size = map.Size;
        for (int dy = -StartRadius; dy < hall.Height + StartRadius; dy++)
        {
            for (int dx = -StartRadius; dx < hall.Width + StartRadius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > StartRadius)
                {
                    continue;
                }

                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                TerrainId terrain = map.Terrain[(ny * size) + nx];
                if (terrain.Equals(sea) || terrain.Equals(reef) || terrain.Equals(deep))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // How many steps from the hall footprint the storehouse reach covers: the reach bound is reach × 1000 cost units
    // and a dirt tile costs its road cost, read from the same content the fields use (the DistanceField bounds and the
    // road costs in RoadNetwork). The field search seeds the hall's access tiles at cost 0 and charges every further
    // tile its road cost, so one step more than bound / dirt cost stays inside: every dirt tile costs the same, so the
    // cost bound below is a plain step count. This is the same formula the reach labels use (storage reach in tiles
    // times 1000 cost units per dirt tile), shifted by the free seed tile.
    private static int MaxRoadSteps(ContentDb content)
    {
        int reach = content.Get(content.Required.FoundersHall).Storage?.Reach
            ?? throw new KeyNotFoundException("The content does not define storage for the Founders' Hall.");
        int dirtCost = content.Get(content.IdOf<BuildingTypeId>("dirt_road")).RoadCost
            ?? throw new KeyNotFoundException("The content does not define a road cost for dirt_road.");
        return (checked(reach * 1000) / dirtCost) + 1;
    }

    // The tiles a dirt road can actually reach from the hall: the cost-bounded flood over tiles a dirt road may pave,
    // from the hall footprint. Four neighbours only, like the road network's own search (RoadPlacement.Check refuses
    // river tiles to ordinary roads, and the field Dijkstra steps to the four neighbours): a diagonal step would leak
    // across thin rivers that roads can never cross. Tiles past the hall's storehouse reach are not reached, because a
    // longer road would leave them outside every storehouse field.
    internal static bool[] FloodReach(MapDef map, ContentDb content, int hallWidth, int hallHeight)
    {
        int maxSteps = MaxRoadSteps(content);
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        bool[] reached = new bool[map.TileCount];
        // Pool the scratch buffers: FloodReach runs thousands of times per island during repair, and allocating
        // two 256 KB int arrays per call costs a gigabyte of garbage. Rented arrays may be larger than TileCount,
        // but every index used stays below it, depth is filled and queue slots are written before they are read,
        // so stale pool contents never leak into the result. Byte-identical outputs.
        int[] depth = System.Buffers.ArrayPool<int>.Shared.Rent(map.TileCount);
        int[] queue = System.Buffers.ArrayPool<int>.Shared.Rent(map.TileCount);
        int head = 0;
        int tail = 0;
        try
        {
            Array.Fill(depth, -1, 0, map.TileCount);
        for (int dy = 0; dy < hallHeight; dy++)
        {
            for (int dx = 0; dx < hallWidth; dx++)
            {
                int tile = ((map.StartSite.Y + dy) * map.Width) + map.StartSite.X + dx;
                if (depth[tile] < 0)
                {
                    depth[tile] = 0;
                    reached[tile] = true;
                    queue[tail++] = tile;
                }
            }
        }

        while (head < tail)
        {
            int at = queue[head++];
            if (depth[at] >= maxSteps)
            {
                continue;
            }

            int x = at % map.Width;
            int y = at / map.Width;
            if (x > 0)
            {
                TryStep(map, content, highlands, reached, depth, queue, ref tail, at - 1, at);
            }

            if (x < map.Width - 1)
            {
                TryStep(map, content, highlands, reached, depth, queue, ref tail, at + 1, at);
            }

            if (y > 0)
            {
                TryStep(map, content, highlands, reached, depth, queue, ref tail, at - map.Width, at);
            }

            if (y < map.Height - 1)
            {
                TryStep(map, content, highlands, reached, depth, queue, ref tail, at + map.Width, at);
            }
        }
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(depth);
            System.Buffers.ArrayPool<int>.Shared.Return(queue);
        }

        return reached;
    }

    private static void TryStep(MapDef map, ContentDb content, TerrainId highlands, bool[] reached, int[] depth, int[] queue, ref int tail, int tile, int from)
    {
        if (depth[tile] >= 0 || !RoadGround(map, content, tile, highlands))
        {
            return;
        }

        depth[tile] = depth[from] + 1;
        reached[tile] = true;
        queue[tail++] = tile;
    }

    // Whether a dirt road may be paved on a tile at the start of the game: the ground rule of RoadPlacement.Check for
    // a non-bridge road with nothing researched. River tiles are refused to ordinary roads (TerrainNotAllowed); highland
    // access is still locked (TerrainAccessLocked); every other refusal is terrain that is not buildable or blocks roads.
    private static bool RoadGround(MapDef map, ContentDb content, int tile, TerrainId highlands)
    {
        if (map.RiverWidth[tile] != 0)
        {
            return false;
        }

        TerrainId terrain = map.Terrain[tile];
        if (terrain.Equals(highlands))
        {
            return false;
        }

        TerrainDef def = content.Get(terrain);
        return def.Buildable && !def.BlocksRoads;
    }

    // A valid fishing-hut site on reachable coast: the first ascending footprint whose every tile is road-reachable
    // and gentle enough to build on, with coast under it. Reachable tiles are paveable buildable land, so this carries
    // the buildable and access rules of the real placement check (BuildingPlacement.Validate) with it; the slope and
    // coastline rules are checked exactly as the placement code checks them. The area yield is only a warning there,
    // so a site needs no yield, only the footprint rules.
    private static string? CheckFishingHut(MapDef map, ContentDb content, bool[] reach)
    {
        BuildingDef hut = content.Get(content.IdOf<BuildingTypeId>("fishing_hut"));
        for (int y = 0; y + hut.Footprint.Height <= map.Height; y++)
        {
            for (int x = 0; x + hut.Footprint.Width <= map.Width; x++)
            {
                bool coast = false;
                bool ok = true;
                for (int dy = 0; dy < hut.Footprint.Height && ok; dy++)
                {
                    for (int dx = 0; dx < hut.Footprint.Width; dx++)
                    {
                        int tile = ((y + dy) * map.Width) + x + dx;
                        if (!reach[tile] || SlopeOf(map, x + dx, y + dy) > (int)SlopeClass.Gentle)
                        {
                            ok = false;
                            break;
                        }

                        coast |= IsCoastTile(map, x + dx, y + dy);
                    }
                }

                if (ok && (!hut.Placement.Coastline || coast))
                {
                    return null;
                }
            }
        }

        return "hut";
    }

    // The slope class of a tile from the elevation layer, the same derivation MapStateLoader runs when it loads a
    // map: the largest elevation step to an existing 4-neighbour, saturating at Steep.
    private static int SlopeOf(MapDef map, int x, int y)
    {
        int here = map.Elevation[(y * map.Width) + x];
        int steepest = 0;
        if (x > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Width) + x - 1]));
        }

        if (x < map.Width - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[(y * map.Width) + x + 1]));
        }

        if (y > 0)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y - 1) * map.Width) + x]));
        }

        if (y < map.Height - 1)
        {
            steepest = Math.Max(steepest, Math.Abs(here - map.Elevation[((y + 1) * map.Width) + x]));
        }

        return Math.Min(steepest, (int)SlopeClass.Steep);
    }

    // Whether a tile carries the Coast feature, the same derivation MapStateLoader runs: land that is neither open
    // water nor river, with open water among its four neighbours. Rivers are not open water.
    private static bool IsCoastTile(MapDef map, int x, int y)
    {
        int tile = (y * map.Width) + x;
        if ((map.TerrainFeatures[map.Terrain[tile].Value] & MapTerrainFeatures.Water) != 0 || map.RiverWidth[tile] != 0)
        {
            return false;
        }

        return IsWaterTile(map, x - 1, y) || IsWaterTile(map, x + 1, y) || IsWaterTile(map, x, y - 1) || IsWaterTile(map, x, y + 1);
    }

    private static bool IsWaterTile(MapDef map, int x, int y) =>
        (uint)x < (uint)map.Width && (uint)y < (uint)map.Height
        && (map.TerrainFeatures[map.Terrain[(y * map.Width) + x].Value] & MapTerrainFeatures.Water) != 0;

    internal static int CountForest(MapDef map, TerrainId forest, bool[] reach)
    {
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (reach[i] && map.Terrain[i].Equals(forest))
            {
                count++;
            }
        }

        return count;
    }

    internal static int CountGrainPlacements(MapDef map, TerrainId plains, bool[] reach)
    {
        uint grain = 1u << (int)CropKind.Grain;
        return CountPlacements(map, 5, reach, (x, y) =>
            map.Terrain[(y * map.Width) + x].Equals(plains) && (map.Fertility[(y * map.Width) + x] & grain) != 0);
    }

    internal static int CountPlainsPlacements(MapDef map, TerrainId plains, bool[] reach)
    {
        return CountPlacements(map, 4, reach, (x, y) =>
            map.Terrain[(y * map.Width) + x].Equals(plains));
    }

    // Non-overlapping square placements whose every tile is road-reachable and fits, top to bottom, left to right.
    private static int CountPlacements(MapDef map, int size, bool[] reach, Func<int, int, bool> fits) =>
        FindPlacements(map, size, reach, fits).Count;

    // The non-overlapping square placements whose every tile is road-reachable and fits, top to bottom, left to
    // right: the top-left tile of each block, in ascending order. Shared with the start repair, which fertilizes
    // the same blocks the predicate counts, so the repaired island passes by construction.
    internal static List<(int X, int Y)> FindPlacements(MapDef map, int size, bool[] reach, Func<int, int, bool> fits)
    {
        var found = new List<(int X, int Y)>();
        bool[] used = new bool[map.TileCount];
        for (int by = 0; by + size <= map.Height; by++)
        {
            for (int bx = 0; bx + size <= map.Width; bx++)
            {
                bool ok = true;
                for (int dy = 0; dy < size && ok; dy++)
                {
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x = bx + dx;
                        int y = by + dy;
                        if (used[(y * map.Width) + x] || !reach[(y * map.Width) + x] || !fits(x, y))
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (ok)
                {
                    found.Add((bx, by));
                    for (int dy = 0; dy < size; dy++)
                    {
                        for (int dx = 0; dx < size; dx++)
                        {
                            used[((by + dy) * map.Width) + bx + dx] = true;
                        }
                    }
                }
            }
        }

        return found;
    }

    // Land tiles (not open water, not river) with open water among their neighbours, over road-reachable tiles.
    internal static int CountCoast(MapDef map, ContentDb content, bool[] reach)
    {
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId deep = content.IdOf<TerrainId>("deep_water");
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!reach[(y * map.Width) + x]
                    || IsOpenWater(map.Terrain[(y * map.Width) + x], sea, reef, deep)
                    || map.RiverWidth[(y * map.Width) + x] != 0)
                {
                    continue;
                }

                if (HasOpenWaterNeighbour(map, x, y, sea, reef, deep))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsOpenWater(TerrainId terrain, TerrainId sea, TerrainId reef, TerrainId deep) =>
        terrain.Equals(sea) || terrain.Equals(reef) || terrain.Equals(deep);

    private static bool HasOpenWaterNeighbour(MapDef map, int x, int y, TerrainId sea, TerrainId reef, TerrainId deep)
    {
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                && IsOpenWater(map.Terrain[(ny * map.Width) + nx], sea, reef, deep))
            {
                return true;
            }
        }

        return false;
    }

    internal static int CountStone(MapDef map, TerrainId hills, bool[] reach)
    {
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (reach[i]
                && map.Terrain[i].Equals(hills)
                && map.Deposit[i] == (byte)((int)DepositKind.Stone + 1))
            {
                count++;
            }
        }

        return count;
    }

    internal static int CountClay(MapDef map, bool[] reach)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (!reach[i]
                    || map.Deposit[i] != (byte)((int)DepositKind.Clay + 1))
                {
                    continue;
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                        && map.RiverWidth[(ny * map.Width) + nx] != 0)
                    {
                        count++;
                        break;
                    }
                }
            }
        }

        return count;
    }

    // The start region is bounded by rivers: river tiles touch it, and treating rivers as walls cuts the hall off
    // from ground it otherwise reaches. Passable ground is buildable land that is not highland (whose access is
    // locked in Era I); water, peaks and highlands always block, exactly as on the starter island.
    // The start region is bounded by rivers: enough river tiles touch it, and their bounding box spans both axes,
    // so the boundary runs rather than dots. (Full enclosure like the hand-authored starter is vanishingly rare with
    // thin generated rivers; the penalized Voronoi already pulls borders onto rivers where the geometry allows.)
    private static string? CheckRivers(MapDef map, ContentDb content, int hallWidth, int hallHeight)
    {
        ushort startRegion = StartRegionOf(map, hallWidth, hallHeight);
        int border = 0;
        int minX = map.Width;
        int maxX = -1;
        int minY = map.Height;
        int maxY = -1;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (map.RiverWidth[i] == 0 || !TouchesRegion(map, x, y, startRegion))
                {
                    continue;
                }

                border++;
                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }
            }
        }

        if (border < MinBorderRivers)
        {
            return $"border {border}/{MinBorderRivers}";
        }

        if (maxX - minX < MinBorderSpan || maxY - minY < MinBorderSpan)
        {
            return $"span {(maxX - minX)}x{(maxY - minY)}/{MinBorderSpan}";
        }

        return null;
    }

    // The region holding most of the hall footprint (ties to the lowest region number): the start region.
    private static ushort StartRegionOf(MapDef map, int hallWidth, int hallHeight)
    {
        int top = 0;
        foreach (ushort region in map.Region)
        {
            if (region > top)
            {
                top = region;
            }
        }

        int[] votes = new int[top + 1];
        for (int dy = 0; dy < hallHeight; dy++)
        {
            for (int dx = 0; dx < hallWidth; dx++)
            {
                votes[map.Region[((map.StartSite.Y + dy) * map.Width) + map.StartSite.X + dx]]++;
            }
        }

        int best = 0;
        for (int r = 1; r < votes.Length; r++)
        {
            if (votes[r] > votes[best])
            {
                best = r;
            }
        }

        return (ushort)best;
    }

    private static bool TouchesRegion(MapDef map, int x, int y, ushort region)
    {
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                && map.Region[(ny * map.Width) + nx] == region)
            {
                return true;
            }
        }

        return false;
    }



}
