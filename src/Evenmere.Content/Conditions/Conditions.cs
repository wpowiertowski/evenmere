using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Content.Conditions;

/// <summary>
/// A test on the game state, used by inspirations, happenings, advisors and tutorial steps. In JSON a condition is an
/// object with exactly one key, the condition kind, as in
/// <c>{ "produced": { "good": "grain", "atLeast": 30 } }</c>. The hierarchy is closed.
/// </summary>
public abstract record Condition
{
    private protected Condition()
    {
    }
}

/// <summary>
/// Buildings standing: <c>{ "count": { "building": "fishing_hut", "atLeast": 5 } }</c>, or by <c>tag</c> or <c>category</c>,
/// optionally only those built on a terrain: <c>{ "count": { "tag": "farm", "terrain": "hills", "atLeast": 3 } }</c>.
/// With a terrain and no selector it counts every building on that terrain.
/// </summary>
/// <param name="Selector">Which buildings count; <see langword="null"/> for every building (then <paramref name="Terrain"/> is set).</param>
/// <param name="Terrain">Only buildings on this terrain count; <see langword="null"/> for any.</param>
/// <param name="AtLeast">The number needed.</param>
public sealed record CountCondition(BuildingSelector? Selector, TerrainId? Terrain, int AtLeast) : Condition;

/// <summary>Cumulative production of a good: <c>{ "produced": { "good": "grain", "atLeast": 30 } }</c>.</summary>
/// <param name="Good">The good.</param>
/// <param name="AtLeast">The quantity needed.</param>
public sealed record ProducedCondition(GoodId Good, Amount AtLeast) : Condition;

/// <summary>Cumulative consumption of a good: <c>{ "consumed": { "good": "bread", "atLeast": 100 } }</c>.</summary>
/// <param name="Good">The good.</param>
/// <param name="AtLeast">The quantity needed.</param>
public sealed record ConsumedCondition(GoodId Good, Amount AtLeast) : Condition;

/// <summary>
/// The goods in the island pool right now (not cumulative production): <c>{ "stock": { "good": "timber", "atLeast": 40 } }</c>.
/// </summary>
/// <param name="Good">The good.</param>
/// <param name="AtLeast">The quantity needed in stock.</param>
public sealed record StockCondition(GoodId Good, Amount AtLeast) : Condition;

/// <summary>
/// Residents: <c>{ "population": { "tier": "villagers", "atLeast": 50 } }</c>, or the total with no <c>tier</c>.
/// </summary>
/// <param name="Tier">The tier counted; <see langword="null"/> for all residents.</param>
/// <param name="AtLeast">The number needed.</param>
public sealed record PopulationCondition(TierId? Tier, int AtLeast) : Condition;

/// <summary>
/// Road tiles laid: <c>{ "roadTiles": { "atLeast": 40 } }</c>, optionally of one road type:
/// <c>{ "roadTiles": { "road": "cobbled_road", "atLeast": 60 } }</c>.
/// </summary>
/// <param name="Road">The road building type counted; <see langword="null"/> for any road.</param>
/// <param name="AtLeast">The number needed.</param>
public sealed record RoadTilesCondition(BuildingTypeId? Road, int AtLeast) : Condition;

/// <summary>Expeditions completed: <c>{ "expeditions": { "atLeast": 4 } }</c>.</summary>
/// <param name="AtLeast">The number needed.</param>
public sealed record ExpeditionsCondition(int AtLeast) : Condition;

/// <summary>Festivals held: <c>{ "festivalsHeld": { "atLeast": 2 } }</c>.</summary>
/// <param name="AtLeast">The number needed.</param>
public sealed record FestivalsHeldCondition(int AtLeast) : Condition;

/// <summary>
/// Great works: <c>{ "greatWorks": { "type": "writing", "displayed": true, "atLeast": 3 } }</c>. Without <c>displayed</c>
/// it counts works created; with it, works on display.
/// </summary>
/// <param name="Type">The great-work type counted; <see langword="null"/> for any.</param>
/// <param name="Displayed">Whether only works on display count.</param>
/// <param name="AtLeast">The number needed.</param>
public sealed record GreatWorksCondition(GreatWorkTypeId? Type, bool Displayed, int AtLeast) : Condition;

/// <summary>Visitors in one day: <c>{ "visitorsPerDay": { "atLeast": 200 } }</c>.</summary>
/// <param name="AtLeast">The number needed.</param>
public sealed record VisitorsPerDayCondition(int AtLeast) : Condition;

/// <summary>A tech is researched: <c>{ "researched": "milling" }</c>.</summary>
/// <param name="Tech">The tech.</param>
public sealed record ResearchedCondition(TechId Tech) : Condition;

/// <summary>A civic is adopted: <c>{ "adopted": "kinship" }</c>.</summary>
/// <param name="Civic">The civic.</param>
public sealed record AdoptedCondition(CivicId Civic) : Condition;

/// <summary>
/// Discoveries made, either of a kind, <c>{ "discovered": { "kind": "iron_deposit", "atLeast": 1 } }</c>, or of regions
/// of a terrain, <c>{ "discovered": { "region": "marsh", "atLeast": 3 } }</c>. Exactly one of the two is set.
/// </summary>
/// <param name="Kind">The kind of discovery, or <see langword="null"/> when counting regions.</param>
/// <param name="Region">The terrain of the regions revealed, or <see langword="null"/> when counting a kind.</param>
/// <param name="AtLeast">The number needed.</param>
public sealed record DiscoveredCondition(DiscoveryKind? Kind, TerrainId? Region, int AtLeast) : Condition;

/// <summary>It is a season: <c>{ "season": "autumn" }</c>.</summary>
/// <param name="Season">The season.</param>
public sealed record SeasonCondition(Season Season) : Condition;

/// <summary>The era has begun: <c>{ "era": "township" }</c>.</summary>
/// <param name="Era">The era.</param>
public sealed record EraCondition(EraId Era) : Condition;

/// <summary>Every sub-condition holds: <c>{ "all": [ ... ] }</c>.</summary>
/// <param name="Conditions">The sub-conditions; at least one.</param>
public sealed record AllCondition(ImmutableArray<Condition> Conditions) : Condition;

/// <summary>At least one sub-condition holds: <c>{ "any": [ ... ] }</c>.</summary>
/// <param name="Conditions">The sub-conditions; at least one.</param>
public sealed record AnyCondition(ImmutableArray<Condition> Conditions) : Condition;

/// <summary>The sub-condition does not hold: <c>{ "not": { ... } }</c>.</summary>
/// <param name="Condition">The negated condition.</param>
public sealed record NotCondition(Condition Condition) : Condition;
