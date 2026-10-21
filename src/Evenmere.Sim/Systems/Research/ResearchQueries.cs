using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Research;

/// <summary>Implements <see cref="IResearchQueries"/> over the research state. Era gating reads the current era through the
/// late-bound queries, because the roster builds this system before the facade exists.</summary>
internal sealed class ResearchQueries : IResearchQueries
{
    private readonly ResearchState _state;
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;

    /// <summary>Initializes a new instance of the <see cref="ResearchQueries"/> class.</summary>
    /// <param name="state">The research state to read.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">A late-bound accessor for the query facade.</param>
    public ResearchQueries(ResearchState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        _state = state;
        _content = content;
        _queries = queries;
    }

    /// <inheritdoc />
    public TechId? Target => _state.Target;

    /// <inheritdoc />
    public ImmutableArray<TechId> Queue => [.. _state.Queue];

    /// <inheritdoc />
    public Amount PointsPerDay => _state.PointsPerDay;

    /// <inheritdoc />
    public Amount Progress(TechId tech) => ResearchMath.ProgressOf(_state, _content, tech);

    /// <inheritdoc />
    public Amount Cost(TechId tech) => ResearchMath.EffectiveCost(_state, _content, DiscountPerMille(), tech);

    /// <inheritdoc />
    public bool IsDone(TechId tech) => ResearchMath.IsKnown(_content, tech) && Progression.Bits.Has(_state.Done, tech.Value);

    /// <inheritdoc />
    public bool IsAvailable(TechId tech) => ResearchMath.IsAvailable(_state, _content, EraOrder(), tech);

    /// <inheritdoc />
    public bool IsInspired(TechId tech) => ResearchMath.IsInspired(_state, _content, tech);

    private int EraOrder()
    {
        EraId era = _queries().Progression.CurrentEra;
        return (uint)era.Value < (uint)_content.Eras.Count ? _content.Get(era).Order : 0;
    }

    private int DiscountPerMille() => _content.Rules.InspirationDiscount.PerMille;
}
