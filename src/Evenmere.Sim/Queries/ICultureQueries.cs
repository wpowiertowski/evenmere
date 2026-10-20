using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the civics (and, from M2, traditions, festivals and great works). Implemented by the culture
/// system (M1-11). Points are Amounts in milli-units (a civic that costs 40 culture has <see cref="Cost"/> 40000). A
/// civic is "done" when it has been adopted.
/// </summary>
public interface ICultureQueries
{
    /// <summary>Gets the civic that receives the day's culture, or <see langword="null"/> when none is being adopted.</summary>
    CivicId? Target { get; }

    /// <summary>Gets the civics that follow the target, in the order they will be adopted. The target itself is not in the queue.</summary>
    ImmutableArray<CivicId> Queue { get; }

    /// <summary>Gets the culture a civic has received so far.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns>The progress in milli-units, from 0 up to <see cref="Cost"/>.</returns>
    Amount Progress(CivicId civic);

    /// <summary>Gets what a civic still costs in all, after any inspiration discount.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns>The cost in milli-units.</returns>
    Amount Cost(CivicId civic);

    /// <summary>Tests whether a civic has been adopted.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if it is done.</returns>
    bool IsDone(CivicId civic);

    /// <summary>Tests whether a civic can be chosen as the target: it is not done, all its prerequisites are, and its era has begun.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if it is available.</returns>
    bool IsAvailable(CivicId civic);

    /// <summary>Gets the culture the previous daily accrual received.</summary>
    /// <returns>The culture per day, in milli-units; 0 before the first accrual.</returns>
    Amount PointsPerDay { get; }

    /// <summary>Tests whether a civic's inspiration has triggered, which discounts its cost.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if the inspiration has triggered.</returns>
    bool IsInspired(CivicId civic);
}
