using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Content.Definitions;

/// <summary>A good: a resource, material or product.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Category">What kind of material it is.</param>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) in which it first appears.</param>
/// <param name="Tags">Free-form tags, in file order.</param>
public sealed record GoodDef(GoodId Id, GoodCategory Category, int Era, ImmutableArray<string> Tags);

/// <summary>A production recipe: inputs to outputs over a cycle. The building supplies the workforce.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Inputs">Goods consumed per cycle, sorted by good ID; empty for gatherers.</param>
/// <param name="Outputs">Goods produced per cycle, sorted by good ID; at least one.</param>
/// <param name="CycleSeconds">Game seconds per cycle at 100% productivity.</param>
/// <param name="Start">Whether it is available from the start without being unlocked.</param>
public sealed record RecipeDef(RecipeId Id, ImmutableArray<GoodAmount> Inputs, ImmutableArray<GoodAmount> Outputs, int CycleSeconds, bool Start);

/// <summary>A population tier. Houses of one tier upgrade in place to the next.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Order">The tier's rank, 1 for the lowest; unique across tiers.</param>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) the tier belongs to.</param>
/// <param name="Capacity">Residents per house.</param>
/// <param name="AdvanceFrom">The tier a house advances from to reach this one; <see langword="null"/> for the lowest tier.</param>
/// <param name="UpgradeCost">The cost of advancing a house into this tier.</param>
/// <param name="BasicGoods">Basic-need goods per 1,000 residents per day, sorted by good ID.</param>
/// <param name="BasicServices">Basic-need services, sorted by service.</param>
/// <param name="ComfortGoods">Comfort-need goods per 1,000 residents per day, sorted by good ID.</param>
/// <param name="Outputs">Knowledge, culture, coin and creativity per 1,000 residents per day.</param>
/// <param name="Start">Whether it is available from the start without being unlocked.</param>
public sealed record TierDef(
    TierId Id,
    int Order,
    int Era,
    int Capacity,
    TierId? AdvanceFrom,
    CostSpec UpgradeCost,
    ImmutableArray<GoodAmount> BasicGoods,
    ImmutableArray<ServiceKind> BasicServices,
    ImmutableArray<GoodAmount> ComfortGoods,
    OutputsSpec Outputs,
    bool Start);

/// <summary>Makes a node repeatable: after completion it can be researched again at a growing cost.</summary>
/// <param name="CostGrowthPercent">How much the cost grows per level, in percent of the base cost.</param>
public sealed record RepeatableSpec(int CostGrowthPercent);

/// <summary>The shape a tech and a civic share: a node in a research tree.</summary>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) the node belongs to.</param>
/// <param name="Cost">The cost in knowledge (techs) or culture (civics) points.</param>
/// <param name="Effects">What completing the node does; the only source of unlocks.</param>
/// <param name="Inspiration">The condition that discounts the node's cost, if it has one.</param>
/// <param name="Gateway">Whether completing it is one half of an era transition.</param>
/// <param name="Repeatable">Repeat settings, if the node can be researched again.</param>
public abstract record NodeDef(
    int Era,
    Amount Cost,
    ImmutableArray<Effect> Effects,
    Condition? Inspiration,
    bool Gateway,
    RepeatableSpec? Repeatable);

/// <summary>A technology, researched with knowledge points.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Requires">The techs that must be researched first, in file order.</param>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) the tech belongs to.</param>
/// <param name="Cost">The cost in knowledge points.</param>
/// <param name="Effects">What researching it does.</param>
/// <param name="Inspiration">The condition that discounts its cost, if any.</param>
/// <param name="Gateway">Whether it is a tech gateway into the next era.</param>
/// <param name="Repeatable">Repeat settings, if it can be researched again.</param>
public sealed record TechDef(
    TechId Id,
    ImmutableArray<TechId> Requires,
    int Era,
    Amount Cost,
    ImmutableArray<Effect> Effects,
    Condition? Inspiration,
    bool Gateway,
    RepeatableSpec? Repeatable)
    : NodeDef(Era, Cost, Effects, Inspiration, Gateway, Repeatable);

