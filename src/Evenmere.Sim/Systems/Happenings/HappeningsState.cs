using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>
/// A happening the island is waiting on an answer for: the happening and the tick its daily step raised it on.
/// The player has ten days to choose before the system applies the default option. (M2-12.)
/// </summary>
/// <param name="Happening">The happening waiting for a choice.</param>
/// <param name="RaisedTick">The tick the daily step raised it on.</param>
[MessagePackObject]
public readonly record struct PendingHappening(
    [property: Key(0)] HappeningId Happening,
    [property: Key(1)] long RaisedTick);

/// <summary>
/// A duration-bound modifier batch a chosen happening option left behind: the happening it came from and the tick it
/// expires on. The daily step removes the happening's modifiers through <c>RemoveSource</c> once the tick is reached.
/// One entry per resolve, so a repeat choice replaces the old expiry instead of stacking with it. (M2-12.)
/// </summary>
/// <param name="Happening">The happening whose option applied the modifiers.</param>
/// <param name="ExpiresTick">The tick the modifiers are removed on.</param>
[MessagePackObject]
public readonly record struct TempModifierRecord(
    [property: Key(0)] HappeningId Happening,
    [property: Key(1)] long ExpiresTick);

/// <summary>
/// The happenings system's slice of the world state: the one pending happening, one cooldown per happening and the
/// duration-bound modifiers chosen options left behind. Empty in M1; M2-12 fills it. Every member added later gets an
/// explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class HappeningsState
{
    /// <summary>Gets or sets the happening waiting for a choice, if one is pending; at most one is pending at a time.</summary>
    [Key(0)]
    public PendingHappening? Pending { get; set; }

    /// <summary>
    /// Gets or sets the tick until which each happening is cooling down, indexed by <see cref="HappeningId.Value"/>.
    /// A happening is eligible once the tick reaches its entry; 0 means no cooldown. Grown with load-safe
    /// defaults (no cooldown) by <see cref="EnsureSlots"/>, so a version-2 save from before M2-12 loads as-is.
    /// </summary>
    [Key(1)]
    [ContentIndexed(ContentKind.Happening)]
    public long[] CooldownUntil { get; set; } = [];

    /// <summary>
    /// Gets or sets the duration-bound modifier batches chosen options left behind, in resolve order. Each entry holds
    /// a happening ID the load-time remap must move, so this rides the same pass as the other happening members.
    /// </summary>
    [Key(2)]
    [ContentValued(ContentKind.Happening)]
    public TempModifierRecord[] TempModifiers { get; set; } = [];

    /// <summary>Grows the cooldown array to the loaded content's happening count, keeping any values a loaded state already holds.</summary>
    /// <param name="happeningCount">The number of happenings in the content.</param>
    internal void EnsureSlots(int happeningCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(happeningCount);
        TempModifiers ??= [];
        if (CooldownUntil.Length < happeningCount)
        {
            long[] grown = new long[happeningCount];
            Array.Copy(CooldownUntil, grown, CooldownUntil.Length);
            CooldownUntil = grown;
        }
    }
}
