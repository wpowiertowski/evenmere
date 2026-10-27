using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Repairs a generated island that fails the start guarantee (M2-02c): deterministically tops up the repairable
/// shortfalls — forest belt, stone and clay deposits, grain fertility — inside the hall's road reach, nearest the
/// hall first, until the predicate passes. Coast, rivers, elevation, regions and the hut site are never touched:
/// only paveable land gains forest, deposits are placed, and fertility bits are set. The repair is a pure function
/// of the island and needs no MapDef field to record it (the map contract is frozen): the same seed always repairs
/// to the same island, and the layers show what was added.
/// </summary>
internal static class StartRepair
{
    /// <summary>
    /// Repairs the island in place so <see cref="StartGuarantee.IsSatisfied"/> holds for the hall site: reserves the
    /// grain and pasture placements, places the missing stone and clay deposits, grows the forest belt, raises grain
    /// fertility and verifies the predicate. Nothing that the predicate's unrepairable checks read (coast, hut,
    /// border, span, rivers, elevation) is changed.
    /// </summary>
    /// <param name="map">The finished heightmap; its terrain, deposit and fertility layers are repaired in place.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="site">The hall site the island was generated for; it must already pass the unrepairable checks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A shortfall has no repairable source (no hills, riverbanks, adjacent forest or plains blocks in reach).</exception>
    public static void Repair(GeneratedHeightmap map, ContentDb content, MapStartSite site)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        Footprint hall = content.Get(content.Required.FoundersHall).Footprint;
        MapTerrainFeatures[] features = StartGuarantee.TerrainFeaturesOf(content);
        MapDef wrapped = StartGuarantee.WrapCandidate(map, content, site, features);
        bool[] reach = StartGuarantee.FloodReach(wrapped, content, hall.Width, hall.Height);
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId forest = content.IdOf<TerrainId>("forest");

        // Reserve the grain and pasture blocks first, so forest growth never eats the farmland it promises. The
        // blocks are found in the predicate's greedy order ignoring fertility; fertilizing exactly those blocks is
        // what the predicate then counts.
        List<(int X, int Y)> grainBlocks = StartGuarantee.FindPlacements(
            wrapped, 5, reach, (x, y) => map.Terrain[(y * map.Size) + x].Equals(plains));
        if (grainBlocks.Count < StartGuarantee.MinGrainPlacements)
        {
            throw new InvalidOperationException(
                $"Cannot repair the start: only {grainBlocks.Count} reachable 5x5 plains blocks for grain, need {StartGuarantee.MinGrainPlacements}.");
        }

        List<(int X, int Y)> pastureBlocks = StartGuarantee.FindPlacements(
            wrapped, 4, reach, (x, y) => map.Terrain[(y * map.Size) + x].Equals(plains));
        if (pastureBlocks.Count < StartGuarantee.MinPlainsPlacements)
        {
            throw new InvalidOperationException(
                $"Cannot repair the start: only {pastureBlocks.Count} reachable 4x4 plains blocks for sheep, need {StartGuarantee.MinPlainsPlacements}.");
        }

        // Reserve only the first blocks the predicate will count (same greedy order), so forest growth keeps the
        // maximum room: extra plains blocks stay convertible.
        bool[] reserved = new bool[map.TileCount];
        for (int b = 0; b < StartGuarantee.MinGrainPlacements && b < grainBlocks.Count; b++)
        {
            MarkBlock(reserved, map.Size, grainBlocks[b].X, grainBlocks[b].Y, 5);
        }

        for (int b = 0; b < StartGuarantee.MinPlainsPlacements && b < pastureBlocks.Count; b++)
        {
            MarkBlock(reserved, map.Size, pastureBlocks[b].X, pastureBlocks[b].Y, 4);
        }

        PlaceStone(map, wrapped, reach, hills, hall);
        PlaceClay(map, wrapped, reach, hall);
        GrowForest(map, content, wrapped, reach, reserved, plains, forest, hall);
        RaiseGrain(map, grainBlocks);

