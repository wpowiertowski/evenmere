using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Effects;

/// <summary>
/// Something a tech, civic, tradition, festival, monument or happening does. In JSON an effect is an object with
/// exactly one key, the effect kind, as in <c>{ "unlockBuilding": "bakery" }</c> (architecture.md, effects vocabulary).
/// The hierarchy is closed: every kind is one of the sealed records below.
/// </summary>
public abstract record Effect
{
    private protected Effect()
    {
    }
}

/// <summary>Unlocks a building: <c>{ "unlockBuilding": "bakery" }</c>.</summary>
/// <param name="Building">The building type.</param>
public sealed record UnlockBuildingEffect(BuildingTypeId Building) : Effect;

/// <summary>Unlocks a recipe: <c>{ "unlockRecipe": "bake_bread" }</c>.</summary>
/// <param name="Recipe">The recipe.</param>
public sealed record UnlockRecipeEffect(RecipeId Recipe) : Effect;

/// <summary>Unlocks a population tier: <c>{ "unlockTier": "townsfolk" }</c>.</summary>
/// <param name="Tier">The tier.</param>
public sealed record UnlockTierEffect(TierId Tier) : Effect;

/// <summary>Unlocks a tradition card: <c>{ "unlockTradition": "common_hearth" }</c>.</summary>
/// <param name="Tradition">The tradition.</param>
public sealed record UnlockTraditionEffect(TraditionId Tradition) : Effect;

/// <summary>Unlocks a festival: <c>{ "unlockFestival": "harvest_fair" }</c>.</summary>
/// <param name="Festival">The festival.</param>
public sealed record UnlockFestivalEffect(FestivalId Festival) : Effect;

/// <summary>Opens ground: <c>{ "unlockTerrainAccess": "narrow_rivers" }</c>.</summary>
/// <param name="Access">The access flag.</param>
public sealed record UnlockTerrainAccessEffect(TerrainAccess Access) : Effect;

/// <summary>
/// Switches on a feature: <c>{ "unlockFeature": "coin" }</c>, or for one type of great work
/// <c>{ "unlockFeature": "great_works:writing" }</c>.
/// </summary>
/// <param name="Feature">The feature.</param>
/// <param name="GreatWorkType">The great-work type when <paramref name="Feature"/> is <see cref="FeatureKind.GreatWorks"/>; otherwise <see langword="null"/>.</param>
public sealed record UnlockFeatureEffect(FeatureKind Feature, GreatWorkTypeId? GreatWorkType) : Effect;

/// <summary>Grants one tradition slot of a type: <c>{ "traditionSlot": "hearth" }</c>.</summary>
/// <param name="Slot">The slot type.</param>
public sealed record TraditionSlotEffect(TraditionSlotType Slot) : Effect;

/// <summary>
/// Changes a stat by a percentage, for example
/// <c>{ "modifier": { "stat": "productivity", "target": { "tag": "farm" }, "percent": 20, "when": { "season": "autumn" } } }</c>.
/// Percentages on the same stat add before applying once.
/// </summary>
/// <param name="Stat">The stat changed.</param>
/// <param name="Target">What the modifier applies to.</param>
/// <param name="Percent">The change in percent; negative reduces.</param>
/// <param name="When">An optional condition that must hold for the modifier to apply.</param>
public sealed record ModifierEffect(Stat Stat, ModifierTarget Target, int Percent, Condition? When) : Effect;

/// <summary>Grants goods once: <c>{ "grantGoods": { "timber": 10 } }</c>.</summary>
/// <param name="Goods">The goods, sorted by good ID.</param>
public sealed record GrantGoodsEffect(ImmutableArray<GoodAmount> Goods) : Effect;

/// <summary>Grants points once: <c>{ "grantPoints": { "knowledge": 50 } }</c>.</summary>
/// <param name="Points">The points, sorted by kind.</param>
public sealed record GrantPointsEffect(ImmutableArray<PointAmount> Points) : Effect;

/// <summary>Reveals the coastline fog: <c>{ "revealCoastline": true }</c>. Only the literal <c>true</c> is valid.</summary>
public sealed record RevealCoastlineEffect : Effect;

/// <summary>
/// Makes the townsfolk ask for a festival, which then costs less if the player agrees:
/// <c>{ "requestFestival": { "festival": "harvest_fair", "discountPercent": 25 } }</c>.
/// </summary>
/// <param name="Festival">The festival requested.</param>
/// <param name="DiscountPercent">The goods discount for accepting, from 1 to 100.</param>
public sealed record RequestFestivalEffect(FestivalId Festival, int DiscountPercent) : Effect;

/// <summary>
/// What a modifier applies to. In JSON, <c>"target": { "building": "id" }</c>, <c>{ "tag": "t" }</c>,
/// <c>{ "category": "c" }</c>, <c>{ "tier": "id" }</c>, <c>{ "good": "id" }</c> or
/// <c>{ "discovery": "natural_wonder" }</c>; leaving <c>target</c> out means island-wide.
/// </summary>
public abstract record ModifierTarget
{
    private protected ModifierTarget()
    {
    }
}

/// <summary>The whole island.</summary>
public sealed record IslandTarget : ModifierTarget;

/// <summary>Buildings picked by ID, tag or category.</summary>
/// <param name="Selector">The building selector.</param>
public sealed record BuildingsTarget(BuildingSelector Selector) : ModifierTarget;

/// <summary>One population tier.</summary>
/// <param name="Tier">The tier.</param>
public sealed record TierTarget(TierId Tier) : ModifierTarget;

/// <summary>One good.</summary>
/// <param name="Good">The good.</param>
public sealed record GoodTarget(GoodId Good) : ModifierTarget;

/// <summary>Discoveries of one kind, such as natural wonders: <c>{ "discovery": "natural_wonder" }</c>.</summary>
/// <param name="Kind">The kind of discovery: <see cref="DiscoveryKind.NaturalWonder"/>, <see cref="DiscoveryKind.Ruin"/> or <see cref="DiscoveryKind.Shipwreck"/>.</param>
public sealed record DiscoveryTarget(DiscoveryKind Kind) : ModifierTarget;
