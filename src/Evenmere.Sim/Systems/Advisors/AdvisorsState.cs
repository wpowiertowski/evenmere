using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using MessagePack;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// One live advisor note: a (kind, subject) problem the daily rules confirmed. There is at most one record per
/// (kind, subject); the ID comes from the state's counter and is never reused, so a dismissed note never comes back
/// under its old ID. <see cref="Showing"/> is sticky: a record that is showing stays showing until its problem clears,
/// and a newer, higher-priority problem never bumps it.
/// </summary>
[MessagePackObject]
public struct AdviceRecord
{
    /// <summary>Gets or sets the note's stable ID, from the state's deterministic counter; never reused.</summary>
    [Key(0)]
    public int Id { get; set; }

    /// <summary>Gets or sets what the problem is.</summary>
    [Key(1)]
    public AdviceKind Kind { get; set; }

    /// <summary>Gets or sets what the problem is about; <see cref="SubjectLink.None"/> for the idle notes.</summary>
    [Key(2)]
    public SubjectLink Subject { get; set; }

    /// <summary>Gets or sets the tick the problem was first detected; kept when the note is re-confirmed.</summary>
    [Key(3)]
    public long FirstSeenTick { get; set; }

    /// <summary>Gets or sets the tick the problem was last confirmed.</summary>
    [Key(4)]
    public long LastSeenTick { get; set; }

    /// <summary>Gets or sets a value indicating whether the note is one of the at most five showing.</summary>
    [Key(5)]
    public bool Showing { get; set; }
}

/// <summary>
/// A dismissed (kind, subject) that may not raise a record again until <see cref="ExpiresTick"/>. While it lives, the
/// daily rules skip the pair outright; once it expires (and is pruned), a still-present problem raises a new record
/// with a new ID.
/// </summary>
[MessagePackObject]
public struct Suppression
{
    /// <summary>Gets or sets what the dismissed problem was.</summary>
    [Key(0)]
    public AdviceKind Kind { get; set; }

    /// <summary>Gets or sets what the dismissed problem was about.</summary>
    [Key(1)]
    public SubjectLink Subject { get; set; }

    /// <summary>Gets or sets the first tick the pair may raise a record again (dismiss tick plus one season).</summary>
    [Key(2)]
    public long ExpiresTick { get; set; }
}

/// <summary>
/// The advisors system's slice of the world state: the live notes, the deterministic ID counter, the two-day streak
/// counters per good and per tier, and the dismissal suppressions. Records and suppressions are kept sorted by
/// (kind, subject kind, subject value), so lookups are binary searches and iteration order is deterministic. The
/// streak arrays are indexed by <c>GoodId.Value</c> and <c>TierId.Value</c>. Every member has an explicit integer
/// <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class AdvisorsState
{
    /// <summary>Gets or sets the live notes, one per (kind, subject), sorted by (kind, subject kind, subject value).</summary>
    [Key(0)]
    [ContentValued(ContentKind.Good, ContentKind.Tier, ContentKind.Building)]
    public AdviceRecord[] Records { get; set; } = [];

    /// <summary>Gets or sets the next advice ID; it only ever grows, so IDs are never reused. Starts at 1.</summary>
    [Key(1)]
    public int NextId { get; set; } = 1;

    /// <summary>Gets or sets the consecutive daily evaluations each good has been short, indexed by <c>GoodId.Value</c>, capped at 2.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Good)]
    public int[] GoodStreak { get; set; } = [];

    /// <summary>Gets or sets the consecutive daily evaluations each tier has been short of workers, indexed by <c>TierId.Value</c>, capped at 2.</summary>
    [Key(3)]
    [ContentIndexed(ContentKind.Tier)]
    public int[] TierStreak { get; set; } = [];

    /// <summary>Gets or sets the dismissal suppressions, sorted by (kind, subject kind, subject value).</summary>
    [Key(4)]
    [ContentValued(ContentKind.Good, ContentKind.Tier, ContentKind.Building)]
    public Suppression[] Suppressions { get; set; } = [];

    /// <summary>Grows the streak arrays to the loaded content's goods and tiers, keeping any values a loaded state already holds.</summary>
    /// <param name="goodCount">The number of goods in the content.</param>
    /// <param name="tierCount">The number of tiers in the content.</param>
    internal void EnsureSlots(int goodCount, int tierCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(goodCount);
        ArgumentOutOfRangeException.ThrowIfNegative(tierCount);
        if (GoodStreak.Length < goodCount)
        {
            int[] grown = new int[goodCount];
            Array.Copy(GoodStreak, grown, GoodStreak.Length);
            GoodStreak = grown;
        }

        if (TierStreak.Length < tierCount)
        {
            int[] grown = new int[tierCount];
            Array.Copy(TierStreak, grown, TierStreak.Length);
            TierStreak = grown;
        }
    }
}
