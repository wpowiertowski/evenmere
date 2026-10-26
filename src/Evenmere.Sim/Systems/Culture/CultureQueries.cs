using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;

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

    /// <inheritdoc />
    ImmutableArray<TraditionSlotView> ICultureQueries.Slots()
    {
        IProgressionQueries progression = _queries().Progression;
        ImmutableArray<TraditionSlotView>.Builder builder = ImmutableArray.CreateBuilder<TraditionSlotView>();
        foreach (TraditionSlotType slot in Enum.GetValues<TraditionSlotType>())
        {
            int count = progression.TraditionSlotCount(slot);
            for (int index = 0; index < count; index++)
            {
                builder.Add(new TraditionSlotView(slot, index, TraditionSlots.Find(_state.Slotted, slot, index)));
            }
        }

        return builder.ToImmutable();
    }

    /// <inheritdoc />
    ImmutableArray<TraditionId> ICultureQueries.UnlockedTraditions()
    {
        IProgressionQueries progression = _queries().Progression;
        ImmutableArray<TraditionId>.Builder builder = ImmutableArray.CreateBuilder<TraditionId>();
        for (int i = 0; i < _content.Traditions.Count; i++)
        {
            var tradition = TraditionId.From(checked((ushort)i));
            if (progression.IsTraditionUnlocked(tradition))
            {
                builder.Add(tradition);
            }
        }

        return builder.ToImmutable();
    }

    /// <inheritdoc />
    TraditionId? ICultureQueries.ActiveStyle()
    {
        foreach (SlottedTradition entry in _state.Slotted)
        {
            if ((uint)entry.Tradition.Value < (uint)_content.Traditions.Count
                && HasTag(_content.Get(entry.Tradition).Tags, TraditionSlots.StyleTag))
            {
                return entry.Tradition;
            }
        }

        return null;
    }

    /// <inheritdoc />
    bool ICultureQueries.IsFestivalRunning() => _state.FestivalRunning;

    /// <inheritdoc />
    FestivalRun? ICultureQueries.ActiveFestival() => _state.Active;

    /// <inheritdoc />
    int ICultureQueries.FestivalsHeldInYear(FestivalId festival, int year) =>
        FestivalScheduler.HeldInYear(_state.FestivalLastHeldYear, festival, year) ? 1 : 0;

    private static bool HasTag(ImmutableArray<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private int EraOrder()
    {
        EraId era = _queries().Progression.CurrentEra;
        return (uint)era.Value < (uint)_content.Eras.Count ? _content.Get(era).Order : 0;
    }

    private int DiscountPerMille() => _content.Rules.InspirationDiscount.PerMille;
}
