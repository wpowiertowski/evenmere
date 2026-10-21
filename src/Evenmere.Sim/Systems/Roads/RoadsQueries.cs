using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>Implements <see cref="IRoadsQueries"/> over the road network. Queries never change the world state.</summary>
internal sealed class RoadsQueries(RoadNetwork network) : IRoadsQueries
{
    /// <inheritdoc />
    public BuildingTypeId? RoadAt(int x, int y) => network.RoadAt(x, y);

    /// <inheritdoc />
    public bool HasRoadAccess(EntityId building) => network.HasRoadAccess(building);

    /// <inheritdoc />
    public bool IsInStorehouseReach(EntityId building) => network.IsInStorehouseReach(building);

    /// <inheritdoc />
    public bool IsFootprintInStorehouseReach(int x, int y, int width, int height) =>
        network.IsFootprintInStorehouseReach(x, y, width, height);

    /// <inheritdoc />
    public bool TryNearestStorehouse(EntityId building, out EntityId storehouse, out int cost) =>
        network.TryNearestStorehouse(building, out storehouse, out cost);

    /// <inheritdoc />
    public bool TryGetStorehouseCost(EntityId storehouse, EntityId building, out int cost) =>
        network.TryGetStorehouseCost(storehouse, building, out cost);

    /// <inheritdoc />
    public bool IsCovered(EntityId house, ServiceKind service) => network.IsCovered(house, service);

    /// <inheritdoc />
    public bool TryFindViewPath(EntityId source, EntityId destination, List<TileCoord> path) =>
        network.TryFindViewPath(source, destination, path);

    /// <inheritdoc />
    public bool TryFindViewPath(TileCoord source, TileCoord destination, List<TileCoord> path) =>
        network.TryFindViewPath(source, destination, path);
}
