using System.Diagnostics.CodeAnalysis;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

// Distance fields: which buildings have one, building them with a multi-source Dijkstra, and keeping them current as roads
// and buildings change.
internal sealed partial class RoadNetwork
{
    // The fields in ascending order of source ID, one per storehouse or service building.
    private readonly List<DistanceField> _fields = [];

    private bool _ready;

    // Bumped whenever the set of fields or the contents of any field change. The per-building lookups remember the epoch they
    // were computed in and are recomputed when it has moved on. It starts at 0 and EnsureReady makes it at least 1, so a
    // lookup that was never computed (epoch 0) is never taken for a current one.
    private int _epoch;

    // Dijkstra and A* scratch, sized to the layer and reused so a rebuild allocates only the field it produces. Every tile
    // starts Unreached; whoever uses the scratch resets exactly the tiles it touched.
    private int[] _dist = [];
    private int[] _touched = [];
    private int _touchedCount;
    private long[] _heap = [];
    private int _heapCount;

    /// <summary>Gets the number of buildings that currently have a distance field.</summary>
    public int FieldCount
    {
        get
        {
            EnsureReady();
            return _fields.Count;
        }
    }

    /// <summary>
    /// Finds the revision of a source's field: the number of the build that last computed it. A field was not rebuilt between
    /// two moments exactly when its revision is the same at both.
    /// </summary>
    /// <param name="source">The source building.</param>
    /// <returns>The revision, or -1 when the building has no field.</returns>
    public int RevisionOf(EntityId source)
    {
        EnsureReady();
        int index = FindField(source);
        return index < 0 ? -1 : _fields[index].Revision;
    }

    /// <summary>Reads the contents of a source's field, for tests.</summary>
    /// <param name="source">The source building.</param>
    /// <param name="tiles">The tile indices in the field, ascending.</param>
    /// <param name="costs">The cost to each of those tiles, in cost units.</param>
    /// <returns><see langword="true"/> if the building has a field.</returns>
    public bool TryGetFieldEntries(EntityId source, out int[] tiles, out int[] costs)
    {
        EnsureReady();
        int index = FindField(source);
        if (index < 0)
        {
            tiles = [];
            costs = [];
            return false;
        }

        tiles = _fields[index].Tiles;
        costs = _fields[index].Costs;
        return true;
    }

    /// <summary>
    /// Builds every field from the layer and the building registry, if that has not been done yet. This is what makes a
    /// restored save work: the layer arrives with the state, the registry arrives with the map, and the fields, which are
    /// derived from the two, are built here on first use. A failure leaves the network not ready, so the next call tries again.
    /// </summary>
    public void EnsureReady()
    {
        if (_ready)
        {
            return;
        }

        var built = new List<DistanceField>();
        foreach (BuildingInfo building in _queries().Map.ActiveBuildings)
        {
            if (TryCreateField(building, out DistanceField? field))
            {
                BuildField(field);
                built.Add(field);
            }
        }

        _fields.Clear();
        _fields.AddRange(built);
        _epoch++;
        _ready = true;
    }

    /// <summary>
    /// Starts, replaces or stops tracking a building as a field source after it became active or changed type. A building
    /// with neither a storage nor a service spec ends up with no field.
    /// </summary>
    /// <param name="building">The building as it now stands.</param>
    public void SetSource(in BuildingInfo building)
    {
        EnsureReady();
        bool changed = RemoveField(building.Id);
        if (TryCreateField(building, out DistanceField? field))
        {
            BuildField(field);
            _fields.Insert(~FindField(building.Id), field);
            changed = true;
        }

        if (changed)
        {
            _epoch++;
            _reachStale = true;
        }
    }

    /// <summary>Stops tracking a building that was removed, and forgets what was derived for it.</summary>
    /// <param name="building">The building as it stood before removal.</param>
    public void RemoveSource(in BuildingInfo building)
    {
        EnsureReady();
        if (RemoveField(building.Id))
        {
            _epoch++;
            _reachStale = true;
        }

        ForgetDerived(building.Id);
    }

