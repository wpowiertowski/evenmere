using Evenmere.Content;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Traces rivers from flow accumulation on the generator's fine-grained relief (all integers, per
/// ADR-0003), and fills tiny enclosed seawater pockets with neighbouring land. A priority-flood
/// (Barnes) pass conditions the per-mille heights so every land tile drains to the open sea; D8 flow
/// on that surface gives each tile an accumulation count. Tiles above a size-scaled threshold become
/// rivers — tributaries join naturally and count — widening past a second threshold, with speckle and
/// too-close networks pruned away. Actual elevations are lightly carved along river tiles so they never
/// rise downstream. The same inputs always paint the same rivers: every tie-break is by tile index.
/// </summary>
internal static class RiverTracer
{


    /// <summary>The divisor applied to the island's biggest river accumulation to set the wide-river threshold.
    /// only the top discharge near the biggest mouths counts as wide.</summary>
    public const int WideThresholdDivisor = 3;

    /// <summary>A traced run is kept only when it adds at least this many new channel tiles.</summary>
    public const int MinNewRiverTiles = 3;

    /// <summary>A traced run longer than this many tiles (scaled to island size) is skipped: over-long walks
    /// are fill meanders across merged flats, and skipping them leaves room for the basin's separate
    /// drainages.</summary>
    public const int MaxRunLength = 512;

    /// <summary>The spring tiles sit in the hills or higher: traced runs start at or above this elevation.</summary>
    public const int SpringElevation = 8;

    /// <summary>The most main rivers kept per island: when more qualify, the smallest are removed.</summary>
    public const int MaxMainRivers = 5;

    /// <summary>River networks smaller than this many tiles are speckle, not rivers, and are removed.</summary>
    public const int MinRiverTiles = 6;

    /// <summary>A river system must reach at least this elevation: networks that never touch the hills
    /// are lowland creeks, not hill rivers, and are removed.</summary>
    public const int MinSourceElevation = 8;

    /// <summary>Mouths of distinct river systems stay at least this far apart in Chebyshev tiles; the smaller system goes.</summary>
    public const int MinMouthDistance = 12;

    /// <summary>River tiles of distinct systems stay further apart than this Chebyshev distance; the smaller system goes.</summary>
    public const int RiverGap = 3;

    /// <summary>No walk keeps the same compass direction for more than this many consecutive steps:
    /// walks that cannot turn are discarded, so no river ever runs straight for long.</summary>
    public const int MaxStraightRun = 12;

    /// <summary>Stream separation for the flood tie-break hash: it must differ from every other generator stream.</summary>
    public const int FloodTieStreamXor = unchecked((int)0x0F1E2D3C);

    /// <summary>The largest enclosed seawater pocket filled with neighbouring land; bigger pockets stay water.</summary>
    public const int MaxFillPocketTiles = 4;

    // Neighbour offsets in a fixed order (N, NE, E, SE, S, SW, W, NW), so ties break identically everywhere.
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    // The compass direction from one adjacent tile to another, matching the neighbour order above.
    private static int DirectionTo(int from, int to, int size)
    {
        int dx = (to % size) - (from % size) + 1;
        int dy = (to / size) - (from / size) + 1;
        return (dy * 3) + dx;
    }

