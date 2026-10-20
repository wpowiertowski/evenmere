using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the problem detection and advice. Implemented by the advisors system (M1-15).</summary>
public interface IAdvisorsQueries
{
    /// <summary>
    /// Gets the advice that is showing now: at most 5 notes, ordered by priority and then age, not counting dismissed ones.
    /// The span is a copy made for the view, not the simulation's own records, and it stays valid only until the next tick.
    /// </summary>
    /// <returns>The active advice; empty when there is none.</returns>
    ReadOnlySpan<AdviceView> Active();
}
