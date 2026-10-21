using Evenmere.Core.Collections;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// The workforce system's slice of the world state: the raw workforce supply Population published per tier, and one
/// <see cref="WorkplaceRecord"/> per workplace that has been staffed or marked as high priority. Every member has an
/// explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class WorkforceState
{
    /// <summary>Gets or sets the raw supply Population set, indexed by <c>TierId.Value</c>. Queries scale it by the <c>workforce</c> modifiers.</summary>
    [Key(0)]
    [ContentIndexed(ContentKind.Tier)]
    public int[] RawSupply { get; set; } = [];

    /// <summary>Gets or sets one record per workplace the allocation or the priority command has touched, keyed by the building's entity ID.</summary>
    // The formatter is named outright because MessagePack's source generator cannot find one for a generic collection from another
    // assembly and emits code that does not compile; at run time SimResolver would pick the same formatter anyway.
    [Key(1)]
    [MessagePackFormatter(typeof(IdTableFormatter<WorkplaceRecord>))]
    public IdTable<WorkplaceRecord> Workplaces { get; set; } = new();

    /// <summary>Grows the supply array to the loaded content's tier count, keeping any values a loaded state already holds.</summary>
    /// <param name="tierCount">The number of tiers in the content.</param>
    internal void EnsureTierSlots(int tierCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tierCount);
        if (RawSupply.Length >= tierCount)
        {
            return;
        }

        int[] grown = new int[tierCount];
        Array.Copy(RawSupply, grown, RawSupply.Length);
        RawSupply = grown;
    }
}

/// <summary>
/// Everything the workforce system remembers about one workplace: how many workers the last daily allocation gave it,
/// and whether the player marked it as high priority. The flag is stored here (rather than only in the query layer) so
/// it survives save/load, and only <see cref="Evenmere.Sim.Services.IBuildingLifecycleListener.OnRemoved"/> clears it.
/// </summary>
[MessagePackObject]
public sealed class WorkplaceRecord
{
    /// <summary>Gets or sets the workers the last allocation assigned to this workplace.</summary>
    [Key(0)]
    public int Allocated { get; set; }

    /// <summary>Gets or sets a value indicating whether the player marked this workplace as high priority with <c>SetWorkforcePriority</c>.</summary>
    [Key(1)]
    public bool HighPriority { get; set; }
}
