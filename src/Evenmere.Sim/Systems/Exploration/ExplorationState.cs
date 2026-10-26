using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// One expedition under way: the fogged region it travels to, the days left before it returns, and the lodge that sent it.
/// Days count down once per day; the region reveals when the count reaches zero. (M2-06.)
/// </summary>
[MessagePackObject]
public struct ExpeditionRecord
{
    /// <summary>Gets or sets the region number the expedition travels to.</summary>
    [Key(0)]
    public ushort TargetRegion { get; set; }

    /// <summary>Gets or sets the days left before the region reveals; at least 1 while under way.</summary>
    [Key(1)]
    public int DaysLeft { get; set; }

    /// <summary>Gets or sets the Scout's Lodge that sent the expedition.</summary>
    [Key(2)]
    public EntityId Lodge { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ExpeditionRecord"/> struct.</summary>
    /// <param name="targetRegion">The region number the expedition travels to.</param>
    /// <param name="daysLeft">The days left before the region reveals.</param>
    /// <param name="lodge">The Scout's Lodge that sent the expedition.</param>
    public ExpeditionRecord(ushort targetRegion, int daysLeft, EntityId lodge)
    {
        TargetRegion = targetRegion;
        DaysLeft = daysLeft;
        Lodge = lodge;
    }
}

/// <summary>
/// The exploration system's slice of the world state: revealed regions, expeditions and discoveries. Empty in M0; M2-06 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ExplorationState
{
    /// <summary>Gets or sets the revealed regions as a bitset: bit <c>n</c> is region <c>n</c>. Empty until the first new-game reveal or the load-time default for pre-fog saves.</summary>
    [Key(0)]
    [NotContentIndexed("region numbers are map data, not content IDs")]
    public ulong[] RevealedRegions { get; set; } = [];

    /// <summary>Gets or sets the expeditions under way, by the lodge entity that sent them, in ascending lodge-ID order.</summary>
    [Key(1)]
    [MessagePackFormatter(typeof(IdTableFormatter<ExpeditionRecord>))]
    public IdTable<ExpeditionRecord> Expeditions { get; set; } = new();

    /// <summary>Gets or sets whether each discovery's effect has applied, indexed by <c>MapState.Discoveries</c> order. Grown with load-safe defaults (unapplied) by <see cref="EnsureSlots"/>.</summary>
    [Key(2)]
    [NotContentIndexed("map feature indices, not content IDs")]
    public bool[] DiscoveriesApplied { get; set; } = [];

    /// <summary>Grows the per-discovery flags to the island's discovery count, keeping any values a loaded state already holds.</summary>
    /// <param name="discoveryCount">The number of discoveries on the island.</param>
    internal void EnsureSlots(int discoveryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discoveryCount);
        DiscoveriesApplied ??= [];
        Expeditions ??= new();
        RevealedRegions ??= [];
        if (DiscoveriesApplied.Length < discoveryCount)
        {
            bool[] grown = new bool[discoveryCount];
            Array.Copy(DiscoveriesApplied, grown, DiscoveriesApplied.Length);
            DiscoveriesApplied = grown;
        }
    }
}
