using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using MessagePack;

namespace Evenmere.Sim.Queries.Types;

/// <summary>What an effect or a modifier comes from. The values are part of the save format; add members at the end.</summary>
public enum EffectSourceKind : byte
{
    /// <summary>A researched technology; the value is a <see cref="TechId"/>.</summary>
    Tech = 0,

    /// <summary>An adopted civic; the value is a <see cref="CivicId"/>.</summary>
    Civic = 1,

    /// <summary>A slotted tradition; the value is a <see cref="TraditionId"/>.</summary>
    Tradition = 2,

    /// <summary>A running festival; the value is a <see cref="FestivalId"/>.</summary>
    Festival = 3,

    /// <summary>A completed monument or wonder; the value is a <see cref="MonumentId"/>.</summary>
    Monument = 4,

    /// <summary>A chosen happening option; the value is a <see cref="HappeningId"/>.</summary>
    Happening = 5,

    /// <summary>A building's own site bonus or aura; the value is the building's <see cref="EntityId"/>.</summary>
    Building = 6,
}

/// <summary>
/// The origin of a set of effects, such as the tech that was just researched. Applying effects is idempotent per source
/// (applying the same source twice changes nothing), and the explain queries name a modifier by its source.
/// </summary>
/// <param name="Kind">What kind of thing the source is.</param>
/// <param name="Value">The raw value of the source's ID, which the kind decides how to read (the factory methods and accessors do it).</param>
[MessagePackObject]
public readonly record struct EffectSource([property: Key(0)] EffectSourceKind Kind, [property: Key(1)] int Value)
{
    /// <summary>Creates the source for a technology.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForTech(TechId tech) => new(EffectSourceKind.Tech, tech.Value);

    /// <summary>Creates the source for a civic.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForCivic(CivicId civic) => new(EffectSourceKind.Civic, civic.Value);

    /// <summary>Creates the source for a tradition.</summary>
    /// <param name="tradition">The tradition.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForTradition(TraditionId tradition) => new(EffectSourceKind.Tradition, tradition.Value);

    /// <summary>Creates the source for a festival.</summary>
    /// <param name="festival">The festival.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForFestival(FestivalId festival) => new(EffectSourceKind.Festival, festival.Value);

    /// <summary>Creates the source for a monument.</summary>
    /// <param name="monument">The monument.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForMonument(MonumentId monument) => new(EffectSourceKind.Monument, monument.Value);

    /// <summary>Creates the source for a happening.</summary>
    /// <param name="happening">The happening.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForHappening(HappeningId happening) => new(EffectSourceKind.Happening, happening.Value);

    /// <summary>Creates the source for a building's own bonus or aura.</summary>
    /// <param name="building">The building.</param>
    /// <returns>The source.</returns>
    public static EffectSource ForBuilding(EntityId building) => new(EffectSourceKind.Building, building.Value);

    /// <summary>Reads the value as a tech; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Tech"/>.</summary>
    /// <returns>The tech.</returns>
    public TechId AsTech() => new(checked((ushort)Value));

    /// <summary>Reads the value as a civic; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Civic"/>.</summary>
    /// <returns>The civic.</returns>
    public CivicId AsCivic() => new(checked((ushort)Value));

    /// <summary>Reads the value as a tradition; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Tradition"/>.</summary>
    /// <returns>The tradition.</returns>
    public TraditionId AsTradition() => new(checked((ushort)Value));

    /// <summary>Reads the value as a festival; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Festival"/>.</summary>
    /// <returns>The festival.</returns>
    public FestivalId AsFestival() => new(checked((ushort)Value));

    /// <summary>Reads the value as a monument; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Monument"/>.</summary>
    /// <returns>The monument.</returns>
    public MonumentId AsMonument() => new(checked((ushort)Value));

    /// <summary>Reads the value as a happening; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Happening"/>.</summary>
    /// <returns>The happening.</returns>
    public HappeningId AsHappening() => new(checked((ushort)Value));

    /// <summary>Reads the value as a building; valid when <see cref="Kind"/> is <see cref="EffectSourceKind.Building"/>.</summary>
    /// <returns>The building's entity ID.</returns>
    public EntityId AsBuilding() => new(Value);
}

/// <summary>
/// What a modifier query is about: the thing whose stat is being asked for. Modifiers select on it (a building by ID, tag
/// or category, a tier, a good, or the whole island), and the answers are cached per building type, tier and good. A
/// default value asks about the whole island.
/// </summary>
/// <param name="BuildingType">The building type, or <see langword="null"/> when the query is not about a building.</param>
/// <param name="Building">The building, so building-local site bonuses and auras can apply; <see cref="EntityId.None"/> when the query is about a type or about no building.</param>
/// <param name="Tier">The population tier, or <see langword="null"/>.</param>
/// <param name="Good">The good, or <see langword="null"/>.</param>
/// <param name="Discovery">The discovery kind, or <see langword="null"/> when the query is not about a discovery. (M2-06, per ADR-0017.)</param>
public readonly record struct ModifierContext(BuildingTypeId? BuildingType, EntityId Building, TierId? Tier, GoodId? Good, DiscoveryKind? Discovery = null)
{
    /// <summary>Gets the context of an island-wide query.</summary>
    public static ModifierContext Island => default;

    /// <summary>Creates the context of one building.</summary>
    /// <param name="building">The building's entity ID.</param>
    /// <param name="type">The building's type.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForBuilding(EntityId building, BuildingTypeId type) => new(type, building, null, null, null);

    /// <summary>Creates the context of a building type, for queries that are not about one particular building.</summary>
    /// <param name="type">The building type.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForBuildingType(BuildingTypeId type) => new(type, EntityId.None, null, null, null);

    /// <summary>Creates the context of a population tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForTier(TierId tier) => new(null, EntityId.None, tier, null, null);

    /// <summary>Creates the context of a tier consuming or producing one good.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="good">The good.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForTierAndGood(TierId tier, GoodId good) => new(null, EntityId.None, tier, good, null);

    /// <summary>Creates the context of a good.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForGood(GoodId good) => new(null, EntityId.None, null, good, null);

    /// <summary>Creates the context of a discovery kind, for natural-wonder and similar modifiers.</summary>
    /// <param name="kind">The discovery kind.</param>
    /// <returns>The context.</returns>
    public static ModifierContext ForDiscovery(DiscoveryKind kind) => new(null, EntityId.None, null, null, kind);
}

/// <summary>One modifier that matches a stat query, for the explain queries.</summary>
/// <param name="Source">Where the modifier comes from; the view names it with the source's locale key.</param>
/// <param name="Percent">The change in percent; negative reduces.</param>
/// <param name="IsActive">Whether the modifier counts now. An inactive one (an autumn-only bonus in spring) is listed so the explanation can say why it is not applied.</param>
public readonly record struct ModifierContribution(EffectSource Source, int Percent, bool IsActive);
