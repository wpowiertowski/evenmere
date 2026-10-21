using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>Implements <see cref="ICultureQueries"/> over the culture state. Era gating reads the current era through the
/// late-bound queries, because the roster builds this system before the facade exists.</summary>
internal sealed class CultureQueries : ICultureQueries
{
    private readonly CultureState _state;
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;

    /// <summary>Initializes a new instance of the <see cref="CultureQueries"/> class.</summary>
    /// <param name="state">The culture state to read.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">A late-bound accessor for the query facade.</param>
    public CultureQueries(CultureState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        _state = state;
        _content = content;
        _queries = queries;
    }

    /// <inheritdoc />
    public CivicId? Target => _state.Target;

    /// <inheritdoc />
    public ImmutableArray<CivicId> Queue => [.. _state.Queue];

    /// <inheritdoc />
    public Amount PointsPerDay => _state.PointsPerDay;

    /// <inheritdoc />
    public Amount Progress(CivicId civic) => CultureMath.ProgressOf(_state, _content, civic);

    /// <inheritdoc />
    public Amount Cost(CivicId civic) => CultureMath.EffectiveCost(_state, _content, DiscountPerMille(), civic);

    /// <inheritdoc />
    public bool IsDone(CivicId civic) => CultureMath.IsKnown(_content, civic) && Progression.Bits.Has(_state.Done, civic.Value);

    /// <inheritdoc />
    public bool IsAvailable(CivicId civic) => CultureMath.IsAvailable(_state, _content, EraOrder(), civic);

    /// <inheritdoc />
    public bool IsInspired(CivicId civic) => CultureMath.IsInspired(_state, _content, civic);

    private int EraOrder()
    {
        EraId era = _queries().Progression.CurrentEra;
        return (uint)era.Value < (uint)_content.Eras.Count ? _content.Get(era).Order : 0;
    }

    private int DiscountPerMille() => _content.Rules.InspirationDiscount.PerMille;
}
