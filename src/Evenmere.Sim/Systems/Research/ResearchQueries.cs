using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Research;

/// <summary>Implements <see cref="IResearchQueries"/>. M1-00 declares the methods and M1-11 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class ResearchQueries : IResearchQueries
{
    /// <inheritdoc />
    public TechId? Target => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public ImmutableArray<TechId> Queue => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount PointsPerDay => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount Progress(TechId tech) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount Cost(TechId tech) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsDone(TechId tech) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsAvailable(TechId tech) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsInspired(TechId tech) => throw new NotImplementedException("M1-11");
}