    /// <summary>
    /// Traces rivers onto a heightmap in place: river tiles get the river terrain and a width class, and
    /// their elevations are carved so they never rise downstream. Fertility, deposits and regions are left
    /// untouched.
    /// </summary>
    /// <param name="map">The heightmap to paint; terrain and elevation are read, river width and carved elevations are written.</param>
    /// <param name="seed">The generator seed; it drives the flood tie-break hash.</param>
    /// <param name="content">The loaded content, which must define the sea and river terrains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this tracer needs.</exception>
    internal static void Trace(GeneratedHeightmap map, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId river = content.IdOf<TerrainId>("river");

        int size = map.Size;
        int tiles = map.TileCount;
        bool[] edgeSea = EdgeSea(map, sea);
        int[] relief = HeightmapBuilder.BuildRelief(size, seed).Height;
        int[] fill = Condition(size, relief, seed);
        int[] flow = FlowDirections(map, sea, fill);
        int[] order = DrainageOrder(map, sea, fill);
        int land = order.Length;

        int[] accumulation = new int[tiles];
        for (int o = 0; o < land; o++)
        {
            accumulation[order[o]] = 1;
        }

        for (int o = land - 1; o >= 0; o--)
        {
            int target = flow[order[o]];
            if (target >= 0)
            {
                accumulation[target] += accumulation[order[o]];
            }
        }

        // A tile drains to the edge exactly when its receiver does; outlets drain when they touch
        // edge-connected water. Receivers sit lower on the conditioned surface, so ascending order
        // always runs them first.
        bool[] drains = new bool[tiles];
        for (int o = 0; o < land; o++)
        {
            int i = order[o];
            int target = flow[i];
            drains[i] = target < 0 ? IsShore(map, sea, edgeSea, i) : drains[target];
        }

        // The wide threshold scales to the island's biggest draining basin.
        bool[] isRiver = new bool[tiles];

        TraceTributaries(map, sea, edgeSea, flow, fill, drains, order, isRiver);

        PruneRivers(map, sea, edgeSea, isRiver, accumulation);

        // Light carving: a sill never stands above its upstream neighbour. Only lowers tiles, never
        // raises them, and only along river tiles, so basins keep their shape. Upstream tiles run first
        // in descending conditioned-surface order, so each sill sees its final upstream level.
        byte[] elevation = map.Elevation;
        for (int o = land - 1; o >= 0; o--)
        {
            int i = order[o];
            if (!isRiver[i])
            {
                continue;
            }

            int target = flow[i];
            if (target >= 0 && isRiver[target] && elevation[target] > elevation[i])
            {
                elevation[target] = elevation[i];
            }
        }

        // The wide threshold scales to the biggest river discharges on this island, so the widest
        // mouth reaches count as wide while every island still grows both classes.
        int maxRiverAcc = 1;
        for (int i = 0; i < tiles; i++)
        {
            if (isRiver[i] && accumulation[i] > maxRiverAcc)
            {
                maxRiverAcc = accumulation[i];
            }
        }

        int wide = Math.Max(1, maxRiverAcc / WideThresholdDivisor);
        for (int i = 0; i < tiles; i++)
        {
            if (isRiver[i])
            {
                map.Terrain[i] = river;
                map.RiverWidth[i] = accumulation[i] >= wide ? (byte)2 : (byte)1;
            }
        }
    }

    // Traces runs from hill springs: draining hill tiles ordered by downstream length, longest
    // first. A run that reaches an earlier river keeps its new segment as a tributary; a run that
    // reaches the open sea with enough new channel becomes a brook. Hugging an earlier river without
    // joining it aborts the walk (parallel braids collapse to one channel), as do short stubs,
    // pit-ended walks and fill meanders. Joined tributaries count as part of their system.
    private static void TraceTributaries(
        GeneratedHeightmap map,
        TerrainId sea,
        bool[] edgeSea,
        int[] flow,
        int[] fill,
        bool[] drains,
        int[] order,
        bool[] isRiver)
    {
        int size = map.Size;
        int tiles = map.TileCount;

        int[] downstream = new int[tiles];
        for (int o = 0; o < order.Length; o++)
        {
            int i = order[o];
            int target = flow[i];
            downstream[i] = target < 0 ? 0 : downstream[target] + 1;
        }

        int springs = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (drains[i] && !map.Terrain[i].Equals(sea) && map.Elevation[i] >= SpringElevation)
            {
                springs++;
            }
        }

