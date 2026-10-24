namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// One tier's workforce after stepping down (ADR-0016), in whole workers. The HUD, the build menu and the
/// inspector all read through this, so the three surfaces agree. It is pure and Godot-free, so the game tests
/// cover it directly. Supply and demand are live, while the substitution counts come from the last daily
/// allocation, so they can disagree briefly within a day; everything clamps at 0.
/// </summary>
public readonly record struct WorkforceSummary(
    int Supply,
    int Demand,
    int OwnFilled,
    int Covered,
    int Shortfall,
    int Free,
    int Below)
{
    /// <summary>
    /// Builds the summary for one tier.
    /// <para>
    /// <c>ownFilled</c> is the tier's own workers staffing its own jobs (<c>min(supply, demand)</c>);
    /// <c>covered</c> is the jobs filled from above; <c>short</c> is the real shortage after stepping down;
    /// <c>free</c> is the tier's own workers still idle; <c>below</c> is the tier's workers in lower-tier jobs.
    /// </para>
    /// </summary>
    /// <param name="supply">The tier's live workforce supply.</param>
    /// <param name="demand">The tier's live workforce demand.</param>
    /// <param name="workingBelow">The tier's workers in lower-tier jobs at the last daily allocation.</param>
    /// <param name="filledFromAbove">The tier's jobs filled from above at the last daily allocation.</param>
    /// <returns>The summary, clamped at 0.</returns>
    public static WorkforceSummary From(int supply, int demand, int workingBelow, int filledFromAbove)
    {
        supply = Math.Max(0, supply);
        demand = Math.Max(0, demand);
        int below = Math.Max(0, workingBelow);
        int covered = Math.Max(0, filledFromAbove);
        int ownFilled = Math.Min(supply, demand);
        int shortfall = Math.Max(0, demand - ownFilled - covered);
        int free = Math.Max(0, supply - ownFilled - below);
        return new WorkforceSummary(supply, demand, ownFilled, covered, shortfall, free, below);
    }

    /// <summary>
    /// Recovers how many residents of a house work below their tier from its morale, exactly the way
    /// <c>ExplainService</c> does: morale is <c>1000 × (residents − underemployed) ÷ residents</c> floored,
    /// so inverting it with the same flooring recovers the count.
    /// </summary>
    /// <param name="residents">The house's residents.</param>
    /// <param name="moralePerMille">The house's morale in per-mille (1000 is full).</param>
    /// <returns>The residents working below their tier; 0 at full morale or with no residents.</returns>
    public static int UnderemployedCount(int residents, int moralePerMille)
    {
        if (residents <= 0 || moralePerMille >= 1000)
        {
            return 0;
        }

        return (int)((long)residents * (1000 - moralePerMille) / 1000);
    }
}
