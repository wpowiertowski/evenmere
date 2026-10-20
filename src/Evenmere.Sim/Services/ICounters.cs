using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>
/// The write side of the milestone counters that conditions read (inspirations, advisors, tutorial steps). A counter only
/// ever grows. Implemented by the progression system (M1-04); the read side is <c>IProgressionQueries.Counter</c>.
/// </summary>
internal interface ICounters
{
    /// <summary>Adds to a counter.</summary>
    /// <param name="key">The counter.</param>
    /// <param name="amount">How much to add, in milli-units for goods counters and in whole units for the others; zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    void Add(CounterKey key, long amount);
}
