using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

// Throwing stand-ins for the write services, so the simulation can be assembled and the other tasks can be written against
// the interfaces before the owning task has landed. Each message is the owning task's ID. The owning task writes the real
// class in its own folder and replaces one line in SystemRoster.

/// <summary>The stand-in for <see cref="IProductionBuffers"/> until M1-08.</summary>
internal sealed class UnimplementedProductionBuffers : IProductionBuffers
{
    /// <inheritdoc />
    public void ListRequests(List<TransportRequest> requests) => throw new NotImplementedException("M1-08");

    /// <inheritdoc />
    public bool TryTakeOutput(EntityId building, GoodId good, Amount amount) => throw new NotImplementedException("M1-08");

    /// <inheritdoc />
    public void AddInput(EntityId building, GoodId good, Amount amount) => throw new NotImplementedException("M1-08");
}

/// <summary>The stand-in for <see cref="IWorkforceSupply"/> until M1-07.</summary>
internal sealed class UnimplementedWorkforceSupply : IWorkforceSupply
{
    /// <inheritdoc />
    public void SetSupply(TierId tier, int workers) => throw new NotImplementedException("M1-07");
}

/// <summary>The stand-in for <see cref="IBuildingPlacement"/> until M1-06.</summary>
internal sealed class UnimplementedBuildingPlacement : IBuildingPlacement
{
    /// <inheritdoc />
    public PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation) => throw new NotImplementedException("M1-06");
}

/// <summary>The stand-in for <see cref="IRoadPlacement"/> until M1-05.</summary>
internal sealed class UnimplementedRoadPlacement : IRoadPlacement
{
    /// <inheritdoc />
    public PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType) => throw new NotImplementedException("M1-05");
}
