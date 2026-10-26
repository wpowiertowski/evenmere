using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

// The per-epoch tile-level storehouse-reach labels: for every road tile, the minimum over all storehouses of
// (road cost from the storehouse's access tiles minus the storehouse's reach bound). A tile is in some storehouse's
// reach exactly when its label is at most zero, which is exact even when reach bounds differ: a storehouse claims a
// tile exactly when its own cost is within its own bound, and the label keeps the smallest claim. Keeping the minimum
// (rather than which storehouse wins) is enough for the boolean reach question, so the labels are one int per tile.
//
// The labels answer the same question as scanning every storehouse field for each building
// (<see cref="RoadNetwork.TryNearestStorehouse"/>), but in one multi-source Dijkstra per roads change instead of one
// scan per (building, storehouse) pair. The boolean <see cref="RoadNetwork.IsInStorehouseReach"/> reads the labels;
// the which-and-how-far queries keep the field scan, with its exact current semantics including the tie-break.
// Like the fields, the labels are a pure function of the layer, the building registry and the content: they are
// rebuilt lazily on first use after a storehouse or road change, never saved, and never reach the state hash.
internal sealed partial class RoadNetwork
{
    // The label per tile index: the minimum (cost from that tile to a storehouse's access tiles, minus that
    // storehouse's bound), or Unreached when no storehouse claims the tile. Labels at most zero are in reach.
    private int[] _reach = [];

    // Dial's propagation scratch, reused across builds: the node pool (one node per enqueue, so re-queuing a tile
    // never corrupts the chains) and one chain head per cost residue. Only the used count resets per build.
    private int[] _dialTile = [];
    private int[] _dialCost = [];
    private int[] _dialNext = [];
    private int _dialUsed;
    private int[] _dialHead = [];

    // Whether the labels must be rebuilt: set whenever a storehouse appears, disappears or changes type, or a road
    // tile changes, and cleared by the build. The fields' epoch bump on first build does not set this: materialising
    // the fields changes none of the labels' inputs (layer, registry, content), so labels built before the first
    // field build stay valid and the day's first reach query does not pay for a second build.
    private bool _reachStale = true;

