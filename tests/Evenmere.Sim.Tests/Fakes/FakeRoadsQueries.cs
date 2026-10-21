using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable stand-in for <see cref="IRoadsQueries"/>. Road tiles, road access, storehouse reach costs, service coverage
/// and view paths are all set by the test; nothing is computed from the map.
/// </summary>
internal sealed class FakeRoadsQueries : IRoadsQueries
{
    private readonly Dictionary<TileCoord, BuildingTypeId> _roads = [];
    private readonly HashSet<EntityId> _roadAccess = [];
    private readonly SortedDictionary<(EntityId Storehouse, EntityId Building), int> _costs = [];
    private readonly HashSet<(EntityId House, ServiceKind Service)> _covered = [];
    private readonly HashSet<(int X, int Y, int Width, int Height)> _footprintReach = [];
    private readonly Dictionary<(EntityId Source, EntityId Destination), TileCoord[]> _entityPaths = [];
    private readonly Dictionary<(TileCoord Source, TileCoord Destination), TileCoord[]> _tilePaths = [];

    /// <summary>Puts a road on a tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="type">The road type.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetRoad(int x, int y, BuildingTypeId type)
    {
        _roads[new TileCoord(x, y)] = type;
        return this;
    }

    /// <summary>Gives buildings road access (a road touching the footprint).</summary>
    /// <param name="buildings">The buildings.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries GrantRoadAccess(params EntityId[] buildings)
    {
        _roadAccess.UnionWith(buildings);
        return this;
    }

    /// <summary>Puts a building in a storehouse's reach at a road cost.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <param name="building">The building, which may be the storehouse itself.</param>
    /// <param name="cost">The road cost in cost units (1000 = one dirt tile).</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetReach(EntityId storehouse, EntityId building, int cost)
    {
        _costs[(storehouse, building)] = cost;
        return this;
    }

    /// <summary>Covers a house with a service.</summary>
    /// <param name="house">The house.</param>
    /// <param name="service">The service.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetCovered(EntityId house, ServiceKind service)
    {
        _covered.Add((house, service));
        return this;
    }

    /// <summary>Sets the view path between two buildings.</summary>
    /// <param name="source">The start building.</param>
    /// <param name="destination">The end building.</param>
    /// <param name="path">The route.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetViewPath(EntityId source, EntityId destination, params TileCoord[] path)
    {
        _entityPaths[(source, destination)] = path;
        return this;
    }

    /// <summary>Sets the view path between two road tiles.</summary>
    /// <param name="source">The start tile.</param>
    /// <param name="destination">The end tile.</param>
    /// <param name="path">The route.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetViewPath(TileCoord source, TileCoord destination, params TileCoord[] path)
    {
        _tilePaths[(source, destination)] = path;
        return this;
    }

    /// <summary>Puts an unbuilt footprint in storehouse reach.</summary>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="height">The footprint height in tiles.</param>
    /// <returns>This fake.</returns>
    public FakeRoadsQueries SetFootprintReach(int x, int y, int width, int height)
    {
        _footprintReach.Add((x, y, width, height));
        return this;
    }

    /// <inheritdoc />
    public bool IsFootprintInStorehouseReach(int x, int y, int width, int height) => _footprintReach.Contains((x, y, width, height));

    /// <inheritdoc />
    public BuildingTypeId? RoadAt(int x, int y) => _roads.TryGetValue(new TileCoord(x, y), out BuildingTypeId type) ? type : null;

    /// <inheritdoc />
    public bool HasRoadAccess(EntityId building) => _roadAccess.Contains(building);

    /// <inheritdoc />
    public bool IsInStorehouseReach(EntityId building) => TryNearestStorehouse(building, out _, out _);

    /// <inheritdoc />
    public bool TryNearestStorehouse(EntityId building, out EntityId storehouse, out int cost)
    {
        storehouse = EntityId.None;
        cost = 0;
        bool found = false;

        // The dictionary is sorted by storehouse ID, so the first of equal costs is the lowest ID, as the contract says.
        foreach (((EntityId candidate, EntityId target), int candidateCost) in _costs)
        {
            if (target == building && (!found || candidateCost < cost))
            {
                storehouse = candidate;
                cost = candidateCost;
                found = true;
            }
        }

        return found;
    }

    /// <inheritdoc />
    public bool TryGetStorehouseCost(EntityId storehouse, EntityId building, out int cost) =>
        _costs.TryGetValue((storehouse, building), out cost);

    /// <inheritdoc />
    public bool IsCovered(EntityId house, ServiceKind service) => _covered.Contains((house, service));

    /// <inheritdoc />
    public bool TryFindViewPath(EntityId source, EntityId destination, List<TileCoord> path)
    {
        path.Clear();
        if (!_entityPaths.TryGetValue((source, destination), out TileCoord[]? found))
        {
            return false;
        }

        path.AddRange(found);
        return true;
    }

    /// <inheritdoc />
    public bool TryFindViewPath(TileCoord source, TileCoord destination, List<TileCoord> path)
    {
        path.Clear();
        if (!_tilePaths.TryGetValue((source, destination), out TileCoord[]? found))
        {
            return false;
        }

        path.AddRange(found);
        return true;
    }
}