/// <summary>A civic, adopted with culture points.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Requires">The civics that must be adopted first, in file order.</param>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) the civic belongs to.</param>
/// <param name="Cost">The cost in culture points.</param>
/// <param name="Effects">What adopting it does.</param>
/// <param name="Inspiration">The condition that discounts its cost, if any.</param>
/// <param name="Gateway">Whether it is a civic gateway into the next era.</param>
/// <param name="Repeatable">Repeat settings, if it can be adopted again.</param>
public sealed record CivicDef(
    CivicId Id,
    ImmutableArray<CivicId> Requires,
    int Era,
    Amount Cost,
    ImmutableArray<Effect> Effects,
    Condition? Inspiration,
    bool Gateway,
    RepeatableSpec? Repeatable)
    : NodeDef(Era, Cost, Effects, Inspiration, Gateway, Repeatable);

/// <summary>A tradition card, placed in a typed slot.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Slot">The slot type that takes the card.</param>
/// <param name="Effects">The effects while slotted.</param>
/// <param name="Tags">Free-form tags; style cards carry <c>style</c>.</param>
/// <param name="Style">For style cards, the key the view maps to a mesh variant set; otherwise <see langword="null"/>.</param>
/// <param name="Start">Whether it is available from the start without being unlocked.</param>
public sealed record TraditionDef(
    TraditionId Id,
    TraditionSlotType Slot,
    ImmutableArray<Effect> Effects,
    ImmutableArray<string> Tags,
    string? Style,
    bool Start);

/// <summary>An extra culture percentage a festival earns in one season.</summary>
/// <param name="Season">The season.</param>
/// <param name="Percent">The bonus in percent, for example 50 for culture times 1.5.</param>
public sealed record SeasonBonus(Season Season, int Percent);

/// <summary>A festival, held at a festival ground.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Seasons">The seasons it can be held in, sorted; empty means any season.</param>
/// <param name="Cost">What it costs to hold.</param>
/// <param name="Days">How many days it runs.</param>
/// <param name="CulturePerDay">Culture it yields per day while running.</param>
/// <param name="SeasonBonus">A culture bonus in one season, if any.</param>
/// <param name="Effects">Effects (typically modifiers) active while it runs.</param>
/// <param name="Start">Whether it is available from the start without being unlocked.</param>
public sealed record FestivalDef(
    FestivalId Id,
    ImmutableArray<Season> Seasons,
    CostSpec Cost,
    int Days,
    Amount CulturePerDay,
    SeasonBonus? SeasonBonus,
    ImmutableArray<Effect> Effects,
    bool Start);

/// <summary>A type of great work: its slot type, how works are created and what a displayed work yields.</summary>
/// <param name="Id">The interned ID; it also names the slot type.</param>
/// <param name="Source">Whether works come from creativity or from discovery.</param>
/// <param name="BaseCreativity">For creativity types, the creativity the first work needs; zero otherwise.</param>
/// <param name="GrowthPercent">For creativity types, how much more each later work needs than the last, in percent; zero otherwise.</param>
/// <param name="CultureByEra">Culture per day a displayed work yields, by the era it was made in (index 0 is era 1).</param>
/// <param name="Attractiveness">Attractiveness a displayed work adds.</param>
public sealed record GreatWorkTypeDef(
    GreatWorkTypeId Id,
    GreatWorkSource Source,
    Amount BaseCreativity,
    int GrowthPercent,
    ImmutableArray<Amount> CultureByEra,
    int Attractiveness);

/// <summary>
/// A multi-stage monument or wonder. The building it names supplies the footprint, placement and unlock; this adds the
/// stage bills and the effects that apply when the last stage completes.
/// </summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Building">The building (category monument or wonder) that is built.</param>
/// <param name="Stages">The bill for each stage, in build order; at least one.</param>
/// <param name="Effects">The effects that start when the last stage completes.</param>
public sealed record MonumentDef(MonumentId Id, BuildingTypeId Building, ImmutableArray<CostSpec> Stages, ImmutableArray<Effect> Effects);

/// <summary>One choice in a happening.</summary>
/// <param name="Effects">What choosing it does; never harsh.</param>
/// <param name="DurationDays">For modifiers, how many days they last; 0 means they are permanent.</param>
/// <param name="Label">The locale key of the option's button label, or <see langword="null"/> when the dialog shows only the effect preview.</param>
public sealed record HappeningOption(ImmutableArray<Effect> Effects, int DurationDays, string? Label = null);

/// <summary>A gentle world event with choices.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Trigger">The condition that makes the happening eligible.</param>
/// <param name="ChancePerMille">The chance per day, in per-mille, that an eligible happening occurs.</param>
/// <param name="CooldownDays">Days before it can occur again.</param>
/// <param name="Options">The choices, at least one.</param>
/// <param name="DefaultOption">The index of the option applied if the player does not choose.</param>
public sealed record HappeningDef(
    HappeningId Id,
    Condition Trigger,
    int ChancePerMille,
    int CooldownDays,
    ImmutableArray<HappeningOption> Options,
    int DefaultOption);

