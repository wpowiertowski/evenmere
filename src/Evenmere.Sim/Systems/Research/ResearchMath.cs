using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// The pure rules behind research: availability, queue reachability, the inspiration-discounted cost and the
/// building-output credit. The queries, the command handlers and the daily accrual all read these, so the target the
/// view shows is the target the commands accept and the day spends. Everything here is integer maths (ADR-0003).
/// </summary>
internal static class ResearchMath
{
    /// <summary>Tests whether a tech belongs to the loaded content.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if the content defines the tech.</returns>
    public static bool IsKnown(ContentDb content, TechId tech) => tech.Value < content.Techs.Count;

    /// <summary>Tests whether a tech can be chosen as the target: it is known, not done, all its prerequisites are done, and its era has begun.</summary>
    /// <param name="state">The research state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="eraOrder">The current era's order, for comparing with the tech's era.</param>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if it is available.</returns>
    public static bool IsAvailable(ResearchState state, ContentDb content, int eraOrder, TechId tech)
    {
        if (!IsKnown(content, tech) || Bits.Has(state.Done, tech.Value))
        {
            return false;
        }

        TechDef def = content.Get(tech);
        if (def.Era > eraOrder)
        {
            return false;
        }

        foreach (TechId require in def.Requires)
        {
            // A require the content does not define reads as not done (Bits.Has reads past the end as clear), so a
            // corrupt definition locks the tech instead of crashing the check.
            if (!Bits.Has(state.Done, require.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether the entry at <paramref name="index"/> can follow the <paramref name="earlierCount"/> entries before
    /// it: it is known, not done, not the target, not a repeat, its era begun, and every prerequisite done, the target,
    /// or one of the earlier entries. The queue command's validation and the target command's pruning both read this, so
    /// the two cannot drift apart.
    /// </summary>
    /// <param name="state">The research state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="eraOrder">The current era's order, for comparing with the entry's era.</param>
    /// <param name="target">The target the entry would follow.</param>
    /// <param name="queue">The queue the entry stands in.</param>
    /// <param name="index">The entry's position; the candidate is <c>queue[index]</c>.</param>
    /// <param name="earlierCount">How many of the entries before <paramref name="index"/> count as earlier: the prefix
    /// length for validation, or the kept prefix length while pruning.</param>
    /// <returns>Why the entry cannot follow, or <see cref="QueueReachability.Reachable"/> when it can.</returns>
    public static QueueReachability CheckQueueEntry(ResearchState state, ContentDb content, int eraOrder, TechId? target, TechId[] queue, int index, int earlierCount)
    {
        TechId candidate = queue[index];
        if (!IsKnown(content, candidate) || Bits.Has(state.Done, candidate.Value))
        {
            return QueueReachability.UnknownOrDone;
        }

        if (target is { } current && current == candidate)
        {
            return QueueReachability.IsTarget;
        }

        for (int j = 0; j < earlierCount; j++)
        {
            if (queue[j] == candidate)
            {
                return QueueReachability.Duplicate;
            }
        }

        if (content.Get(candidate).Era > eraOrder)
        {
            return QueueReachability.EraLocked;
        }

        foreach (TechId require in content.Get(candidate).Requires)
        {
            if (Bits.Has(state.Done, require.Value))
            {
                continue;
            }

            if (target is { } need && need == require)
            {
                continue;
            }

            bool earlier = false;
            for (int j = 0; j < earlierCount; j++)
            {
                if (queue[j] == require)
                {
                    earlier = true;
                    break;
                }
            }

            if (!earlier)
            {
                return QueueReachability.PrerequisiteMissing;
            }
        }

        return QueueReachability.Reachable;
    }

    /// <summary>Tests whether a tech's inspiration has triggered.</summary>
    /// <param name="state">The research state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="tech">The tech.</param>
    /// <returns><see langword="true"/> if it is inspired.</returns>
    public static bool IsInspired(ResearchState state, ContentDb content, TechId tech) =>
        IsKnown(content, tech) && Bits.Has(state.Inspired, tech.Value);

    /// <summary>Reads the knowledge a tech has received so far.</summary>
    /// <param name="state">The research state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="tech">The tech.</param>
    /// <returns>The progress in milli-units; 0 for a tech the content does not define.</returns>
    public static Amount ProgressOf(ResearchState state, ContentDb content, TechId tech) =>
        IsKnown(content, tech) && tech.Value < state.Progress.Length ? state.Progress[tech.Value] : Amount.Zero;

    /// <summary>
    /// Gets what a tech still costs in all: the full cost, or the full cost minus the inspiration discount once inspired.
    /// The discounted cost never falls below the progress already made, so inspiring a nearly finished tech completes it.
    /// </summary>
    /// <param name="state">The research state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="discountPerMille">The inspiration discount, in per-mille.</param>
    /// <param name="tech">The tech.</param>
    /// <returns>The cost in milli-units; 0 for a tech the content does not define.</returns>
    public static Amount EffectiveCost(ResearchState state, ContentDb content, int discountPerMille, TechId tech)
    {
        if (!IsKnown(content, tech))
        {
            return Amount.Zero;
        }

        Amount full = content.Get(tech).Cost;
        if (!Bits.Has(state.Inspired, tech.Value))
        {
            return full;
        }

        long discounted = full.Milli - full.Milli * discountPerMille / 1000;
        long progress = tech.Value < state.Progress.Length ? state.Progress[tech.Value].Milli : 0;
        return Amount.FromMilli(Math.Max(discounted, progress));
    }

    /// <summary>
    /// Credits one building's daily knowledge: its outputs, scaled by its workforce fill and then by the
    /// <c>output.knowledge</c> bonus, in integer maths. The bonus is a percent (+25 means \u00d71.25) and floors at zero,
    /// so a gloomy hall yields nothing; it never takes knowledge away.
    /// </summary>
    /// <param name="output">The building's daily knowledge output, in milli-units.</param>
    /// <param name="fill">The building's workforce fill, in per-mille; 1000 is fully staffed.</param>
    /// <param name="bonusPercent">The <c>output.knowledge</c> bonus in percent.</param>
    /// <returns>The credit in milli-units; never negative.</returns>
    public static Amount BuildingCredit(Amount output, Ratio fill, int bonusPercent)
    {
        if (output <= Amount.Zero)
        {
            return Amount.Zero;
        }

        long milli = output.Milli * fill.PerMille / 1000;
        milli = milli * Ratio.FromBonusPercent(bonusPercent).PerMille / 1000;
        return Amount.FromMilli(milli);
    }
}

/// <summary>Why a queue entry cannot follow its predecessors, or <see cref="Reachable"/> when it can.</summary>
internal enum QueueReachability
{
    /// <summary>The entry can be researched in its position.</summary>
    Reachable = 0,

    /// <summary>The entry is unknown to the content or already researched.</summary>
    UnknownOrDone = 1,

    /// <summary>The entry is the target.</summary>
    IsTarget = 2,

    /// <summary>The entry repeats an earlier one.</summary>
    Duplicate = 3,

    /// <summary>The entry's era has not begun.</summary>
    EraLocked = 4,

    /// <summary>A prerequisite is neither done nor the target nor an earlier entry.</summary>
    PrerequisiteMissing = 5,
}
