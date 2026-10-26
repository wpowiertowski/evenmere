using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;
using MessagePack;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// The culture system's slice of the world state: culture progress per civic, the adopted and inspired sets, the target
/// and queue, the points banked while nothing was targeted, and the last day's income. Progress is a dense array indexed
/// by <see cref="CivicId.Value"/>; the sets are bitsets over the same IDs. Every member added later gets an explicit
/// integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class CultureState
{
    /// <summary>Gets or sets the culture each civic has received, in milli-units, indexed by <see cref="CivicId.Value"/>.</summary>
    [Key(0)]
    [ContentIndexed(ContentKind.Civic)]
    public Amount[] Progress { get; set; } = [];

    /// <summary>Gets or sets the adopted civics, as a bitset over <see cref="CivicId.Value"/>.</summary>
    [Key(1)]
    [ContentIndexed(ContentKind.Civic)]
    public ulong[] Done { get; set; } = [];

    /// <summary>Gets or sets the civics whose inspiration has triggered, as a bitset over <see cref="CivicId.Value"/>.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Civic)]
    public ulong[] Inspired { get; set; } = [];

    /// <summary>Gets or sets the civic that receives the day's culture, or <see langword="null"/> when none is being adopted.</summary>
    [Key(3)]
    public CivicId? Target { get; set; }

    /// <summary>Gets or sets the civics that follow the target, in the order they will be adopted. The target itself is never in here.</summary>
    [Key(4)]
    [ContentValued(ContentKind.Civic)]
    public CivicId[] Queue { get; set; } = [];

    /// <summary>
    /// Gets or sets the culture that arrived while nothing was targeted. It is carried over to the next target instead of
    /// being lost: each daily accrual spends the banked points before the new day's.
    /// </summary>
    [Key(5)]
    public Amount Banked { get; set; }

    /// <summary>Gets or sets the culture the last daily accrual received, in milli-units; 0 before the first accrual.</summary>
    [Key(6)]
    public Amount PointsPerDay { get; set; }

    /// <summary>
    /// Gets or sets the slotted tradition cards, sorted by (slot type, index). Slot counts stay in
    /// <see cref="Progression.ProgressionState.TraditionSlots"/>; the cards ride the version-2 save with load-safe
    /// defaults (no slot is slotted), so no migration class is needed. (M2-07.)
    /// </summary>
    [Key(7)]
    [ContentValued(ContentKind.Tradition)]
    public SlottedTradition[] Slotted { get; set; } = [];

    /// <summary>
    /// Gets or sets the running festival, if one is running. The culture system's daily step counts it down;
    /// the running flag below is what Population reads. Rides the version-2 save with load-safe defaults
    /// (no festival runs), so no migration class is needed. (M2-08.)
    /// </summary>
    [Key(8)]
    public FestivalRun? Active { get; set; }

    /// <summary>
    /// Gets or sets the last year each festival type was held, indexed by <see cref="FestivalId.Value"/>.
    /// Each entry keeps the year plus one, so 0 means never held: a fresh game in year 0 and a remap that adds a
    /// festival both read as never held. Rides the version-2 save with load-safe defaults. (M2-08.)
    /// </summary>
    [Key(9)]
    [ContentIndexed(ContentKind.Festival)]
    public int[] FestivalLastHeldYear { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a festival is running. Population mirrors this flag through its
    /// queries and pauses every house's decline timer while it is set. Defaults false. (M2-08.)
    /// </summary>
    [Key(10)]
    public bool FestivalRunning { get; set; }

    /// <summary>Grows the progress and festival arrays and the bitsets to the loaded content's counts, keeping any values a loaded state already holds.</summary>
    /// <param name="civicCount">The number of civics in the content.</param>
    /// <param name="festivalCount">The number of festivals in the content.</param>
    internal void EnsureSlots(int civicCount, int festivalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(civicCount);
        ArgumentOutOfRangeException.ThrowIfNegative(festivalCount);
        Slotted ??= [];
        FestivalLastHeldYear ??= [];
        if (Progress.Length < civicCount)
        {
            var grown = new Amount[civicCount];
            Array.Copy(Progress, grown, Progress.Length);
            Progress = grown;
        }

        int words = (civicCount + 63) >> 6;
        if (Done.Length < words)
        {
            ulong[] grown = new ulong[words];
            Array.Copy(Done, grown, Done.Length);
            Done = grown;
        }

        if (Inspired.Length < words)
        {
            ulong[] grown = new ulong[words];
            Array.Copy(Inspired, grown, Inspired.Length);
            Inspired = grown;
        }

        // New entries keep 0, which the scheduler reads as never held (entries keep the year plus one).
        // The remap coverage guard requires the default for added items, and here the default is the right value.
        if (FestivalLastHeldYear.Length < festivalCount)
        {
            int[] grown = new int[festivalCount];
            Array.Copy(FestivalLastHeldYear, grown, FestivalLastHeldYear.Length);
            FestivalLastHeldYear = grown;
        }
    }
}