    // The index of a source's field, or the bitwise complement of where it would go.
    private int FindField(EntityId source)
    {
        int low = 0;
        int high = _fields.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _fields[middle].Source.Value.CompareTo(source.Value);
            if (order == 0)
            {
                return middle;
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private bool RemoveField(EntityId source)
    {
        int index = FindField(source);
        if (index < 0)
        {
            return false;
        }

        _fields.RemoveAt(index);
        return true;
    }

    // A building is a source when it is finished and its type has a storage or a service spec. Both bounds are in cost units:
    // a reach or coverage counts dirt tiles, and a dirt tile costs 1000.
    private bool TryCreateField(in BuildingInfo building, [NotNullWhen(true)] out DistanceField? field)
    {
        field = null;
        if (!building.IsComplete || building.Type.Value >= _content.Buildings.Count)
        {
            return false;
        }

        BuildingDef def = _content.Get(building.Type);
        int storageBound = def.Storage is { } storage ? checked(storage.Reach * 1000) : 0;
        int serviceBound = def.Service is { } service ? checked(service.Coverage * 1000) : 0;
        if (storageBound <= 0 && serviceBound <= 0)
        {
            return false;
        }

        field = new DistanceField(building, Math.Max(storageBound, 0), Math.Max(serviceBound, 0), def.Service?.Kind ?? default);
        return true;
    }

    // Rebuilds the fields that a change to the tiles in _changed can alter, and no others.
    //
    // A field is the set of road tiles within the bound of its source's access tiles, with their costs. A change to tile t can
    // alter it only if (a) t is in the field, so a path through it is lost or repriced; (b) a neighbour of t is in the field, so
    // a new or cheaper t can extend it; or (c) t borders the source's footprint, so t becomes or stops being an access tile.
    // Any other new path through t must enter it from a tile that was already reached, which is case (b); any other lost path
    // runs through t with a cost within the bound, which is case (a). The extent box is only a cheap first test: it holds the
    // field, its footprint and a one-tile margin, so a change outside it cannot touch the field at all.
    private void InvalidateFields(Box changes)
    {
        bool rebuilt = false;
        foreach (DistanceField field in _fields)
        {
            if (changes.MaxX < field.ExtentMinX || changes.MinX > field.ExtentMaxX
                || changes.MaxY < field.ExtentMinY || changes.MinY > field.ExtentMaxY
                || !IsAffected(field))
            {
                continue;
            }

            BuildField(field);
            rebuilt = true;
        }

        if (rebuilt)
        {
            _epoch++;
            _reachStale = true;
        }
    }

    private bool IsAffected(DistanceField field)
    {
        int width = _state.Width;
        int height = _state.Height;
        foreach (int index in _changed)
        {
            int x = index % width;
            int y = index / width;
            if (field.Contains(index)
                || (x > 0 && field.Contains(index - 1))
                || (x < width - 1 && field.Contains(index + 1))
                || (y > 0 && field.Contains(index - width))
                || (y < height - 1 && field.Contains(index + width))
                || BordersFootprint(field.Building, x, y))
            {
                return true;
            }
        }

        return false;
    }

    // Computes a field from scratch and stores it in the field object.
    private void BuildField(DistanceField field)
    {
        BuildCount++;
        BuildingInfo building = field.Building;
        int[] tiles = [];
        int[] costs = [];

        if (_state.Layer.Length > 0)
        {
            Span<int> seeds = RingBuffer(building, stackalloc int[StackRing]);
            int seedCount = CollectAccess(building, seeds);
            if (seedCount > 0)
            {
                EnsureScratch();
                RunDijkstra(seeds[..seedCount], field.Bound);

                // The search settles tiles in order of cost; a field is stored in order of tile index.
                tiles = new int[_touchedCount];
                Array.Copy(_touched, tiles, _touchedCount);
                Array.Sort(tiles);
                costs = new int[tiles.Length];
                for (int i = 0; i < tiles.Length; i++)
                {
                    costs[i] = _dist[tiles[i]];
                }

                ResetScratch();
            }
        }

        // The extent holds the footprint with its ring of access tiles, every tile of the field, and a one-tile margin.
        int minX = building.Origin.X - 2;
        int minY = building.Origin.Y - 2;
        int maxX = building.Origin.X + building.Width + 1;
        int maxY = building.Origin.Y + building.Height + 1;
        foreach (int tile in tiles)
        {
            int x = tile % _state.Width;
            int y = tile / _state.Width;
            minX = Math.Min(minX, x - 1);
            minY = Math.Min(minY, y - 1);
            maxX = Math.Max(maxX, x + 1);
            maxY = Math.Max(maxY, y + 1);
        }

        field.Assign(tiles, costs, BuildCount, minX, minY, maxX, maxY);
    }

    private void EnsureScratch()
    {
        int tiles = _state.Layer.Length;
        if (_dist.Length != tiles)
        {
            _dist = new int[tiles];
            Array.Fill(_dist, Unreached);
            _touched = new int[tiles];
        }
    }

    private void ResetScratch()
    {
        for (int i = 0; i < _touchedCount; i++)
        {
            _dist[_touched[i]] = Unreached;
        }

        _touchedCount = 0;
    }

    // Multi-source Dijkstra over road tiles. The seeds start at cost 0 and entering a tile costs that tile's road cost; tiles
    // beyond the bound are never entered. The queue orders by cost and then by tile index, so the order of work does not
    // depend on anything but the layer. Afterwards _touched lists every tile reached and _dist holds its cost.
    private void RunDijkstra(ReadOnlySpan<int> seeds, int bound)
    {
        _touchedCount = 0;
        _heapCount = 0;
        foreach (int seed in seeds)
        {
            _dist[seed] = 0;
            _touched[_touchedCount++] = seed;
            HeapPush(seed);
        }

        int width = _state.Width;
        int tiles = _state.Layer.Length;
        while (_heapCount > 0)
        {
            long key = HeapPop();
            int cost = (int)(key >> 32);
            int index = (int)(key & uint.MaxValue);
            if (cost != _dist[index])
            {
                // A cheaper route to this tile was found after this entry was queued.
                continue;
            }

            int x = index % width;
            if (index >= width)
            {
                Relax(index - width, cost, bound);
            }

            if (x > 0)
            {
                Relax(index - 1, cost, bound);
            }

            if (x < width - 1)
            {
                Relax(index + 1, cost, bound);
            }

            if (index + width < tiles)
            {
                Relax(index + width, cost, bound);
            }
        }
    }

    private void Relax(int tile, int cost, int bound)
    {
        int step = CostAt(tile);
        if (step == 0)
        {
            return;
        }

        int next = cost + step;
        int known = _dist[tile];
        if (next > bound || next >= known)
        {
            return;
        }

        if (known == Unreached)
        {
            _touched[_touchedCount++] = tile;
        }

        _dist[tile] = next;
        HeapPush(((long)next << 32) | (uint)tile);
    }

    // A binary min-heap of packed (priority, tile index) keys. Comparing the packed keys orders by priority and then by tile
    // index, which is the deterministic tie-break.
    private void HeapPush(long key)
    {
        if (_heapCount == _heap.Length)
        {
            Array.Resize(ref _heap, Math.Max(64, _heap.Length * 2));
        }

        int child = _heapCount++;
        while (child > 0)
        {
            int parent = (child - 1) >> 1;
            if (_heap[parent] <= key)
            {
                break;
            }

            _heap[child] = _heap[parent];
            child = parent;
        }

        _heap[child] = key;
    }

    private long HeapPop()
    {
        long top = _heap[0];
        long last = _heap[--_heapCount];
        if (_heapCount > 0)
        {
            int parent = 0;
            while (true)
            {
                int child = (parent * 2) + 1;
                if (child >= _heapCount)
                {
                    break;
                }

                if (child + 1 < _heapCount && _heap[child + 1] < _heap[child])
                {
                    child++;
                }

                if (last <= _heap[child])
                {
                    break;
                }

                _heap[parent] = _heap[child];
                parent = child;
            }

            _heap[parent] = last;
        }

        return top;
    }
}