        long[] keys = new long[springs];
        int[] candidates = new int[springs];
        int count = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (drains[i] && !map.Terrain[i].Equals(sea) && map.Elevation[i] >= SpringElevation)
            {
                keys[count] = (((long)(int.MaxValue - downstream[i])) << 32) | (uint)i;
                candidates[count] = i;
                count++;
            }
        }

        Array.Sort(keys, candidates);

        int[] run = new int[tiles];
        int[] seen = new int[tiles];
        int walkId = 0;
        int maxRun = (size * MaxRunLength) / 320;
        for (int c = 0; c < count; c++)
        {
            walkId++;
            int length = WalkRun(map, sea, edgeSea, flow, fill, drains, isRiver, seen, walkId, candidates[c], run, maxRun);
            if (length < MinNewRiverTiles || length > maxRun)
            {
                continue;
            }

            // Veto any walk that would complete a 13-step straight run: mark tentatively, then
            // scan rays through the new tiles. A vetoed walk paints nothing.
            for (int k = 0; k < length; k++)
            {
                isRiver[run[k]] = true;
            }

            if (CompletesStraightRun(isRiver, run, length, size))
            {
                for (int k = 0; k < length; k++)
                {
                    isRiver[run[k]] = false;
                }

                continue;
            }

            // Carve along the walked path so elevation never rises downstream, including where the
            // walk sidled away from the straightest descent. Only lowers tiles.
            for (int k = 0; k < length; k++)
            {
                int i = run[k];
                if (k > 0 && map.Elevation[i] > map.Elevation[run[k - 1]])
                {
                    map.Elevation[i] = map.Elevation[run[k - 1]];
                }
            }

        }

    }

    // Walks downstream from a spring along the flow directions, stopping at a shore mouth (flow -1)
    // or on an earlier river (a tributary join keeps the new segment). Hugging an earlier river
    // without joining it aborts the walk, as do walks past the length cap and pit-ended walks that
    // never reach the sea. Most steps sidle to a non-steepest downhill neighbour instead, which breaks
    // ruler-straight runs into natural wiggles; narrow valleys with a single downhill neighbour stay
    // straight, as they should. Returns the number of new channel tiles, or -1 when discarded.
    private static int WalkRun(
        GeneratedHeightmap map,
        TerrainId sea,
        bool[] edgeSea,
        int[] flow,
        int[] fill,
        bool[] drains,
        bool[] isRiver,
        int[] seen,
        int walkId,
        int spring,
        int[] run,
        int maxRun)
    {
        if (isRiver[spring])
        {
            return -1;
        }

        int length = 0;
        int current = spring;
        int lastDir = -1;
        int straightRun = 0;
        int size = map.Size;
        while (true)
        {
            if (length > maxRun || TouchesRiver(map, isRiver, current, flow[current]))
            {
                return -1;
            }

            if (isRiver[current])
            {
                return -1;
            }

            run[length++] = current;
            seen[current] = walkId;
            int next = flow[current];
            if (next >= 0 && straightRun >= MaxStraightRun && lastDir >= 0)
            {
                int turn = TurnAside(map, sea, drains, isRiver, seen, walkId, current, lastDir, size);
                if (turn < 0)
                {
                    return -1;
                }

                next = turn;
            }

            if (next < 0)
            {
                if (IsShore(map, sea, edgeSea, current))
                {
                    return length;
                }

                return -1;
            }

            int dir = DirectionTo(current, next, size);
            if (dir == lastDir)
            {
                straightRun++;
            }
            else
            {
                straightRun = 1;
                lastDir = dir;
            }

            current = next;
        }
    }


    // A forced turn when a walk would otherwise run straight past the limit: the lowest downhill-or-level
    // draining neighbour in a different compass direction, or -1 when the walk is boxed in and must end.
    // Turning (rather than discarding) keeps rivers on flat islands, where every natural drainage runs
    // straight; the carve pass fixes any uphill step.
    private static int TurnAside(GeneratedHeightmap map, TerrainId sea, bool[] drains, bool[] isRiver, int[] seen, int walkId, int tile, int forbidden, int size)
    {
        int best = -1;
        int bestFill = int.MaxValue;
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
            {
                continue;
            }

            int n = (ny * size) + nx;
            if (n == tile || isRiver[n] || seen[n] == walkId || map.Terrain[n].Equals(sea))
            {
                continue;
            }

            if (DirectionTo(tile, n, size) == forbidden)
            {
                continue;
            }

            if (!drains[n])
            {
                continue;
            }

            int level = map.Elevation[n];
            if (level < bestFill)
            {
                bestFill = level;
                best = n;
            }
        }

        return best;
    }

    // Whether a tile touches painted river water anywhere but through its downstream neighbour: a step
    // towards a true tributary join only ever touches the river it flows into.
    // Whether tentatively painted new tiles complete a 13-step straight run of river: for each
    // new tile and each of the 4 axes, count consecutive river both ways. A total of 14 collinear
    // river tiles (13 steps) is a violation, wherever along the run the new tile sits.
    private static bool CompletesStraightRun(bool[] isRiver, int[] run, int length, int size)
    {
        for (int k = 0; k < length; k++)
        {
            int x = run[k] % size;
            int y = run[k] / size;
            for (int d = 0; d < 4; d++)
            {
                int total = 1;
                int nx = x;
                int ny = y;
                for (int s = 0; s < 13; s++)
                {
                    nx += _dx[d];
                    ny += _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        break;
                    }

                    if (!isRiver[(ny * size) + nx])
                    {
                        break;
                    }

                    total++;
                    if (total >= 14)
                    {
                        return true;
                    }
                }

                nx = x;
                ny = y;
                for (int s = 0; s < 13; s++)
                {
                    nx -= _dx[d];
                    ny -= _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        break;
                    }

                    if (!isRiver[(ny * size) + nx])
                    {
                        break;
                    }

                    total++;
                    if (total >= 14)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TouchesRiver(GeneratedHeightmap map, bool[] isRiver, int tile, int next)
    {
        int size = map.Size;
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
            {
                continue;
            }

            int n = (ny * size) + nx;
            if (n != next && isRiver[n])
            {
                return true;
            }
        }

        return false;
    }

    // Marks the sea tiles connected to the map edge: the flood fill starts on the border (the sea margin
    // guarantees border water) and spreads through sea tiles in 8-connectivity, so diagonally linked water
    // still counts as open sea.
    private static bool[] EdgeSea(GeneratedHeightmap map, TerrainId sea)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        bool[] connected = new bool[tiles];
        int[] stack = new int[tiles];
        int top = 0;
        for (int x = 0; x < size; x++)
        {
            top = PushSea(map, sea, connected, stack, top, x, 0);
            top = PushSea(map, sea, connected, stack, top, x, size - 1);
        }

        for (int y = 1; y < size - 1; y++)
        {
            top = PushSea(map, sea, connected, stack, top, 0, y);
            top = PushSea(map, sea, connected, stack, top, size - 1, y);
        }

        while (top > 0)
        {
            int at = stack[--top];
            int x = at % size;
            int y = at / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                {
                    top = PushSea(map, sea, connected, stack, top, nx, ny);
                }
            }
        }

        return connected;
    }

    private static int PushSea(GeneratedHeightmap map, TerrainId sea, bool[] connected, int[] stack, int top, int x, int y)
    {
        int i = (y * map.Size) + x;
        if (!connected[i] && map.Terrain[i].Equals(sea))
        {
            connected[i] = true;
            stack[top++] = i;
        }

        return top;
    }

    // A shore tile is a land tile with open sea among its eight neighbours: the mouth of a river run.
    // Pockets of enclosed sea do not count, so no river drains into a future lake.
    private static bool IsShore(GeneratedHeightmap map, TerrainId sea, bool[] edgeSea, int tile)
    {
        int size = map.Size;
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx < (uint)size && (uint)ny < (uint)size)
            {
                int n = (ny * size) + nx;
                if (edgeSea[n] && map.Terrain[n].Equals(sea))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The water tiles connected to the map edge in 8-connectivity, through sea and reef alike. The sea
    // margin guarantees border water to start from; reefs are always painted on connected sea.
    private static bool[] ConnectedWater(GeneratedHeightmap map, TerrainId sea, TerrainId reef)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        bool[] connected = new bool[tiles];
        int[] stack = new int[tiles];
        int top = 0;
        for (int x = 0; x < size; x++)
        {
            top = PushWater(map, sea, reef, connected, stack, top, x, 0);
            top = PushWater(map, sea, reef, connected, stack, top, x, size - 1);
        }

        for (int y = 1; y < size - 1; y++)
        {
            top = PushWater(map, sea, reef, connected, stack, top, 0, y);
            top = PushWater(map, sea, reef, connected, stack, top, size - 1, y);
        }

        while (top > 0)
        {
            int at = stack[--top];
            int x = at % size;
            int y = at / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                {
                    top = PushWater(map, sea, reef, connected, stack, top, nx, ny);
                }
            }
        }

        return connected;
    }

    private static int PushWater(GeneratedHeightmap map, TerrainId sea, TerrainId reef, bool[] connected, int[] stack, int top, int x, int y)
    {
        int i = (y * map.Size) + x;
        if (!connected[i] && (map.Terrain[i].Equals(sea) || map.Terrain[i].Equals(reef)))
        {
            connected[i] = true;
            stack[top++] = i;
        }

        return top;
    }

    // Paints one tiny pocket with the most common neighbouring land terrain and elevation. River tiles
    // never donate (a pocket filled with river would drain nowhere), and neither does water; ties break
    // towards the lowest terrain ID and elevation, so the choice is deterministic. A pocket ringed only
    // by river and water keeps its sea: it cannot happen on generated islands (runs are too thin to ring
    // a tile), and leaving water is always safe for the river and lake rules.
    private static void FillPocket(
        GeneratedHeightmap map,
        TerrainId sea,
        TerrainId reef,
        TerrainId river,
        ContentDb content,
        int[] members,
        int count)
    {
        int size = map.Size;
        int[] terrainVotes = new int[content.Terrains.Count];
        int[] elevationVotes = new int[HeightmapBuilder.MaxElevation + 1];
        for (int m = 0; m < count; m++)
        {
            int x = members[m] % size;
            int y = members[m] / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                TerrainId neighbour = map.Terrain[n];
                if (neighbour.Equals(sea) || neighbour.Equals(reef) || neighbour.Equals(river))
                {
                    continue;
                }

                terrainVotes[neighbour.Value]++;
                elevationVotes[map.Elevation[n]]++;
            }
        }

        int terrain = -1;
        for (int t = 0; t < terrainVotes.Length; t++)
        {
            if (terrainVotes[t] > 0 && (terrain < 0 || terrainVotes[t] > terrainVotes[terrain]))
            {
                terrain = t;
            }
        }

        if (terrain < 0)
        {
            return;
        }

        int elevation = 0;
        for (int e = 1; e < elevationVotes.Length; e++)
        {
            if (elevationVotes[e] > elevationVotes[elevation])
            {
                elevation = e;
            }
        }

        var fill = TerrainId.From((ushort)terrain);
        for (int m = 0; m < count; m++)
        {
            map.Terrain[members[m]] = fill;
            map.Elevation[members[m]] = (byte)elevation;
        }
    }

    /// <summary>
    /// Fills tiny enclosed seawater pockets with neighbouring land in place: every sea component of
    /// <see cref="MaxFillPocketTiles"/> tiles or fewer that is not connected to the map edge through water
    /// (sea and reef) takes the most common neighbouring land terrain and elevation. Bigger pockets stay
    /// water (inland lakes for fishing). Reef bars painted earlier can pinch off a pocket; the water-aware
    /// check fills those too. Fertility is painted after this, so filled tiles behave like ordinary land.
    /// </summary>
    /// <param name="map">The heightmap to fix; rivers must already be traced and reefs painted.</param>
    /// <param name="content">The loaded content, which must define the sea and reef terrains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this fix needs.</exception>
    internal static void FillTinyPockets(GeneratedHeightmap map, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId river = content.IdOf<TerrainId>("river");

        int size = map.Size;
        int tiles = map.TileCount;
        bool[] water = ConnectedWater(map, sea, reef);
        int[] component = new int[tiles];
        int[] members = new int[tiles];
        int[] stack = new int[tiles];
        int components = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (!map.Terrain[i].Equals(sea) || component[i] != 0)
            {
                continue;
            }

            components++;
            int count = 0;
            bool enclosed = true;
            stack[0] = i;
            int top = 1;
            component[i] = components;
            while (top > 0)
            {
                int at = stack[--top];
                members[count++] = at;
                if (water[at])
                {
                    enclosed = false;
                }

                int x = at % size;
                int y = at / size;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                    {
                        int n = (ny * size) + nx;
                        if (map.Terrain[n].Equals(sea) && component[n] == 0)
                        {
                            component[n] = components;
                            stack[top++] = n;
                        }
                    }
                }
            }

            if (enclosed && count <= MaxFillPocketTiles)
            {
                FillPocket(map, sea, reef, river, content, members, count);
            }
        }
    }

    // Priority-flood conditioning (Barnes) over the fine relief: depressions are filled with a unit
    // gradient, so every land tile has a strictly lower neighbour on the conditioned surface and drains
    // to the map edge. Drainage runs on the fine per-mille heights rather than the quantised steps, so
    // valleys keep their natural meanders instead of flattening into plateaus that drain in straight
    // lines. The binary heap orders by (fill, tie-break, index) with a seeded hash tie-break: index
    // order alone would comb flat basins into ruler-straight northward gradients. The total order makes
    // the result identical on every CPU.
    internal static int[] Condition(int size, int[] relief, int seed)
    {
        int tiles = size * size;
        int[] fill = new int[tiles];
        for (int i = 0; i < tiles; i++)
        {
            fill[i] = relief[i];
        }

        uint[] tie = new uint[tiles];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                tie[i] = IntegerNoise.Hash(x, y, seed ^ FloodTieStreamXor);
            }
        }

        bool[] closed = new bool[tiles];
        int[] heap = new int[tiles];
        int heapSize = 0;
        for (int x = 0; x < size; x++)
        {
            heapSize = HeapPushTile(heap, heapSize, fill, tie, closed, x);
            heapSize = HeapPushTile(heap, heapSize, fill, tie, closed, ((size - 1) * size) + x);
        }

        for (int y = 1; y < size - 1; y++)
        {
            heapSize = HeapPushTile(heap, heapSize, fill, tie, closed, y * size);
            heapSize = HeapPushTile(heap, heapSize, fill, tie, closed, (y * size) + size - 1);
        }

        while (heapSize > 0)
        {
            int current = HeapPop(heap, ref heapSize, fill, tie);
            int x = current % size;
            int y = current / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                if (closed[n])
                {
                    continue;
                }

                closed[n] = true;
                fill[n] = Math.Max(relief[n], fill[current] + 1);
                heapSize = HeapPushTile(heap, heapSize, fill, tie, n);
            }
        }

        return fill;
    }

    private static int HeapPushTile(int[] heap, int heapSize, int[] fill, uint[] tie, int tile)
    {
        heap[heapSize] = tile;
        int i = heapSize;
        heapSize++;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (HeapLess(fill, tie, heap[i], heap[parent]))
            {
                heap[i] = heap[parent];
                heap[parent] = tile;
                i = parent;
            }
            else
            {
                break;
            }
        }

        return heapSize;
    }

    private static int HeapPushTile(int[] heap, int heapSize, int[] fill, uint[] tie, bool[] closed, int tile)
    {
        if (closed[tile])
        {
            return heapSize;
        }

        closed[tile] = true;
        return HeapPushTile(heap, heapSize, fill, tie, tile);
    }

    private static int HeapPop(int[] heap, ref int heapSize, int[] fill, uint[] tie)
    {
        int top = heap[0];
        heapSize--;
        int last = heap[heapSize];
        heap[0] = last;
        int i = 0;
        while (true)
        {
            int left = (i * 2) + 1;
            int right = left + 1;
            int smallest = i;
            if (left < heapSize && HeapLess(fill, tie, heap[left], heap[smallest]))
            {
                smallest = left;
            }

            if (right < heapSize && HeapLess(fill, tie, heap[right], heap[smallest]))
            {
                smallest = right;
            }

            if (smallest == i)
            {
                break;
            }

            heap[i] = heap[smallest];
            heap[smallest] = last;
            i = smallest;
        }

        return top;
    }

    private static bool HeapLess(int[] fill, uint[] tie, int a, int b) =>
        fill[a] < fill[b] || (fill[a] == fill[b] && (tie[a] < tie[b] || (tie[a] == tie[b] && a < b)));

    // The downhill flow direction of every land tile on the conditioned surface: the lowest strictly
    // lower land neighbour in the fixed neighbour order, or -1 for a shore outlet. Conditioning
    // guarantees a lower neighbour exists; sea tiles never carry flow.
    private static int[] FlowDirections(GeneratedHeightmap map, TerrainId sea, int[] fill)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        int[] flow = new int[tiles];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                flow[i] = -1;
                if (map.Terrain[i].Equals(sea))
                {
                    continue;
                }

                int here = fill[i];
                int best = here;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        continue;
                    }

                    int n = (ny * size) + nx;
                    if (map.Terrain[n].Equals(sea))
                    {
                        continue;
                    }

                    int level = fill[n];
                    if (level < best)
                    {
                        best = level;
                        flow[i] = n;
                    }
                }
            }
        }

        return flow;
    }

    // All land tiles in ascending conditioned-surface order (packed (fill, index) keys sort deterministically).
    private static int[] DrainageOrder(GeneratedHeightmap map, TerrainId sea, int[] fill)
    {
        int tiles = map.TileCount;
        int land = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (!map.Terrain[i].Equals(sea))
            {
                land++;
            }
        }

        long[] keys = new long[land];
        int[] order = new int[land];
        int count = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (!map.Terrain[i].Equals(sea))
            {
                keys[count] = (((long)fill[i]) << 32) | (uint)i;
                order[count] = i;
                count++;
            }
        }

        Array.Sort(keys, order);
        return order;
    }

    // Prunes the selected networks: speckle components below MinRiverTiles go first, then networks that
    // never touch the hills, and then, of any pair of components whose mouths lie closer than
    // MinMouthDistance or whose tiles lie within RiverGap, the smaller system goes (ties break to the
    // lower first tile). Tributaries that join a system count as part of it and are never split. Labels
    // are assigned in ascending tile order and every tie-break is by tile index, so the outcome is
    // identical on every CPU.
    private static void PruneRivers(GeneratedHeightmap map, TerrainId sea, bool[] edgeSea, bool[] isRiver, int[] accumulation)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        int[] component = new int[tiles];
        int[] members = new int[tiles];
        int[] stack = new int[tiles];
        int components = 0;
        for (int i = 0; i < tiles; i++)
        {
            if (!isRiver[i] || component[i] != 0)
            {
                continue;
            }

            components++;
            int count = 0;
            stack[0] = i;
            int top = 1;
            component[i] = components;
            while (top > 0)
            {
                int at = stack[--top];
                members[count++] = at;
                int x = at % size;
                int y = at / size;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                    {
                        int n = (ny * size) + nx;
                        if (isRiver[n] && component[n] == 0)
                        {
                            component[n] = components;
                            stack[top++] = n;
                        }
                    }
                }
            }

            if (count < MinRiverTiles)
            {
                for (int m = 0; m < count; m++)
                {
                    isRiver[members[m]] = false;
                    component[members[m]] = -1;
                }
            }
        }

        if (components == 0)
        {
            return;
        }

        int[] sizes = new int[components + 1];
        int[] first = new int[components + 1];
        int[] mouth = new int[components + 1];
        int[] highest = new int[components + 1];
        for (int c = 1; c <= components; c++)
        {
            first[c] = tiles;
            mouth[c] = -1;
        }

        for (int i = 0; i < tiles; i++)
        {
            int c = component[i];
            if (c <= 0)
            {
                continue;
            }

            sizes[c]++;
            if (i < first[c])
            {
                first[c] = i;
            }

            if (map.Elevation[i] > highest[c])
            {
                highest[c] = map.Elevation[i];
            }

            if (mouth[c] < 0 && IsShoreTile(map, sea, edgeSea, size, i))
            {
                mouth[c] = i;
            }
        }

        for (int i = 0; i < tiles; i++)
        {
            int c = component[i];
            if (c > 0 && highest[c] < MinSourceElevation)
            {
                isRiver[i] = false;
                component[i] = -1;
            }
        }

        for (int c = 1; c <= components; c++)
        {
            if (highest[c] < MinSourceElevation)
            {
                sizes[c] = 0;
            }
        }

        // Cap the main rivers: when more than MaxMainRivers networks reach the main-river length,
        // the smallest go, largest first by tile count and then by lowest tile. Tributary brooks below
        // the main length are unaffected.
        bool[] dropped = new bool[components + 1];
        int mainLength = (size * 8) / 100;
        while (true)
        {
            int big = 0;
            int smallest = 0;
            for (int c = 1; c <= components; c++)
            {
                if (sizes[c] >= mainLength && !dropped[c])
                {
                    big++;
                    if (smallest == 0 || sizes[c] < sizes[smallest] || (sizes[c] == sizes[smallest] && first[c] > first[smallest]))
                    {
                        smallest = c;
                    }
                }
            }

            if (big <= MaxMainRivers || smallest == 0)
            {
                break;
            }

            dropped[smallest] = true;
        }

        for (int a = 1; a <= components; a++)
        {
            if (sizes[a] == 0 || dropped[a])
            {
                continue;
            }

            for (int b = a + 1; b <= components; b++)
            {
                if (sizes[b] == 0 || dropped[b])
                {
                    continue;
                }

                if (TooClose(size, component, a, b, mouth[a], mouth[b], first[a], first[b]))
                {
                    if (sizes[b] > sizes[a] || (sizes[b] == sizes[a] && first[b] < first[a]))
                    {
                        dropped[a] = true;
                        break;
                    }

                    dropped[b] = true;
                }
            }
        }

        for (int i = 0; i < tiles; i++)
        {
            if (component[i] > 0 && dropped[component[i]])
            {
                isRiver[i] = false;
            }
        }
    }

    // Whether two surviving systems break the spacing rules: any pair of tiles within RiverGap, or
    // mouths closer than MinMouthDistance.
    private static bool TooClose(
        int size,
        int[] component,
        int a,
        int b,
        int mouthA,
        int mouthB,
        int firstA,
        int firstB)
    {
        int anchorA = mouthA >= 0 ? mouthA : firstA;
        int anchorB = mouthB >= 0 ? mouthB : firstB;
        if (Chebyshev(anchorA, anchorB, size) < MinMouthDistance)
        {
            return true;
        }

        return TilesWithin(size, component, a, b, RiverGap);
    }

    private static int Chebyshev(int a, int b, int size) =>
        Math.Max(Math.Abs((a % size) - (b % size)), Math.Abs((a / size) - (b / size)));

    // Whether any tile of system A lies within gap tiles (Chebyshev) of a tile of system B. The scan
    // walks A's tiles and checks the neighbourhood window directly, so it costs proportional to the
    // systems, not the island.
    private static bool TilesWithin(int size, int[] component, int a, int b, int gap)
    {
        int tiles = component.Length;
        for (int i = 0; i < tiles; i++)
        {
            if (component[i] != a)
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            for (int dy = -gap; dy <= gap; dy++)
            {
                for (int dx = -gap; dx <= gap; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if ((uint)nx < (uint)size && (uint)ny < (uint)size && component[(ny * size) + nx] == b)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Whether a land tile touches edge-connected sea among its eight neighbours.
    private static bool IsShoreTile(GeneratedHeightmap map, TerrainId sea, bool[] edgeSea, int size, int tile)
    {
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx < (uint)size && (uint)ny < (uint)size)
            {
                int n = (ny * size) + nx;
                if (edgeSea[n] && map.Terrain[n].Equals(sea))
                {
                    return true;
                }
            }
        }

        return false;
    }

}
