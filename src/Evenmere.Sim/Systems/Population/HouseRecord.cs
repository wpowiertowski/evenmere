using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using MessagePack;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// Everything the simulation remembers about one house. The tier lives here and not in the building type, so a house
/// advances and declines without its building changing (ADR-0010). The Needs system and the Population system share this
/// record because they are two stages of one daily pass over the same house (architecture.md, System order): Needs writes
/// the mask fields and <see cref="MarketCovered"/> on the house's bucket tick, and Population reads them right after, on the
/// same tick, and writes everything else.
/// </summary>
/// <remarks>
/// The three masks index the tier's needs in the order of its definition: for the basic and ever-met masks, bit <c>i</c> is
/// the <c>i</c>-th basic good and bit <c>goods + j</c> the <c>j</c>-th basic service; for the comfort mask, bit <c>k</c> is
/// the <c>k</c>-th comfort good. A mask is valid for <see cref="Tier"/> only: it is cleared when the tier changes.
/// <see cref="Capacity"/>, <see cref="TargetResidents"/> and <see cref="AdvanceBlocker"/> are derived from the other
/// fields and the modifiers as of the house's last daily pass; they are kept so the queries need no simulation context.
/// </remarks>
[MessagePackObject]
public struct HouseRecord
{
    /// <summary>Gets or sets the tier the house is at.</summary>
    [Key(0)]
    public TierId Tier { get; set; }

    /// <summary>Gets or sets the building type, which modifier queries use to find auras and category modifiers.</summary>
    [Key(1)]
    public BuildingTypeId Type { get; set; }

    /// <summary>Gets or sets the residents living in the house; at least 1.</summary>
    [Key(2)]
    public int Residents { get; set; }

    /// <summary>Gets or sets the basic needs met at the house's last evaluation.</summary>
    [Key(3)]
    public uint NeedsMetMask { get; set; }

    /// <summary>Gets or sets the basic needs that have been met at least once at the current tier; only these can start the decline timer.</summary>
    [Key(4)]
    public uint EverMetMask { get; set; }

    /// <summary>Gets or sets the comfort goods met at the house's last evaluation.</summary>
    [Key(5)]
    public uint ComfortMetMask { get; set; }

    /// <summary>Gets or sets a value indicating whether a market covered the house at its last evaluation.</summary>
    [Key(6)]
    public bool MarketCovered { get; set; }

    /// <summary>Gets or sets the consecutive days all basic needs have been met, up to the advance threshold.</summary>
    [Key(7)]
    public int AdvanceStreakDays { get; set; }

    /// <summary>Gets or sets the days a once-met basic need has been unmet; 0 when the timer is not running.</summary>
    [Key(8)]
    public int DeclineTimerDays { get; set; }

    /// <summary>Gets or sets a value indicating whether the player has locked the house against advancing.</summary>
    [Key(9)]
    public bool UpgradeLocked { get; set; }

    /// <summary>Gets or sets the most residents the house can hold: the tier's capacity scaled by the capacity modifiers.</summary>
    [Key(10)]
    public int Capacity { get; set; }

    /// <summary>Gets or sets the occupancy the house is moving towards.</summary>
    [Key(11)]
    public int TargetResidents { get; set; }

    /// <summary>Gets or sets what stops the house from advancing.</summary>
    [Key(12)]
    public AdvanceBlocker AdvanceBlocker { get; set; }

    /// <summary>Gets or sets how many residents the last day-start pass assigned to lower-tier jobs (ADR-0016). The first houses of the tier carry the underemployment; morale and the advance block derive from this.</summary>
    [Key(13)]
    public int Underemployed { get; set; }
}
