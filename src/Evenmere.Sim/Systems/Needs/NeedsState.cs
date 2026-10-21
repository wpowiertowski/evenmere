using MessagePack;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// The needs system's slice of the world state: the day's fair shares. Each house's own need masks live in its
/// <c>HouseRecord</c>, because Needs and Population are two stages of one daily pass over the same house. Every member
/// added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class NeedsState
{
    /// <summary>Gets or sets the demand and fulfilment of every good, computed at the start of the day.</summary>
    [Key(0)]
    public FairShares Shares { get; set; } = new();
}
