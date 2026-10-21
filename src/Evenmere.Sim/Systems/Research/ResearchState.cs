using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// The research system's slice of the world state: knowledge progress per tech, the researched and inspired sets, the
/// target and queue, the points banked while nothing was targeted, and the last day's income. Progress is a dense array
/// indexed by <see cref="TechId.Value"/>; the sets are bitsets over the same IDs. Every member added later gets an
/// explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ResearchState
{
    /// <summary>Gets or sets the knowledge each tech has received, in milli-units, indexed by <see cref="TechId.Value"/>.</summary>
    [Key(0)]
    [ContentIndexed(ContentKind.Tech)]
    public Amount[] Progress { get; set; } = [];

    /// <summary>Gets or sets the researched techs, as a bitset over <see cref="TechId.Value"/>.</summary>
    [Key(1)]
    [ContentIndexed(ContentKind.Tech)]
    public ulong[] Done { get; set; } = [];

    /// <summary>Gets or sets the techs whose inspiration has triggered, as a bitset over <see cref="TechId.Value"/>.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Tech)]
    public ulong[] Inspired { get; set; } = [];

    /// <summary>Gets or sets the tech that receives the day's knowledge, or <see langword="null"/> when nothing is being researched.</summary>
    [Key(3)]
    public TechId? Target { get; set; }

    /// <summary>Gets or sets the techs that follow the target, in the order they will be researched. The target itself is never in here.</summary>
    [Key(4)]
    [ContentValued(ContentKind.Tech)]
    public TechId[] Queue { get; set; } = [];

    /// <summary>
    /// Gets or sets the knowledge that arrived while nothing was targeted. It is carried over to the next target instead of
    /// being lost: each daily accrual spends the banked points before the new day's.
    /// </summary>
    [Key(5)]
    public Amount Banked { get; set; }

    /// <summary>Gets or sets the knowledge the last daily accrual received, in milli-units; 0 before the first accrual.</summary>
    [Key(6)]
    public Amount PointsPerDay { get; set; }

    /// <summary>Grows the progress array and the bitsets to the loaded content's tech count, keeping any values a loaded state already holds.</summary>
    /// <param name="techCount">The number of techs in the content.</param>
    internal void EnsureSlots(int techCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(techCount);
        if (Progress.Length < techCount)
        {
            var grown = new Amount[techCount];
            Array.Copy(Progress, grown, Progress.Length);
            Progress = grown;
        }

        int words = (techCount + 63) >> 6;
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
