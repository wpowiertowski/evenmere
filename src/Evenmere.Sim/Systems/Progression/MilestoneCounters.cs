using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The milestone counters that conditions read: monotonic <see cref="long"/>s in dense arrays indexed by content ID (goods,
/// building types) plus the road-tile total. Production, Population, Construction and Roads add to them through
/// <see cref="ICounters"/>; the progression queries and the condition evaluator read them back.
/// </summary>
/// <remarks>
/// <see cref="Version"/> bumps on every <see cref="Add"/>, so the condition evaluator can tell whether a cached answer
/// is still fresh without re-reading every counter it depends on.
/// </remarks>
internal sealed class MilestoneCounters : ICounters
{
    private readonly ProgressionState _state;

    /// <summary>Initializes a new instance of the <see cref="MilestoneCounters"/> class over the progression state.</summary>
    /// <param name="state">The state, already fitted to the content.</param>
    public MilestoneCounters(ProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>Gets how many times any counter was added to since the counters were created. It only ever grows.</summary>
    public long Version { get; private set; }

    /// <inheritdoc />
    public void Add(CounterKey key, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        switch (key.Kind)
        {
            case CounterKind.Produced:
                Increase(_state.Produced, key, amount);
                break;
            case CounterKind.Consumed:
                Increase(_state.Consumed, key, amount);
                break;
            case CounterKind.Built:
                Increase(_state.Built, key, amount);
                break;
            case CounterKind.GreatWorksCreated:
                Increase(_state.GreatWorksCreated, key, amount);
                break;
            case CounterKind.GreatWorksDisplayed:
                Increase(_state.GreatWorksDisplayed, key, amount);
                break;
            case CounterKind.Discovered:
                Increase(_state.Discovered, key, amount);
                break;
            case CounterKind.RegionsRevealed:
                Increase(_state.RegionsRevealed, key, amount);
                break;
            case CounterKind.RoadTilesLaid:
                _state.RoadTilesLaid = checked(_state.RoadTilesLaid + amount);
                break;
            case CounterKind.Expeditions:
                ThrowIfSubject(key);
                _state.Expeditions = checked(_state.Expeditions + amount);
                break;
            case CounterKind.FestivalsHeld:
                ThrowIfSubject(key);
                _state.FestivalsHeld = checked(_state.FestivalsHeld + amount);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Not a counter kind.");
        }

        Version = checked(Version + 1);
    }

    /// <summary>Reads a counter.</summary>
    /// <param name="key">The counter.</param>
    /// <returns>The cumulative count; 0 if nothing has been counted, or if the content has nothing with that ID.</returns>
    public long Get(CounterKey key) => key.Kind switch
    {
        CounterKind.Produced => Read(_state.Produced, key.Subject),
        CounterKind.Consumed => Read(_state.Consumed, key.Subject),
        CounterKind.Built => Read(_state.Built, key.Subject),
        CounterKind.GreatWorksCreated => Read(_state.GreatWorksCreated, key.Subject),
        CounterKind.GreatWorksDisplayed => Read(_state.GreatWorksDisplayed, key.Subject),
        CounterKind.Discovered => Read(_state.Discovered, key.Subject),
        CounterKind.RegionsRevealed => Read(_state.RegionsRevealed, key.Subject),
        CounterKind.RoadTilesLaid => _state.RoadTilesLaid,
        CounterKind.Expeditions => key.Subject == 0 ? _state.Expeditions : 0,
        CounterKind.FestivalsHeld => key.Subject == 0 ? _state.FestivalsHeld : 0,
        _ => 0,
    };

    private static long Read(long[] counters, int subject) => (uint)subject < (uint)counters.Length ? counters[subject] : 0;

    private static void Increase(long[] counters, CounterKey key, long amount)
    {
        if ((uint)key.Subject >= (uint)counters.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "The content has nothing with that ID to count.");
        }

        counters[key.Subject] = checked(counters[key.Subject] + amount);
    }

    // Subject-less counters only count under subject 0; anything else names nothing, like an out-of-range ID.
    private static void ThrowIfSubject(CounterKey key)
    {
        if (key.Subject != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "The content has nothing with that ID to count.");
        }
    }
}
