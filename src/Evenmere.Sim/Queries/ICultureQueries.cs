using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;

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

    /// <summary>
    /// Lists every granted tradition slot with the card it holds, grouped by slot type and then index: one view per
    /// granted slot, in ascending (slot type, index) order. A view holds <see langword="null"/> when its slot is empty.
    /// (M2-07.)
    /// </summary>
    /// <returns>The slot views.</returns>
    ImmutableArray<TraditionSlotView> Slots();

    /// <summary>Lists the tradition cards the island has unlocked, in ascending ID order. (M2-07.)</summary>
    /// <returns>The unlocked traditions.</returns>
    ImmutableArray<TraditionId> UnlockedTraditions();

    /// <summary>
    /// Gets the slotted style card, if one is slotted. Style cards carry the <c>style</c> tag and only one may be
    /// slotted at a time, so this is the island's active architectural style. (M2-07.)
    /// </summary>
    /// <returns>The slotted style card, or <see langword="null"/> when none is slotted.</returns>
    TraditionId? ActiveStyle();

    /// <summary>
    /// Tests whether a festival is running island-wide. While one runs, every house's decline timer is paused.
    /// (M2-08.)
    /// </summary>
    /// <returns><see langword="true"/> while a festival runs.</returns>
    bool IsFestivalRunning();

    /// <summary>
    /// Gets the running festival, if one is running. (M2-08.)
    /// </summary>
    /// <returns>The run with its ground and days left, or <see langword="null"/> when none runs.</returns>
    FestivalRun? ActiveFestival();

    /// <summary>
    /// Counts how often a festival type was held in a year. Each type may be held once per year, so this is 1
    /// when the type was held that year and 0 otherwise. (M2-08.)
    /// </summary>
    /// <param name="festival">The festival.</param>
    /// <param name="year">The calendar year.</param>
    /// <returns>1 when the type was held that year; 0 otherwise.</returns>
    int FestivalsHeldInYear(FestivalId festival, int year);
}
