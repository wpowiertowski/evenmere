using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries.Types;
using MessagePack;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// The modifiers service's slice of the world state: every modifier an applied effect has registered, in the order the
/// effects were applied. The effective-stat index is derived from this list and is rebuilt whenever the system is created
/// over a state (a new game, or a loaded save); the index and its cache are not serialised.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ModifiersState
{
    /// <summary>Gets or sets the registered modifiers, in the order they were applied.</summary>
    [Key(0)]
    public ActiveModifier[] Active { get; set; } = [];
}

/// <summary>What a modifier applies to. The values are part of the save format; add members at the end.</summary>
public enum ModifierTargetKind : byte
{
    /// <summary>Everything the stat is asked about: the whole island.</summary>
    Island = 0,

    /// <summary>One building type; <see cref="ActiveModifier.TargetValue"/> is its <c>BuildingTypeId</c>.</summary>
    Building = 1,

    /// <summary>Every building type that carries a tag; <see cref="ActiveModifier.TargetTag"/> is the tag.</summary>
    Tag = 2,

    /// <summary>Every building type in a category; <see cref="ActiveModifier.TargetValue"/> is the <c>BuildingCategory</c>.</summary>
    Category = 3,

    /// <summary>One population tier; <see cref="ActiveModifier.TargetValue"/> is its <c>TierId</c>.</summary>
    Tier = 4,

    /// <summary>One good; <see cref="ActiveModifier.TargetValue"/> is its <c>GoodId</c>.</summary>
    Good = 5,

    /// <summary>Discoveries of one kind; <see cref="ActiveModifier.TargetValue"/> is the <c>DiscoveryKind</c>.</summary>
    Discovery = 6,
}

/// <summary>
/// One registered modifier as it is saved: where it came from, the stat, what it targets, the percent and the optional
/// <c>when</c> condition. The condition is stored in the flat form of <c>ConditionCodec</c>, so a save never depends on the
/// content files holding the effect the modifier came from.
/// </summary>
[MessagePackObject]
public sealed class ActiveModifier
{
    /// <summary>Gets or sets the origin of the modifier, such as the tech whose effects were applied.</summary>
    [Key(0)]
    public EffectSource Source { get; set; }

    /// <summary>Gets or sets the stat the modifier changes.</summary>
    [Key(1)]
    public Stat Stat { get; set; }

    /// <summary>Gets or sets what the modifier targets.</summary>
    [Key(2)]
    public ModifierTargetKind TargetKind { get; set; }

    /// <summary>Gets or sets the target's raw ID or enum value, which <see cref="TargetKind"/> decides how to read; 0 when there is none.</summary>
    [Key(3)]
    public int TargetValue { get; set; }

    /// <summary>Gets or sets the building tag for <see cref="ModifierTargetKind.Tag"/>; otherwise <see langword="null"/>.</summary>
    [Key(4)]
    public string? TargetTag { get; set; }

    /// <summary>Gets or sets the change in percent; negative reduces.</summary>
    [Key(5)]
    public int Percent { get; set; }

    /// <summary>Gets or sets the encoded <c>when</c> condition, or <see langword="null"/> when the modifier always applies.</summary>
    [Key(6)]
    public long[]? When { get; set; }
}
