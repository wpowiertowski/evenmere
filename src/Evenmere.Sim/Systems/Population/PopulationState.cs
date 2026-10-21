using Evenmere.Core.Collections;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// The population system's slice of the world state: one <see cref="HouseRecord"/> per house, keyed by the house's entity
/// ID (the same as its building's), plus what the system tells Workforce. A house gets its record when its building
/// becomes active and loses it when the building is removed. Every member added later gets an explicit integer
/// <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class PopulationState
{
    /// <summary>Gets or sets the houses in ascending ID order.</summary>
    /// <remarks>
    /// The formatter is named because the MessagePack source generator cannot see through <see cref="IdTable{T}"/>: left to
    /// itself it writes a formatter for the table that does not compile. The runtime resolver (<c>SimResolver</c>) uses the
    /// same <see cref="IdTableFormatter{TValue}"/>, so the bytes are the same either way.
    /// </remarks>
    [Key(0)]
    [MessagePackFormatter(typeof(IdTableFormatter<HouseRecord>))]
    public IdTable<HouseRecord> Houses { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether a festival is running, which pauses every house's decline timer
    /// (progression.md, How needs work). It is <see langword="false"/> until the culture system's festivals (M2-08) set it.
    /// </summary>
    [Key(1)]
    public bool FestivalActive { get; set; }

    /// <summary>Gets or sets the workers last passed to Workforce for each tier, indexed by <c>TierId.Value</c>; empty until the first house exists.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Tier)]
    public int[] SuppliedWorkers { get; set; } = [];
}
