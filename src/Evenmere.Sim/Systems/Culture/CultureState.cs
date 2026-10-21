using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
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
    public Amount[] Progress { get; set; } = [];

    /// <summary>Gets or sets the adopted civics, as a bitset over <see cref="CivicId.Value"/>.</summary>
    [Key(1)]
    public ulong[] Done { get; set; } = [];

    /// <summary>Gets or sets the civics whose inspiration has triggered, as a bitset over <see cref="CivicId.Value"/>.</summary>
    [Key(2)]
    public ulong[] Inspired { get; set; } = [];

    /// <summary>Gets or sets the civic that receives the day's culture, or <see langword="null"/> when none is being adopted.</summary>
    [Key(3)]
    public CivicId? Target { get; set; }

    /// <summary>Gets or sets the civics that follow the target, in the order they will be adopted. The target itself is never in here.</summary>
    [Key(4)]
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

    /// <summary>Grows the progress array and the bitsets to the loaded content's civic count, keeping any values a loaded state already holds.</summary>
    /// <param name="civicCount">The number of civics in the content.</param>
    internal void EnsureSlots(int civicCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(civicCount);
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
    }
}
