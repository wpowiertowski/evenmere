using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>Implements <see cref="ICultureQueries"/>. M1-00 declares the methods and M1-11 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class CultureQueries : ICultureQueries
{
    /// <inheritdoc />
    public CivicId? Target => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public ImmutableArray<CivicId> Queue => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount PointsPerDay => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount Progress(CivicId civic) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public Amount Cost(CivicId civic) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsDone(CivicId civic) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsAvailable(CivicId civic) => throw new NotImplementedException("M1-11");

    /// <inheritdoc />
    public bool IsInspired(CivicId civic) => throw new NotImplementedException("M1-11");
}