    /// <summary>Tests whether some storehouse's reach covers the building. A storehouse is in its own reach.</summary>
    /// <param name="building">The building or site.</param>
    /// <returns><see langword="true"/> if a storehouse's reach covers the building, read from the per-epoch reach labels.</returns>
    public bool IsInStorehouseReach(EntityId building)
    {
        if (building.IsNone)
        {
            return false;
        }

        EnsureReachLabels();
        if (!TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        // A storehouse is in its own reach even with no road beside it, exactly as the field scan's self rule.
        int field = FindField(building);
        if (field >= 0 && _fields[field].StorageBound > 0)
        {
            return true;
        }

        return IsAnyAccessInReach(info);
    }

    // Whether any road tile touching the footprint is labelled in reach, stopping at the first one. This mirrors
    // CollectAccess's walk (same tiles, same order) but skips the buffer and the full scan: almost every building
    // with road access sits beside a reached tile, so the answer is usually found within a few tiles.
    private bool IsAnyAccessInReach(in BuildingInfo building)
    {
        int width = _state.Width;
        int height = _state.Height;
        int left = building.Origin.X;
        int top = building.Origin.Y;
        int right = left + building.Width;
        int bottom = top + building.Height;

        if (top - 1 >= 0 && top - 1 < height)
        {
            for (int x = Math.Max(left, 0); x < Math.Min(right, width); x++)
            {
                int tile = ((top - 1) * width) + x;
                if (CostAt(tile) > 0 && _reach[tile] <= 0)
                {
                    return true;
                }
            }
        }

        for (int y = Math.Max(top, 0); y < Math.Min(bottom, height); y++)
        {
            if (left - 1 >= 0 && left - 1 < width)
            {
                int tile = (y * width) + left - 1;
                if (CostAt(tile) > 0 && _reach[tile] <= 0)
                {
                    return true;
                }
            }

            if (right >= 0 && right < width)
            {
                int tile = (y * width) + right;
                if (CostAt(tile) > 0 && _reach[tile] <= 0)
                {
                    return true;
                }
            }
        }

        if (bottom >= 0 && bottom < height)
        {
            for (int x = Math.Max(left, 0); x < Math.Min(right, width); x++)
            {
                int tile = (bottom * width) + x;
                if (CostAt(tile) > 0 && _reach[tile] <= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Rebuilds the labels when a storehouse or a road tile changed since the last build. The labels do not need
    // the fields themselves, only the same source set, so this never triggers a field build.
    private void EnsureReachLabels()
    {
        if (!_reachStale && _reach.Length == _state.Layer.Length)
        {
            return;
        }

        BuildReachLabels();
        _reachStale = false;
    }

    // One multi-source Dijkstra from every storehouse's access tiles. Each seed starts at minus its storehouse's
    // bound, entering a tile adds that tile's road cost, and any entry above zero is pruned: only tiles some
    // storehouse reaches can lead anywhere, because every further step only adds cost. The queue is Dial's circular
    // buckets (one chain head per cost residue modulo the largest road cost plus one): road costs are small integers,
    // so settling is amortised O(1) per tile instead of a heap's O(log n). Levels advance one by one and every level
    // is settled before the next, exactly like the heap order, although the labels themselves are order-independent:
    // each tile keeps the minimum over all storehouses regardless of processing order.
    private void BuildReachLabels()
    {
        int tiles = _state.Layer.Length;
        if (_reach.Length != tiles)
        {
            _reach = new int[tiles];
            _dialTile = new int[tiles];
            _dialCost = new int[tiles];
            _dialNext = new int[tiles];
        }

        int residues = _maxRoadCost + 1;
        if (_dialHead.Length != residues)
        {
            _dialHead = new int[residues];
        }

        Array.Fill(_reach, Unreached);
        Array.Fill(_dialHead, -1);
        _dialUsed = 0;

        int lowest = 0;
        Span<int> stack = stackalloc int[StackRing];
        foreach (BuildingInfo building in _queries().Map.ActiveBuildings)
        {
            // The storage half of TryCreateField, inlined so the scan allocates nothing: the bound formula must
            // stay the same as the field's (storage reach in tiles times 1000 cost units per dirt tile).
            if (!building.IsComplete || building.Type.Value >= _content.Buildings.Count)
            {
                continue;
            }

            BuildingDef def = _content.Get(building.Type);
            int bound = def.Storage is { } storage ? checked(storage.Reach * 1000) : 0;
            if (bound <= 0)
            {
                continue;
            }

            Span<int> ring = RingBuffer(building, stack);
            int count = CollectAccess(building, ring);
            int seed = -bound;
            int seedResidue = Residue(seed, residues);
            for (int i = 0; i < count; i++)
            {
                if (seed < _reach[ring[i]])
                {
                    _reach[ring[i]] = seed;
                    DialPush(ring[i], seed, seedResidue);
                    lowest = Math.Min(lowest, seed);
                }
            }
        }

        int width = _state.Width;
        int current = lowest;
        int bucket = Residue(lowest, residues);
        int done = 0;
        while (done < _dialUsed)
        {
            int node = _dialHead[bucket];
            while (node != -1 && _dialCost[node] != _reach[_dialTile[node]])
            {
                // A cheaper claim to this tile was queued after this node; the node is stale.
                node = _dialNext[node];
                done++;
            }

            _dialHead[bucket] = node;
            if (node == -1)
            {
                current++;
                bucket++;
                if (bucket >= residues)
                {
                    bucket = 0;
                }

                continue;
            }

            _dialHead[bucket] = _dialNext[node];
            done++;
            int tile = _dialTile[node];
            int cost = _dialCost[node];
            int x = tile % width;
            if (tile >= width)
            {
                RelaxReach(tile - width, cost, bucket);
            }

            if (x > 0)
            {
                RelaxReach(tile - 1, cost, bucket);
            }

            if (x < width - 1)
            {
                RelaxReach(tile + 1, cost, bucket);
            }

            if (tile + width < tiles)
            {
                RelaxReach(tile + width, cost, bucket);
            }
        }
    }

    // Enqueues one claim as a fresh pool node, so re-queuing a tile never corrupts the bucket chains: the older node
    // for the same tile simply goes stale and is dropped when popped. The residue is passed in rather than
    // recomputed: relaxations derive it from the settled tile's residue plus the step, which needs at most one
    // subtraction because no road tile costs more than the residue count minus one.
    private void DialPush(int tile, int cost, int residue)
    {
        if (_dialUsed == _dialTile.Length)
        {
            int grown = Math.Max(4096, _dialTile.Length * 2);
            Array.Resize(ref _dialTile, grown);
            Array.Resize(ref _dialCost, grown);
            Array.Resize(ref _dialNext, grown);
        }

        _dialTile[_dialUsed] = tile;
        _dialCost[_dialUsed] = cost;
        _dialNext[_dialUsed] = _dialHead[residue];
        _dialHead[residue] = _dialUsed;
        _dialUsed++;
    }

    // The non-negative remainder of a cost modulo the residue count, for the rare seeds whose residue is not
    // already known; relaxations and level advances track residues incrementally and never divide.
    private static int Residue(int cost, int residues)
    {
        int residue = cost % residues;
        return residue < 0 ? residue + residues : residue;
    }

    private void RelaxReach(int tile, int cost, int residue)
    {
        int step = CostAt(tile);
        if (step == 0)
        {
            return;
        }

        int next = cost + step;
        if (next > 0 || next >= _reach[tile])
        {
            return;
        }

        int nextResidue = residue + step;
        if (nextResidue >= _dialHead.Length)
        {
            nextResidue -= _dialHead.Length;
        }

        _reach[tile] = next;
        DialPush(tile, next, nextResidue);
    }
}
