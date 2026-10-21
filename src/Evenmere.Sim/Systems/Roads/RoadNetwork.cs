using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The road layer and everything derived from it: access tiles, the distance field of each storehouse and service building,
/// the per-building lookups built on those fields, and view paths. <see cref="RoadsSystem"/> owns one and its command
/// handlers, lifecycle callbacks and queries all go through it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is state and what is not.</b> Only the layer lives in <see cref="RoadsState"/>. The fields and the lookups are
/// rebuilt from the layer, the building registry and the content the first time anything needs them
/// (<see cref="EnsureReady"/>), which is how a loaded save gets its fields without any hook in the load path, and why the
/// state hash does not depend on how the fields were maintained.
/// </para>
/// <para>
/// <b>Cost model.</b> A road tile costs its road type's <c>roadCost</c> to <i>enter</i>. A source's access tiles are the
/// start of its search at cost 0, so a tile that is one dirt road away from an access tile costs 1000, and a storehouse with
/// reach 20 reaches road tiles up to 20 dirt tiles from its door (cost 20000, inclusive).
/// </para>
/// <para>
/// Queries read the map and progression through <c>Func&lt;ISimQueries&gt;</c> because they carry no
/// <see cref="SimContext"/> and the query facade does not exist yet when the roster builds the roads system.
/// </para>
/// </remarks>
internal sealed partial class RoadNetwork
{
    private const int Unreached = int.MaxValue;
    private const int StackRing = 64;

    private readonly RoadsState _state;
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;
    private readonly int[] _roadCost;
    private readonly int _minRoadCost;
    private readonly List<int> _changed = [];

    /// <summary>Initializes a new instance of the <see cref="RoadNetwork"/> class.</summary>
    /// <param name="state">The roads slice of the world state, which this network reads and writes.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">A late-bound accessor for the query facade, called only when a query or command runs.</param>
    /// <exception cref="InvalidDataException">The state's layer does not match its size, which only a damaged save can cause.</exception>
    public RoadNetwork(RoadsState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        if (state.Layer is null || state.Width < 0 || state.Height < 0 || (long)state.Width * state.Height != state.Layer.Length)
        {
            throw new InvalidDataException("The road layer does not match its width and height.");
        }

        _state = state;
        _content = content;
        _queries = queries;

        _roadCost = new int[content.Buildings.Count];
        _minRoadCost = int.MaxValue;
        for (int i = 0; i < _roadCost.Length; i++)
        {
            int cost = content.Buildings.Items[i].RoadCost ?? 0;
            _roadCost[i] = Math.Max(0, cost);
            if (cost > 0)
            {
                _minRoadCost = Math.Min(_minRoadCost, cost);
            }
        }
    }

    /// <summary>Gets the number of distance-field builds since the network was created. Tests use it to see what a change rebuilt.</summary>
    public int BuildCount { get; private set; }

    /// <summary>Tests whether a building type is a road type: it has a positive road cost.</summary>
    /// <param name="type">The building type.</param>
    /// <returns><see langword="true"/> if tiles can be paved with it.</returns>
    public bool IsRoadType(BuildingTypeId type) => type.Value < _roadCost.Length && _roadCost[type.Value] > 0;

    /// <summary>Finds the road on a tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The road type, or <see langword="null"/> when the tile has no road, is off the layer, or its road type no longer exists.</returns>
    public BuildingTypeId? RoadAt(int x, int y)
    {
        if ((uint)x >= (uint)_state.Width || (uint)y >= (uint)_state.Height)
        {
            return null;
        }

        ushort value = _state.Layer[(y * _state.Width) + x];
        return value != 0 && CostOf(value) > 0 ? BuildingTypeId.From((ushort)(value - 1)) : null;
    }

