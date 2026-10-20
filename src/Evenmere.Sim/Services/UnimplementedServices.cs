using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

// Throwing stand-ins for the write services, so the simulation can be assembled and the other tasks can be written against
// the interfaces before the owning task has landed. Each message is the owning task's ID. The owning task writes the real
// class in its own folder and replaces one line in SystemRoster.

/// <summary>The stand-in for <see cref="IBuildingRegistry"/> until M1-01.</summary>
internal sealed class UnimplementedBuildingRegistry : IBuildingRegistry
{
    /// <inheritdoc />
    public EntityId Add(BuildingTypeId type, int x, int y, Rotation rotation, BuildingState state) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public bool Remove(EntityId building) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public void SetState(EntityId building, BuildingState state) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public void SetType(EntityId building, BuildingTypeId type) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public void SetStage(EntityId building, int stage) => throw new NotImplementedException("M1-01");
}

/// <summary>The stand-in for <see cref="IBuildingLifecycle"/> until M1-01.</summary>
internal sealed class UnimplementedBuildingLifecycle : IBuildingLifecycle
{
    /// <inheritdoc />
    public void Subscribe(IBuildingLifecycleListener listener) => throw new NotImplementedException("M1-01");
}

/// <summary>The stand-in for <see cref="IChunkDirtyTracker"/> until M1-01.</summary>
internal sealed class UnimplementedChunkDirtyTracker : IChunkDirtyTracker
{
    /// <inheritdoc />
    public void MarkTile(int x, int y) => throw new NotImplementedException("M1-01");

    /// <inheritdoc />
    public void MarkRect(int minX, int minY, int maxX, int maxY) => throw new NotImplementedException("M1-01");
}

/// <summary>The stand-in for <see cref="IProgressionService"/> until M1-04.</summary>
internal sealed class UnimplementedProgressionService : IProgressionService
{
    /// <inheritdoc />
    public void ApplyEffects(EffectSource source, ImmutableArray<Effect> effects) => throw new NotImplementedException("M1-04");
}

/// <summary>The stand-in for <see cref="IPointsLedger"/> until M1-04.</summary>
internal sealed class UnimplementedPointsLedger : IPointsLedger
{
    /// <inheritdoc />
    public void Credit(PointKind kind, Amount amount) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public Amount TakeToday(PointKind kind) => throw new NotImplementedException("M1-04");
}

/// <summary>The stand-in for <see cref="ICounters"/> until M1-04.</summary>
internal sealed class UnimplementedCounters : ICounters
{
    /// <inheritdoc />
    public void Add(CounterKey key, long amount) => throw new NotImplementedException("M1-04");
}

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
