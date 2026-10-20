using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>Implements <see cref="IWorkforceQueries"/>. M1-00 declares the methods and M1-07 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class WorkforceQueries : IWorkforceQueries
{
    /// <inheritdoc />
    public int Supply(TierId tier) => throw new NotImplementedException("M1-07");

    /// <inheritdoc />
    public int Demand(TierId tier) => throw new NotImplementedException("M1-07");

    /// <inheritdoc />
    public Ratio FillOf(EntityId building) => throw new NotImplementedException("M1-07");

    /// <inheritdoc />
    public bool IsHighPriority(EntityId building) => throw new NotImplementedException("M1-07");
}