    /// <summary>Tests whether at least one road tile touches the building's footprint along an edge.</summary>
    /// <param name="building">The building or site.</param>
    /// <returns><see langword="true"/> if the building has road access; <see langword="false"/> if it has none or does not exist.</returns>
    public bool HasRoadAccess(EntityId building)
    {
        if (_state.Layer.Length == 0 || !TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        Span<int> ring = RingBuffer(info, stackalloc int[StackRing]);
        return CollectAccess(info, ring) > 0;
    }

    /// <summary>Lists a building's access tiles: the road tiles orthogonally adjacent to its footprint, in row-major order.</summary>
    /// <param name="building">The building or site.</param>
    /// <param name="tiles">Cleared, then filled with the access tiles.</param>
    /// <returns><see langword="true"/> if the building exists.</returns>
    public bool TryGetAccessTiles(EntityId building, List<TileCoord> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        tiles.Clear();
        if (!TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        if (_state.Layer.Length > 0)
        {
            Span<int> ring = RingBuffer(info, stackalloc int[StackRing]);
            int count = CollectAccess(info, ring);
            for (int i = 0; i < count; i++)
            {
                tiles.Add(TileCoord.FromIndex(ring[i], _state.Width));
            }
        }

        return true;
    }

    /// <summary>Tests whether any of the given tiles holds a road. It reads only the layer, so it needs no map.</summary>
    /// <param name="tiles">The tiles.</param>
    /// <returns><see langword="true"/> if some tile is on the layer and has a road.</returns>
    public bool HasAnyRoad(ReadOnlySpan<TileCoord> tiles)
    {
        foreach (TileCoord tile in tiles)
        {
            if (tile.IsInside(_state.Width, _state.Height) && _state.Layer[tile.ToIndex(_state.Width)] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lays a road on tiles that placement validation has already accepted. A tile that already holds a road of another type
    /// is upgraded in place; a tile that already holds this type is left alone. When anything changed it rebuilds the
    /// fields the change can reach, marks the chunks dirty, emits <see cref="RoadsChanged"/> and credits the tiles that
    /// were newly paved to <see cref="CounterKey.RoadTilesLaid"/>.
    /// </summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="tiles">The tiles, all on the map.</param>
    /// <param name="type">The road type.</param>
    /// <returns>The number of tiles that changed.</returns>
    public int PlaceRoads(SimContext ctx, ReadOnlySpan<TileCoord> tiles, BuildingTypeId type)
    {
        EnsureReady();
        EnsureLayer();

        ushort value = checked((ushort)(type.Value + 1));
        ushort[] layer = _state.Layer;
        int width = _state.Width;
        _changed.Clear();
        int laid = 0;
        var box = new Box(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        foreach (TileCoord tile in tiles)
        {
            int index = tile.ToIndex(width);
            ushort before = layer[index];
            if (before == value)
            {
                continue;
            }

            if (before == 0)
            {
                laid++;
            }

            layer[index] = value;
            _changed.Add(index);
            box = box.Include(tile.X, tile.Y);
        }

        if (_changed.Count > 0)
        {
            Publish(ctx, box);
            if (laid > 0)
            {
                ctx.Counters.Add(CounterKey.RoadTilesLaid, laid);
            }
        }

        return _changed.Count;
    }

    /// <summary>Clears the road from tiles. Tiles that are off the layer or have no road are skipped.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="tiles">The tiles, in any order.</param>
    /// <returns>The number of tiles that changed.</returns>
    public int RemoveRoads(SimContext ctx, ReadOnlySpan<TileCoord> tiles)
    {
        if (_state.Layer.Length == 0)
        {
            return 0;
        }

        EnsureReady();

        ushort[] layer = _state.Layer;
        int width = _state.Width;
        _changed.Clear();
        var box = new Box(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        foreach (TileCoord tile in tiles)
        {
            if (!tile.IsInside(width, _state.Height))
            {
                continue;
            }

            int index = tile.ToIndex(width);
            if (layer[index] == 0)
            {
                continue;
            }

            layer[index] = 0;
            _changed.Add(index);
            box = box.Include(tile.X, tile.Y);
        }

        if (_changed.Count > 0)
        {
            Publish(ctx, box);
        }

        return _changed.Count;
    }

    // Everything that follows a change to the layer, in one place so Place and Remove cannot drift apart: rebuild the fields
    // the change can reach, mark the chunks the strips of the changed tiles and their neighbours live in, and tell the view.
    private void Publish(SimContext ctx, Box box)
    {
        InvalidateFields(box);

        int width = _state.Width;
        int height = _state.Height;
        foreach (int index in _changed)
        {
            // A road strip joins its neighbours, so the chunk next door needs a redraw too when a tile sits on a chunk edge.
            int x = index % width;
            int y = index / width;
            ctx.DirtyChunks.MarkRect(Math.Max(0, x - 1), Math.Max(0, y - 1), Math.Min(width - 1, x + 1), Math.Min(height - 1, y + 1));
        }

        ctx.Events.Emit(new RoadsChanged(ctx.Tick, new TileCoord(box.MinX, box.MinY), new TileCoord(box.MaxX, box.MaxY)));
    }

    // Sizes the layer from the map on the first road command. Queries never call this: they must not change state.
    private void EnsureLayer()
    {
        IMapQueries map = _queries().Map;
        int width = map.Width;
        int height = map.Height;
        if (_state.Layer.Length == 0)
        {
            _state.Width = width;
            _state.Height = height;
            _state.Layer = new ushort[checked(width * height)];
        }
        else if (_state.Width != width || _state.Height != height)
        {
            throw new InvalidDataException($"The road layer is {_state.Width} × {_state.Height} but the map is {width} × {height}.");
        }
    }

    private bool TryGetBuilding(EntityId id, out BuildingInfo building)
    {
        if (id.IsNone)
        {
            building = default;
            return false;
        }

        return _queries().Map.TryGetBuilding(id, out building);
    }

    // The road cost of a layer value (a road type's raw ID plus one); 0 when the value is not a road of the loaded content.
    private int CostOf(ushort value)
    {
        int type = value - 1;
        return (uint)type < (uint)_roadCost.Length ? _roadCost[type] : 0;
    }

    private int CostAt(int index) => CostOf(_state.Layer[index]);

    // The most tiles that border a footprint, and a buffer for them: on the stack for the usual small building.
    private static int RingCapacity(in BuildingInfo building) => 2 * (building.Width + building.Height);

    private static Span<int> RingBuffer(in BuildingInfo building, Span<int> stack)
    {
        int capacity = RingCapacity(building);
        return capacity <= stack.Length ? stack : new int[capacity];
    }

    // Writes the indices of the road tiles orthogonally adjacent to the footprint, in row-major order, and returns how many.
    // The row above, then each row of the footprint (the tile to its left, then the tile to its right), then the row below:
    // that order is ascending by index, so the result is sorted without a sort.
    private int CollectAccess(in BuildingInfo building, Span<int> destination)
    {
        int width = _state.Width;
        int height = _state.Height;
        int left = building.Origin.X;
        int top = building.Origin.Y;
        int right = left + building.Width;
        int bottom = top + building.Height;
        int count = 0;

        if (top - 1 >= 0 && top - 1 < height)
        {
            for (int x = Math.Max(left, 0); x < Math.Min(right, width); x++)
            {
                count = AddIfRoad(((top - 1) * width) + x, destination, count);
            }
        }

        for (int y = Math.Max(top, 0); y < Math.Min(bottom, height); y++)
        {
            if (left - 1 >= 0 && left - 1 < width)
            {
                count = AddIfRoad((y * width) + left - 1, destination, count);
            }

            if (right >= 0 && right < width)
            {
                count = AddIfRoad((y * width) + right, destination, count);
            }
        }

        if (bottom >= 0 && bottom < height)
        {
            for (int x = Math.Max(left, 0); x < Math.Min(right, width); x++)
            {
                count = AddIfRoad((bottom * width) + x, destination, count);
            }
        }

        return count;
    }

    private int AddIfRoad(int index, Span<int> destination, int count)
    {
        if (CostAt(index) > 0)
        {
            destination[count++] = index;
        }

        return count;
    }

    // Tests whether a tile borders a footprint along an edge (not a corner), which is what makes a road tile an access tile.
    private static bool BordersFootprint(in BuildingInfo building, int x, int y)
    {
        int left = building.Origin.X;
        int top = building.Origin.Y;
        int right = left + building.Width;
        int bottom = top + building.Height;
        bool besideColumns = x >= left && x < right && (y == top - 1 || y == bottom);
        bool besideRows = y >= top && y < bottom && (x == left - 1 || x == right);
        return besideColumns || besideRows;
    }

    /// <summary>A rectangle of tiles, both corners inclusive; empty while MinX is above MaxX.</summary>
    /// <param name="MinX">The first column.</param>
    /// <param name="MinY">The first row.</param>
    /// <param name="MaxX">The last column.</param>
    /// <param name="MaxY">The last row.</param>
    private readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY)
    {
        public Box Include(int x, int y) => new(Math.Min(MinX, x), Math.Min(MinY, y), Math.Max(MaxX, x), Math.Max(MaxY, y));
    }
}
