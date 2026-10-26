using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// The start guarantee (architecture plan §Island and map): the start region is bounded by rivers and holds everything
/// Era I needs before bridges — forest, grain plains, sheep pasture, coastline, hills with stone and clay riverbanks.
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

        // Every count below is measured over the hall's road-connected bank: tiles a dirt road can reach without
        // crossing a river, open water, peaks, marsh or locked highlands. A start whose farmland lies across the
        // river meets no guarantee worth the name, because carts only run over road tiles.
        bool[] bank = FloodBank(map, content, hallWidth, hallHeight);
        int countedForest = CountForest(map, forest, hallWidth, hallHeight, bank);
        if (countedForest < MinForest)
        {
            failing.Add($"forest {countedForest}/{MinForest}");
        }

        int grain = CountGrainPlacements(map, plains, hallWidth, hallHeight, bank);
        if (grain < MinGrainPlacements)
        {
            failing.Add($"grain {grain}/{MinGrainPlacements}");
        }

        int pasture = CountPlainsPlacements(map, plains, hallWidth, hallHeight, bank);
        if (pasture < MinPlainsPlacements)
        {
            failing.Add($"pasture {pasture}/{MinPlainsPlacements}");
        }

        int coast = CountCoast(map, content, hallWidth, hallHeight, bank);
        if (coast < MinCoast)
        {
            failing.Add($"coast {coast}/{MinCoast}");
        }

        int stone = CountStone(map, hills, hallWidth, hallHeight, bank);
        if (stone < MinStone)
        {
            failing.Add($"stone {stone}/{MinStone}");
        }

        int clay = CountClay(map, hallWidth, hallHeight, bank);
        if (clay < MinClay)
        {
            failing.Add($"clay {clay}/{MinClay}");
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

        // River-bordered regions first: the first ascending footprint whose region boundary runs and whose whole
        // start window clears every threshold on its own bank, including the exact greedy placement counts the
        // predicate checks. A site found here satisfies the predicate by construction.
        foreach (int region in order)
        {
            if (borders.Count[region] < MinBorderRivers || !borders.Spans(region))
            {
                continue;
            }

            MapStartSite? site = ScanRegion(map, content, hall, sums, banks, region);
            if (site is not null)
            {
                return site.Value;
            }
        }

        // Fallbacks: the first bank-clearing footprint anywhere (only the river boundary stays for the predicate
        // to verify), then a coastal footprint in the best region, then anything buildable.
        int best = order.Length > 0 ? order[0] : 0;
        MapStartSite? fallback = ScanIsland(map, content, hall, sums, banks)
            ?? ScanRegion(map, content, hall, best, coastRequired: true)
            ?? ScanRegion(map, content, hall, best, coastRequired: false)
            ?? ScanIsland(map, content, hall);
        if (fallback is null)
        {
            throw new InvalidOperationException("The generated island has no buildable Founders' Hall footprint.");
        }

        return fallback.Value;
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
    private sealed class WindowSums
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
    private sealed class RegionBorders
    {
        internal readonly int[] Count;
        private readonly int[] _minX;
        private readonly int[] _maxX;
        private readonly int[] _minY;
        private readonly int[] _maxY;

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
    private static int[] ComputeBanks(GeneratedHeightmap map, ContentDb content)
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

    // The first ascending buildable footprint in a region whose start window clears every threshold on its own
    // bank, including the exact greedy placement counts the predicate checks. A site found here satisfies the
    // predicate by construction.
    private static MapStartSite? ScanRegion(GeneratedHeightmap map, ContentDb content, Footprint hall, WindowSums sums, int[] banks, int region)
    {
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

                if (sums.Fits(x, y, hall)
                    && sums.GreedyPlacements(x, y, hall, 5, MinGrainPlacements)
                    && sums.GreedyPlacements(x, y, hall, 4, MinPlainsPlacements)
                    && sums.FitsBanked(x, y, hall, banks, bank)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 5, MinGrainPlacements)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 4, MinPlainsPlacements))
                {
                    return new MapStartSite(x, y, 0);
                }
            }
        }

        return null;
    }

    // The first ascending footprint anywhere whose start window clears every threshold on its own bank. Only the
    // river boundary stays for the predicate.
    private static MapStartSite? ScanIsland(GeneratedHeightmap map, ContentDb content, Footprint hall, WindowSums sums, int[] banks)
    {
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

                if (sums.Fits(x, y, hall)
                    && sums.GreedyPlacements(x, y, hall, 5, MinGrainPlacements)
                    && sums.GreedyPlacements(x, y, hall, 4, MinPlainsPlacements)
                    && sums.FitsBanked(x, y, hall, banks, bank)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 5, MinGrainPlacements)
                    && sums.GreedyPlacementsBanked(x, y, hall, banks, bank, 4, MinPlainsPlacements))
                {
                    return new MapStartSite(x, y, 0);
                }
            }
        }

        return null;
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

    // The Chebyshev distance from a tile to the hall footprint (0 inside it).
    private static int DistanceToHall(MapDef map, int x, int y, int hallWidth, int hallHeight)
    {
        int dx = 0;
        if (x < map.StartSite.X)
        {
            dx = map.StartSite.X - x;
        }
        else if (x > map.StartSite.X + hallWidth - 1)
        {
            dx = x - (map.StartSite.X + hallWidth - 1);
        }

        int dy = 0;
        if (y < map.StartSite.Y)
        {
            dy = map.StartSite.Y - y;
        }
        else if (y > map.StartSite.Y + hallHeight - 1)
        {
            dy = y - (map.StartSite.Y + hallHeight - 1);
        }

        return Math.Max(dx, dy);
    }

    // The hall's road-connected bank: the 4-directional flood over tiles a dirt road may pave, from the hall
    // footprint. Rivers, open water, peaks, marsh and locked highlands divide banks; a road can never join them.
    private static bool[] FloodBank(MapDef map, ContentDb content, int hallWidth, int hallHeight)
    {
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        bool[] reached = new bool[map.TileCount];
        int[] stack = new int[map.TileCount];
        int top = 0;
        for (int dy = 0; dy < hallHeight; dy++)
        {
            for (int dx = 0; dx < hallWidth; dx++)
            {
                int tile = ((map.StartSite.Y + dy) * map.Width) + map.StartSite.X + dx;
                if (!reached[tile])
                {
                    reached[tile] = true;
                    stack[top++] = tile;
                }
            }
        }

        while (top > 0)
        {
            int at = stack[--top];
            int x = at % map.Width;
            int y = at / map.Width;
            for (int d = 0; d < 8; d += 2)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)map.Width || (uint)ny >= (uint)map.Height)
                {
                    continue;
                }

                int n = (ny * map.Width) + nx;
                if (!reached[n] && RoadGround(map, content, n, highlands))
                {
                    reached[n] = true;
                    stack[top++] = n;
                }
            }
        }

        return reached;
    }

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

    private static int CountForest(MapDef map, TerrainId forest, int hallWidth, int hallHeight, bool[] bank)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (DistanceToHall(map, x, y, hallWidth, hallHeight) <= StartRadius
                    && bank[i]
                    && map.Terrain[i].Equals(forest))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountGrainPlacements(MapDef map, TerrainId plains, int hallWidth, int hallHeight, bool[] bank)
    {
        uint grain = 1u << (int)CropKind.Grain;
        return CountPlacements(map, 5, hallWidth, hallHeight, bank, (x, y) =>
            map.Terrain[(y * map.Width) + x].Equals(plains) && (map.Fertility[(y * map.Width) + x] & grain) != 0);
    }

    private static int CountPlainsPlacements(MapDef map, TerrainId plains, int hallWidth, int hallHeight, bool[] bank)
    {
        return CountPlacements(map, 4, hallWidth, hallHeight, bank, (x, y) =>
            map.Terrain[(y * map.Width) + x].Equals(plains));
    }

    // Non-overlapping square placements whose every tile is within range, on the bank and fits, top to bottom, left to right.
    private static int CountPlacements(MapDef map, int size, int hallWidth, int hallHeight, bool[] bank, Func<int, int, bool> fits)
    {
        bool[] used = new bool[map.TileCount];
        int found = 0;
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
                        if (used[(y * map.Width) + x] || !bank[(y * map.Width) + x] || DistanceToHall(map, x, y, hallWidth, hallHeight) > StartRadius || !fits(x, y))
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (ok)
                {
                    found++;
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

    // Land tiles (not open water, not river) with open water among their eight neighbours.
    private static int CountCoast(MapDef map, ContentDb content, int hallWidth, int hallHeight, bool[] bank)
    {
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId deep = content.IdOf<TerrainId>("deep_water");
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (DistanceToHall(map, x, y, hallWidth, hallHeight) > StartRadius
                    || !bank[(y * map.Width) + x]
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

    private static int CountStone(MapDef map, TerrainId hills, int hallWidth, int hallHeight, bool[] bank)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (DistanceToHall(map, x, y, hallWidth, hallHeight) <= StartRadius
                    && bank[i]
                    && map.Terrain[i].Equals(hills)
                    && map.Deposit[i] == (byte)((int)DepositKind.Stone + 1))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountClay(MapDef map, int hallWidth, int hallHeight, bool[] bank)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (DistanceToHall(map, x, y, hallWidth, hallHeight) > StartRadius
                    || !bank[i]
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
