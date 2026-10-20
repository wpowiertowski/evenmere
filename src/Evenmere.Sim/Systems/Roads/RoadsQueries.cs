using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>Implements <see cref="IRoadsQueries"/>. M1-00 declares the methods and M1-05 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class RoadsQueries : IRoadsQueries
{
    /// <inheritdoc />
    public BuildingTypeId? RoadAt(int x, int y) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool HasRoadAccess(EntityId building) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool IsInStorehouseReach(EntityId building) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool TryNearestStorehouse(EntityId building, out EntityId storehouse, out int cost) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool TryGetStorehouseCost(EntityId storehouse, EntityId building, out int cost) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool IsCovered(EntityId house, ServiceKind service) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool TryFindViewPath(EntityId source, EntityId destination, List<TileCoord> path) => throw new NotImplementedException("M1-05");

    /// <inheritdoc />
    public bool TryFindViewPath(TileCoord source, TileCoord destination, List<TileCoord> path) => throw new NotImplementedException("M1-05");
}