/// <summary>An era. Era N+1 begins when the tech and civic gateways of era N are complete.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Order">The era's number, 1 for the first; unique across eras. Items name an era by this number.</param>
/// <param name="RequiresTech">The tech gateway that must be complete; <see langword="null"/> for the first era.</param>
/// <param name="RequiresCivic">The civic gateway that must be complete; <see langword="null"/> for the first era.</param>
public sealed record EraDef(EraId Id, int Order, TechId? RequiresTech, CivicId? RequiresCivic);

/// <summary>A terrain type.</summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Buildable">Whether buildings may stand on it.</param>
/// <param name="BlocksRoads">Whether roads cannot cross it (rivers need bridges).</param>
/// <param name="Access">The access flag research must unlock before the terrain can be used; <see langword="null"/> when always usable.</param>
public sealed record TerrainDef(TerrainId Id, bool Buildable, bool BlocksRoads, TerrainAccess? Access);

/// <summary>The productivity multiplier of each season for buildings that carry one tag.</summary>
/// <param name="Tag">The building tag the curve applies to, such as <c>seasonal</c>.</param>
/// <param name="Spring">The multiplier in spring.</param>
/// <param name="Summer">The multiplier in summer.</param>
/// <param name="Autumn">The multiplier in autumn.</param>
/// <param name="Winter">The multiplier in winter.</param>
public sealed record SeasonCurve(string Tag, Ratio Spring, Ratio Summer, Ratio Autumn, Ratio Winter)
{
    /// <summary>Gets the multiplier for a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The multiplier.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="season"/> is not a defined season.</exception>
    public Ratio Of(Season season) => season switch
    {
        Season.Spring => Spring,
        Season.Summer => Summer,
        Season.Autumn => Autumn,
        Season.Winter => Winter,
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Not a season."),
    };
}

/// <summary>
/// What one expedition discovery grants when its fog lifts (M2-06): ruins pay knowledge now (a Relic great work
/// from M3-03), shipwrecks pay goods, natural wonders pay nothing now (they register for Overlook bonuses in M3-02).
/// </summary>
/// <param name="RuinKnowledge">The knowledge a ruin grants; zero for none.</param>
/// <param name="ShipwreckGoods">The goods a shipwreck grants, sorted by good ID, each above zero; empty for none.</param>
public sealed record DiscoveryRewards(Amount RuinKnowledge, ImmutableArray<GoodAmount> ShipwreckGoods);

/// <summary>
/// The game-wide tunables in <c>content/rules/core.json</c>. Systems read tunables from here and never from constants.
/// Road costs are not here: roads are buildings with a <see cref="BuildingDef.RoadCost"/>.
/// </summary>
/// <param name="CartTicksPerTile">Ticks a cart takes to cross one tile of road cost 1000.</param>
/// <param name="OutputBuffer">The most a producer's output buffer holds, per good.</param>
/// <param name="CartLoad">The amount one cart carries; a pickup is requested when an output buffer holds a load.</param>
/// <param name="DeclineGraceDays">Days a basic need may stay unmet before a house steps down a tier.</param>
/// <param name="AdvanceStreakDays">Consecutive days all basic needs must be met before a house advances.</param>
/// <param name="InspirationDiscount">The share of a node's full cost an inspiration removes.</param>
/// <param name="MarketGating">Whether a house can draw goods only inside a market's coverage.</param>
/// <param name="Seasons">Season curves per building tag, sorted by tag; empty when seasons are not configured.</param>
/// <param name="WorkforceReservePercent">The spare-worker reserve the workforce hold keeps per tier, in percent: a house advances only if the tier's remaining workers still cover demand, planned demand and this share of both (ADR-0016).</param>
/// <param name="Discoveries">What expedition discoveries grant when their fog lifts, by kind; empty rewards when the rules name none. (M2-06.)</param>
public sealed record RulesDef(
    int CartTicksPerTile,
    Amount OutputBuffer,
    Amount CartLoad,
    int DeclineGraceDays,
    int AdvanceStreakDays,
    Ratio InspirationDiscount,
    bool MarketGating,
    ImmutableArray<SeasonCurve> Seasons,
    int WorkforceReservePercent = 20,
    DiscoveryRewards? Discoveries = null);
