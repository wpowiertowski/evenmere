namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// The pure arithmetic of workforce allocation: scaling the published supply by the <c>workforce</c> modifiers,
/// the equal per-mille share the unprioritised workplaces split, and the reduced crew the <c>crew</c> modifiers
/// leave a workplace needing (ADR-0015). Kept apart from the system so the rounding is testable on its own.
/// All divisions floor toward negative infinity, so allocation is deterministic.
/// </summary>
internal static class WorkforceMath
{
    /// <summary>Scales a tier's published supply by its summed <c>workforce</c> modifier percent.</summary>
    /// <param name="raw">The supply Population published; not positive means no workers.</param>
    /// <param name="bonusPercent">The summed modifier percent for the tier, as <c>BonusPercent</c> reports it; large penalties floor at zero workers.</param>
    /// <returns>The effective supply: <c>raw × (1000 + 10 × bonus) / 1000</c>, floored, at least 0.</returns>
    internal static int EffectiveSupply(int raw, int bonusPercent)
    {
        if (raw <= 0)
        {
            return 0;
        }

        long scaled = (long)raw * (1000L + 10L * bonusPercent) / 1000L;
        if (scaled <= 0)
        {
            return 0;
        }

        return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
    }

    /// <summary>Scales a workplace's content crew by its summed <c>crew</c> modifier percent (ADR-0015).</summary>
    /// <param name="count">The content workforce count, at least 1.</param>
    /// <param name="bonusPercent">The summed modifier percent for the workplace, as <c>BonusPercent</c> reports it; negative reduces.</param>
    /// <returns>
    /// The workers the workplace asks for: <c>max(1, (count × (1000 + 10 × P) + 500) / 1000)</c> in integer maths,
    /// half up and floored at 1, so a large penalty still leaves one worker. The conversion goes through
    /// <see cref="Core.Numerics.Ratio.FromBonusPercent"/>, never by subtracting from 1000.
    /// </returns>
    internal static int EffectiveCrew(int count, int bonusPercent)
    {
        int perMille = Core.Numerics.Ratio.FromBonusPercent(bonusPercent).PerMille;
        long scaled = ((long)count * perMille + 500L) / 1000L;
        if (scaled < 1)
        {
            scaled = 1;
        }

        return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
    }

    /// <summary>Shares the supply left after the high-priority workplaces across the rest at an equal per-mille fill.</summary>
    /// <param name="remaining">The supply left for the unprioritised workplaces.</param>
    /// <param name="totalDemand">Their summed workforce ask.</param>
    /// <returns>The shared fill in per-mille, floored; 1000 when the remainder covers the ask, 0 when there is nothing to share.</returns>
    internal static int SharedFill(int remaining, int totalDemand)
    {
        if (remaining <= 0 || totalDemand <= 0)
        {
            return 0;
        }

        if (remaining >= totalDemand)
        {
            return 1000;
        }

        return (int)((long)remaining * 1000 / totalDemand);
    }
}
