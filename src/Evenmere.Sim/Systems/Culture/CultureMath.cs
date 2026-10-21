using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// The pure rules behind civics: availability, queue reachability, the inspiration-discounted cost and the
/// building-output credit. The queries, the command handlers and the daily accrual all read these, so the target the
/// view shows is the target the commands accept and the day spends. Everything here is integer maths (ADR-0003).
/// </summary>
internal static class CultureMath
{
    /// <summary>Tests whether a civic belongs to the loaded content.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if the content defines the civic.</returns>
    public static bool IsKnown(ContentDb content, CivicId civic) => civic.Value < content.Civics.Count;

    /// <summary>Tests whether a civic can be chosen as the target: it is known, not adopted, all its prerequisites are adopted, and its era has begun.</summary>
    /// <param name="state">The culture state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="eraOrder">The current era's order, for comparing with the civic's era.</param>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if it is available.</returns>
    public static bool IsAvailable(CultureState state, ContentDb content, int eraOrder, CivicId civic)
    {
        if (!IsKnown(content, civic) || Bits.Has(state.Done, civic.Value))
        {
            return false;
        }

        CivicDef def = content.Get(civic);
        if (def.Era > eraOrder)
        {
            return false;
        }

        foreach (CivicId require in def.Requires)
        {
            // A require the content does not define reads as not done (Bits.Has reads past the end as clear), so a
            // corrupt definition locks the civic instead of crashing the check.
            if (!Bits.Has(state.Done, require.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether the entry at <paramref name="index"/> can follow the <paramref name="earlierCount"/> entries before
    /// it: it is known, not adopted, not the target, not a repeat, its era begun, and every prerequisite adopted, the
    /// target, or one of the earlier entries. The queue command's validation and the target command's pruning both read
    /// this, so the two cannot drift apart.
    /// </summary>
    /// <param name="state">The culture state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="eraOrder">The current era's order, for comparing with the entry's era.</param>
    /// <param name="target">The target the entry would follow.</param>
    /// <param name="queue">The queue the entry stands in.</param>
    /// <param name="index">The entry's position; the candidate is <c>queue[index]</c>.</param>
    /// <param name="earlierCount">How many of the entries before <paramref name="index"/> count as earlier: the prefix
    /// length for validation, or the kept prefix length while pruning.</param>
    /// <returns>Why the entry cannot follow, or <see cref="QueueReachability.Reachable"/> when it can.</returns>
    public static QueueReachability CheckQueueEntry(CultureState state, ContentDb content, int eraOrder, CivicId? target, CivicId[] queue, int index, int earlierCount)
    {
        CivicId candidate = queue[index];
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

        foreach (CivicId require in content.Get(candidate).Requires)
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

    /// <summary>Tests whether a civic's inspiration has triggered.</summary>
    /// <param name="state">The culture state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="civic">The civic.</param>
    /// <returns><see langword="true"/> if it is inspired.</returns>
    public static bool IsInspired(CultureState state, ContentDb content, CivicId civic) =>
        IsKnown(content, civic) && Bits.Has(state.Inspired, civic.Value);

    /// <summary>Reads the culture a civic has received so far.</summary>
    /// <param name="state">The culture state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="civic">The civic.</param>
    /// <returns>The progress in milli-units; 0 for a civic the content does not define.</returns>
    public static Amount ProgressOf(CultureState state, ContentDb content, CivicId civic) =>
        IsKnown(content, civic) && civic.Value < state.Progress.Length ? state.Progress[civic.Value] : Amount.Zero;

    /// <summary>
    /// Gets what a civic still costs in all: the full cost, or the full cost minus the inspiration discount once inspired.
    /// The discounted cost never falls below the progress already made, so inspiring a nearly finished civic completes it.
    /// </summary>
    /// <param name="state">The culture state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="discountPerMille">The inspiration discount, in per-mille.</param>
    /// <param name="civic">The civic.</param>
    /// <returns>The cost in milli-units; 0 for a civic the content does not define.</returns>
    public static Amount EffectiveCost(CultureState state, ContentDb content, int discountPerMille, CivicId civic)
    {
        if (!IsKnown(content, civic))
        {
            return Amount.Zero;
        }

        Amount full = content.Get(civic).Cost;
        if (!Bits.Has(state.Inspired, civic.Value))
        {
            return full;
        }

        long discounted = full.Milli - full.Milli * discountPerMille / 1000;
        long progress = civic.Value < state.Progress.Length ? state.Progress[civic.Value].Milli : 0;
        return Amount.FromMilli(Math.Max(discounted, progress));
    }

    /// <summary>
    /// Credits one building's daily culture: its outputs, scaled by its workforce fill and then by the
    /// <c>output.culture</c> bonus, in integer maths. The bonus is a percent (+25 means \u00d71.25) and floors at zero,
    /// so a gloomy shrine yields nothing; it never takes culture away.
    /// </summary>
    /// <param name="output">The building's daily culture output, in milli-units.</param>
    /// <param name="fill">The building's workforce fill, in per-mille; 1000 is fully staffed.</param>
    /// <param name="bonusPercent">The <c>output.culture</c> bonus in percent.</param>
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
    /// <summary>The entry can be adopted in its position.</summary>
    Reachable = 0,

    /// <summary>The entry is unknown to the content or already adopted.</summary>
    UnknownOrDone = 1,

    /// <summary>The entry is the target.</summary>
    IsTarget = 2,

    /// <summary>The entry repeats an earlier one.</summary>
    Duplicate = 3,

    /// <summary>The entry's era has not begun.</summary>
    EraLocked = 4,

    /// <summary>A prerequisite is neither adopted nor the target nor an earlier entry.</summary>
    PrerequisiteMissing = 5,
}
