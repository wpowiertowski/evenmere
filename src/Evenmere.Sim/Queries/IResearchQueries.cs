using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the knowledge accrual, techs and inspirations. Implemented by the research system (M1-11).
/// Points are Amounts in milli-units (a tech that costs 60 knowledge has <see cref="Cost"/> 60000).
/// </summary>
public interface IResearchQueries
{
    /// <summary>Gets the tech that receives the day's knowledge, or <see langword="null"/> when nothing is being researched.</summary>
    TechId? Target { get; }

    /// <summary>Gets the techs that follow the target, in the order they will be researched. The target itself is not in the queue.</summary>
    ImmutableArray<TechId> Queue { get; }

    /// <summary>Gets the knowledge a tech has received so far.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns>The progress in milli-units, from 0 up to <see cref="Cost"/>.</returns>
    Amount Progress(TechId tech);

    /// <summary>Gets what a tech still costs in all, after any inspiration discount.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns>The cost in milli-units.</returns>
    Amount Cost(TechId tech);

    /// <summary>Tests whether a tech has been researched.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if it is done.</returns>
    bool IsDone(TechId tech);

    /// <summary>Tests whether a tech can be chosen as the target: it is not done, all its prerequisites are, and its era has begun.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if it is available.</returns>
    bool IsAvailable(TechId tech);

    /// <summary>Gets the knowledge the previous daily accrual received.</summary>
    /// <returns>The knowledge per day, in milli-units; 0 before the first accrual. Divide the remaining cost by it for an ETA in days.</returns>
    Amount PointsPerDay { get; }

    /// <summary>Tests whether a tech's inspiration has triggered, which discounts its cost.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if the inspiration has triggered.</returns>
    bool IsInspired(TechId tech);
}