        MapDef repaired = StartGuarantee.WrapCandidate(map, content, site, features);
        IReadOnlyList<string> remaining = StartGuarantee.FailingChecks(repaired, content);
        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"The start repair left failing checks: {string.Join(", ", remaining)}.");
        }
    }

    /// <summary>Whether a failing check names a repairable count (forest, grain, pasture, stone or clay).</summary>
    /// <param name="failing">One entry of <see cref="StartGuarantee.FailingChecks"/>.</param>
    /// <returns><see langword="true"/> when repair can address it; coast, hut, border, span and hall need no repair.</returns>
    internal static bool IsRepairable(string failing)
    {
        string kind = failing.Split(' ')[0];
        return kind == "forest" || kind == "grain" || kind == "pasture" || kind == "stone" || kind == "clay";
    }

    private static void MarkBlock(bool[] reserved, int size, int bx, int by, int block)
    {
        for (int dy = 0; dy < block; dy++)
        {
            for (int dx = 0; dx < block; dx++)
            {
                reserved[((by + dy) * size) + bx + dx] = true;
            }
        }
    }

    // Places the missing stone deposits on reachable hills, nearest the hall first: bare hills first, then hills
    // whose deposit would be replaced. Only the deposit layer changes, so nothing else in the predicate moves.
    private static void PlaceStone(GeneratedHeightmap map, MapDef wrapped, bool[] reach, TerrainId hills, Footprint hall)
    {
        byte stone = (byte)((int)DepositKind.Stone + 1);
        int deficit = StartGuarantee.MinStone - StartGuarantee.CountStone(wrapped, hills, reach);
        if (deficit <= 0)
        {
            return;
        }

        List<int> order = ReachOrder(wrapped, reach, hall);
        int placed = 0;
        for (int pass = 0; pass < 2 && placed < deficit; pass++)
        {
            foreach (int tile in order)
            {
                if (placed >= deficit)
                {
                    break;
                }

                if (!map.Terrain[tile].Equals(hills))
                {
                    continue;
                }

                bool bare = map.Deposit[tile] == 0;
                if ((pass == 0) != bare)
                {
                    continue;
                }

                map.Deposit[tile] = stone;
                placed++;
            }
        }

        if (placed < deficit)
        {
            throw new InvalidOperationException(
                $"Cannot repair the start: only {placed} reachable hills for stone, need {deficit}.");
        }
    }

    // Places the missing clay deposits on reachable riverbank tiles, nearest the hall first: bare banks first, then
    // banks whose deposit would be replaced. Only the deposit layer changes; rivers and terrain are untouched.
    private static void PlaceClay(GeneratedHeightmap map, MapDef wrapped, bool[] reach, Footprint hall)
    {
        byte clay = (byte)((int)DepositKind.Clay + 1);
        int deficit = StartGuarantee.MinClay - StartGuarantee.CountClay(wrapped, reach);
        if (deficit <= 0)
        {
            return;
        }

        List<int> order = ReachOrder(wrapped, reach, hall);
        int placed = 0;
        for (int pass = 0; pass < 2 && placed < deficit; pass++)
        {
            foreach (int tile in order)
            {
                if (placed >= deficit)
                {
                    break;
                }

                if (!IsRiverbank(map, tile))
                {
                    continue;
                }

                bool bare = map.Deposit[tile] == 0;
                if ((pass == 0) != bare)
                {
                    continue;
                }

                map.Deposit[tile] = clay;
                placed++;
            }
        }

        if (placed < deficit)
        {
            throw new InvalidOperationException(
                $"Cannot repair the start: only {placed} reachable riverbanks for clay, need {deficit}.");
        }
    }

    // A land tile touching a river along an edge or a diagonal, the same adjacency the predicate counts.
    private static bool IsRiverbank(GeneratedHeightmap map, int tile)
    {
        int size = map.Size;
        int x = tile % size;
        int y = tile / size;
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
                if ((uint)nx < (uint)size && (uint)ny < (uint)size && map.RiverWidth[((ny * size) + nx)] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Grows the forest belt by converting reachable plains next to existing forest, nearest the hall first, until
    // the belt count is met. Reserved farm blocks are never touched; bare plains go before deposited ones. New
    // forest extends the belt for the next pass. Elevation, rivers and coast are untouched.
    // Plants the missing forest belt as compact groves: reachable plains nearest the hall first, never on the
    // hall's footprint, its access tiles or the 1-tile corridor around it, and never on reserved farm blocks.
    // No adjacency to existing forest is required (M2-02c round 3): deforested starts plant new groves outright.
    private static void GrowForest(GeneratedHeightmap map, ContentDb content, MapDef wrapped, bool[] reach, bool[] reserved, TerrainId plains, TerrainId forest, Footprint hall)
    {
        int deficit = StartGuarantee.MinForest - StartGuarantee.CountForest(wrapped, forest, reach);
        if (deficit <= 0)
        {
            return;
        }

        List<int> order = ReachOrder(wrapped, reach, hall);
        int hallX0 = wrapped.StartSite.X - 1;
        int hallY0 = wrapped.StartSite.Y - 1;
        int hallX1 = wrapped.StartSite.X + hall.Width;
        int hallY1 = wrapped.StartSite.Y + hall.Height;
        int converted = 0;
        for (int pass = 0; pass < 2 && converted < deficit; pass++)
        {
            foreach (int tile in order)
            {
                if (converted >= deficit)
                {
                    break;
                }

                int tx = tile % map.Size;
                int ty = tile / map.Size;
                if (tx >= hallX0 && tx <= hallX1 && ty >= hallY0 && ty <= hallY1)
                {
                    continue;
                }

                if (reserved[tile] || !map.Terrain[tile].Equals(plains))
                {
                    continue;
                }

                bool bare = map.Deposit[tile] == 0;
                if ((pass == 0) != bare)
                {
                    continue;
                }

                map.Terrain[tile] = forest;
                converted++;
            }
        }

        if (converted < deficit)
        {
            throw new InvalidOperationException(
                $"Cannot repair the start: only {converted} reachable plains for new forest, need {deficit}.");
        }
    }

    // Raises grain fertility on the reserved 5x5 plains blocks. The blocks were found in the predicate's greedy
    // order ignoring fertility, so fertilizing them is exactly what the predicate then counts: no loop needed.
    private static void RaiseGrain(GeneratedHeightmap map, List<(int X, int Y)> grainBlocks)
    {
        uint grain = 1u << (int)CropKind.Grain;
        for (int b = 0; b < StartGuarantee.MinGrainPlacements && b < grainBlocks.Count; b++)
        {
            (int bx, int by) = grainBlocks[b];
            for (int dy = 0; dy < 5; dy++)
            {
                for (int dx = 0; dx < 5; dx++)
                {
                    map.Fertility[((by + dy) * map.Size) + bx + dx] |= grain;
                }
            }
        }
    }

    // Reachable tiles nearest the hall first: breadth-first visit order from the hall footprint over reach tiles.
    // Deterministic: fixed west, east, north, south neighbour order like the predicate's flood.
    private static List<int> ReachOrder(MapDef wrapped, bool[] reach, Footprint hall)
    {
        var order = new List<int>(wrapped.TileCount);
        bool[] seen = new bool[wrapped.TileCount];
        var queue = new Queue<int>();
        for (int dy = 0; dy < hall.Height; dy++)
        {
            for (int dx = 0; dx < hall.Width; dx++)
            {
                int tile = ((wrapped.StartSite.Y + dy) * wrapped.Width) + wrapped.StartSite.X + dx;
                if (!seen[tile])
                {
                    seen[tile] = true;
                    queue.Enqueue(tile);
                }
            }
        }

        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            order.Add(at);
            int x = at % wrapped.Width;
            int y = at / wrapped.Width;
            TryVisit(wrapped, reach, seen, queue, x - 1, y);
            TryVisit(wrapped, reach, seen, queue, x + 1, y);
            TryVisit(wrapped, reach, seen, queue, x, y - 1);
            TryVisit(wrapped, reach, seen, queue, x, y + 1);
        }

        return order;
    }

    // Queues a neighbour for the reach order when it is road-reachable. Deterministic: fixed order, ascending ties.
    private static void TryVisit(MapDef wrapped, bool[] reach, bool[] seen, Queue<int> queue, int nx, int ny)
    {
        if ((uint)nx >= (uint)wrapped.Width || (uint)ny >= (uint)wrapped.Height)
        {
            return;
        }

        int tile = (ny * wrapped.Width) + nx;
        if (seen[tile] || !reach[tile])
        {
            return;
        }

        seen[tile] = true;
        queue.Enqueue(tile);
    }
}
