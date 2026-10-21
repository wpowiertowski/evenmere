using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The integer arithmetic of construction: how much work a building needs, how fast a site gains it, and how the bill of
/// materials splits into steps. Everything is in whole numbers (ADR-0003) and shared by the system, the handlers and the
/// queries, so a refund always matches what the site drew.
/// </summary>
internal static class SiteMath
{
    /// <summary>The number of equal steps in which a site draws its materials and that its stage counts.</summary>
    public const int Steps = 10;

    /// <summary>The work a site gains per tick at +0% construction speed, so that one tick is a thousand milli-ticks.</summary>
    public const int BaseSpeed = 1000;

    /// <summary>The least work per tick, so a large construction-speed penalty slows a site down to a tenth but never freezes it.</summary>
    public const int MinSpeed = 100;

    /// <summary>Gets the work a building type needs, in milli-ticks: <c>buildDays × 120 × 1000</c>.</summary>
    /// <param name="def">The building type.</param>
    /// <returns>The work; zero for a type that builds instantly.</returns>
    public static long WorkTotal(BuildingDef def) => (long)def.BuildDays * GameTime.TicksPerDay * BaseSpeed;

    /// <summary>Gets the work at which a build step is complete.</summary>
    /// <param name="workTotal">The total work.</param>
    /// <param name="step">The step, 0 to <see cref="Steps"/>; step 0 is the start and step <see cref="Steps"/> is the end.</param>
    /// <returns>The work, rounded down, so the last step always ends exactly at <paramref name="workTotal"/>.</returns>
    public static long StepBoundary(long workTotal, int step) => workTotal * step / Steps;

    /// <summary>Gets the work a site gains per tick under a construction-speed bonus.</summary>
    /// <param name="bonusPercent">The summed <c>constructionSpeed</c> modifiers, in percent (+25 gives 1250).</param>
    /// <returns>Milli-ticks per tick, at least <see cref="MinSpeed"/>.</returns>
    public static int SpeedFor(int bonusPercent) => Math.Max(MinSpeed, Ratio.FromBonusPercent(bonusPercent).PerMille);

    /// <summary>Gets how many ticks a site needs when its speed never changes.</summary>
    /// <param name="workTotal">The total work.</param>
    /// <param name="speed">The work per tick.</param>
    /// <returns>The work divided by the speed, rounded up; at least 1, because even an instant building waits for its first tick.</returns>
    public static long TotalTicks(long workTotal, int speed) => Math.Max(1, (workTotal + speed - 1) / speed);

    /// <summary>Gets the amount of one good that a build step draws.</summary>
    /// <param name="total">The good's amount in the bill.</param>
    /// <param name="step">The step, 1 to <see cref="Steps"/>.</param>
    /// <returns>A tenth of the total, rounded down, for steps 1 to 9; the last step draws the rest, so the steps add up to the total exactly.</returns>
    public static Amount StepAmount(Amount total, int step)
    {
        long tenth = total.Milli / Steps;
        return Amount.FromMilli(step >= Steps ? total.Milli - (tenth * (Steps - 1)) : tenth);
    }

    /// <summary>Gets how much of one good a site has drawn after a number of steps.</summary>
    /// <param name="total">The good's amount in the bill.</param>
    /// <param name="stepsDrawn">The steps taken, 0 to <see cref="Steps"/>.</param>
    /// <returns>The sum of <see cref="StepAmount"/> over those steps.</returns>
    public static Amount DrawnAfter(Amount total, int stepsDrawn)
    {
        long tenth = total.Milli / Steps;
        return Amount.FromMilli(stepsDrawn >= Steps ? total.Milli : tenth * stepsDrawn);
    }

    /// <summary>Gets the share of a bill that is refunded when an active or paused building is demolished.</summary>
    /// <param name="total">The good's amount in the bill.</param>
    /// <returns>Half, rounded down to the milli-unit.</returns>
    public static Amount DemolishRefund(Amount total) => total.MulDiv(1, 2);

    /// <summary>Tests whether a building type is the starting hall, which can never be demolished: it is <c>unique</c> and available from the <c>start</c>.</summary>
    /// <param name="def">The building type.</param>
    /// <returns><see langword="true"/> if demolishing it is refused.</returns>
    public static bool IsProtected(BuildingDef def) => def.Unique && def.Start;
}
