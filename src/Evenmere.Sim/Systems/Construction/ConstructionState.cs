using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>What a <see cref="SiteRecord"/> is building. The values are part of the save format; add members at the end.</summary>
public enum SiteKind : byte
{
    /// <summary>A new building: the building is in the <see cref="BuildingState.Site"/> state until the work is done.</summary>
    Build = 0,

    /// <summary>An in-place upgrade: the building stays <see cref="BuildingState.Active"/> (or paused) and keeps working until the work is done.</summary>
    Upgrade = 1,
}

/// <summary>
/// The progress of one construction site or in-place upgrade, keyed by the building's entity ID in
/// <see cref="ConstructionState.Sites"/>. Work is counted in milli-ticks: a site at +0% construction speed gains 1000 per
/// tick and needs <c>buildDays × 120 × 1000</c> in all, so a faster site simply gains more per tick. The materials of the
/// bill are drawn in <c>SiteMath.Steps</c> (10) equal steps, one before each tenth of the work.
/// </summary>
[MessagePackObject]
public struct SiteRecord
{
    /// <summary>Gets or sets whether this is a new building or an upgrade.</summary>
    [Key(0)]
    public SiteKind Kind { get; set; }

    /// <summary>Gets or sets the type being built, which is also the type whose bill and build time apply; for an upgrade it is the type the building becomes.</summary>
    [Key(1)]
    public BuildingTypeId Target { get; set; }

    /// <summary>Gets or sets the tick in which the site was placed; the site first works on the tick after it.</summary>
    [Key(2)]
    public long PlacedTick { get; set; }

    /// <summary>Gets or sets the work done so far, in milli-ticks.</summary>
    [Key(3)]
    public long Work { get; set; }

    /// <summary>Gets or sets how many material steps have been taken from the storage pool (0 to 10).</summary>
    [Key(4)]
    public int StepsDrawn { get; set; }

    /// <summary>Gets or sets how many build steps are complete (0 to 10); a step is complete when its tenth of the work is done.</summary>
    [Key(5)]
    public int StepsDone { get; set; }

    /// <summary>Gets or sets why the site did not advance on its last tick: none, no storehouse reach, or missing material.</summary>
    [Key(6)]
    public StallReason Stall { get; set; }

    /// <summary>Gets or sets the good the next step lacked; meaningful only when <see cref="Stall"/> is <see cref="StallReason.MissingMaterial"/>.</summary>
    [Key(7)]
    public GoodId WaitingFor { get; set; }

    /// <summary>
    /// Gets or sets the 0-based monument stage under construction. Only a <see cref="SiteKind.Build"/> site whose type a
    /// <c>MonumentDef</c> names builds in stages; every other site stays at 0. Added in save version 2 (ADR-0018): older
    /// saves load with the default 0, which is the first stage.
    /// </summary>
    [Key(8)]
    public int StageIndex { get; set; }
}

/// <summary>
/// The construction system's slice of the world state: one <see cref="SiteRecord"/> per building that is under
/// construction or being upgraded. Buildings that are finished have no record. Every member added later gets an
/// explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ConstructionState
{
    /// <summary>Gets or sets the sites, by the entity ID of the building they build or upgrade, in ascending ID order.</summary>
    [Key(0)]
    [MessagePackFormatter(typeof(IdTableFormatter<SiteRecord>))]
    public IdTable<SiteRecord> Sites { get; set; } = new();
}
