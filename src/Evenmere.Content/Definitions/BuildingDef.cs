using System.Collections.Immutable;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Definitions;

/// <summary>The size of a building in tiles before rotation.</summary>
/// <param name="Width">The width, at least 1.</param>
/// <param name="Height">The height, at least 1.</param>
public readonly record struct Footprint(int Width, int Height);

/// <summary>A crop-fertility requirement: enough of the footprint must be fertile for a crop.</summary>
/// <param name="Crop">The crop whose fertility layer matters.</param>
/// <param name="MinPercent">The least share of footprint tiles that must be fertile, in percent (1 to 100).</param>
public sealed record FertilitySpec(CropKind Crop, int MinPercent);

/// <summary>How a gatherer's yield is measured from the map around it.</summary>
/// <param name="Kind">How the yield is measured.</param>
/// <param name="Terrain">For <see cref="AreaYieldKind.Radius"/>, the terrains that count; empty otherwise.</param>
/// <param name="Radius">For <see cref="AreaYieldKind.Radius"/>, the search radius in tiles; 0 otherwise.</param>
/// <param name="FullAt">For <see cref="AreaYieldKind.Radius"/>, the number of counted tiles that gives a full yield; 0 otherwise.</param>
/// <param name="Exclusive">For <see cref="AreaYieldKind.Radius"/>, whether tiles already claimed by a lower-ID building are skipped.</param>
public sealed record AreaYieldSpec(AreaYieldKind Kind, ImmutableArray<TerrainId> Terrain, int Radius, int FullAt, bool Exclusive);

/// <summary>Where a building may stand and what it needs nearby.</summary>
/// <param name="Terrain">The terrains it may stand on; empty means any buildable terrain.</param>
/// <param name="RoadAccess">Whether it needs a road beside it to work; defaults to true.</param>
/// <param name="Coastline">Whether it must stand on the coast.</param>
/// <param name="Riverside">Whether it must stand beside a river.</param>
/// <param name="Deposit">The deposit it must stand over, if any.</param>
/// <param name="Fertility">The crop fertility it needs, if any.</param>
/// <param name="AreaYield">How its yield is measured, if it is a gatherer.</param>
public sealed record PlacementSpec(
    ImmutableArray<TerrainId> Terrain,
    bool RoadAccess,
    bool Coastline,
    bool Riverside,
    DepositKind? Deposit,
    FertilitySpec? Fertility,
    AreaYieldSpec? AreaYield)
{
    /// <summary>Gets the placement of a building that declares none: any buildable terrain, road access needed.</summary>
    public static PlacementSpec Default { get; } = new(ImmutableArray<TerrainId>.Empty, true, false, false, null, null, null);
}

/// <summary>The workers a building needs, all of one tier.</summary>
/// <param name="Tier">The tier the workers come from.</param>
/// <param name="Count">How many workers, at least 1.</param>
public sealed record WorkforceSpec(TierId Tier, int Count);

/// <summary>A building that stores goods in the island pool and sends carts.</summary>
/// <param name="Reach">How far its carts reach, in road-distance tiles.</param>
/// <param name="Carts">How many carts it owns.</param>
public sealed record StorageSpec(int Reach, int Carts);

/// <summary>A service a building provides within road range.</summary>
/// <param name="Kind">The service.</param>
/// <param name="Coverage">The coverage radius, in road-distance tiles.</param>
public sealed record ServiceSpec(ServiceKind Kind, int Coverage);

/// <summary>Display slots for great works.</summary>
/// <param name="Type">The type of work the slots take, or <see langword="null"/> for slots that take any type.</param>
/// <param name="Count">How many slots, at least 1.</param>
public sealed record SlotSpec(GreatWorkTypeId? Type, int Count);

/// <summary>Fuel a building burns each day.</summary>
/// <param name="Good">The fuel good.</param>
/// <param name="PerDay">How much it burns per day.</param>
public sealed record FuelSpec(GoodId Good, Amount PerDay);

/// <summary>
/// Where a building must stand for a <see cref="SiteBonusSpec"/> to apply. In JSON it is an object with exactly one key,
/// <c>terrain</c> or <c>near</c>. The hierarchy is closed.
/// </summary>
public abstract record SiteCondition
{
    private protected SiteCondition()
    {
    }
}

/// <summary>The building stands on one of these terrains: <c>{ "terrain": ["hills", "highlands"] }</c>.</summary>
/// <param name="Terrain">The terrains, in file order; at least one.</param>
public sealed record TerrainSite(ImmutableArray<TerrainId> Terrain) : SiteCondition;

/// <summary>
/// Another building is within a radius: <c>{ "near": { "building": "orchard", "radius": 6 } }</c> or with a
/// <c>tag</c> instead of a <c>building</c>.
/// </summary>
/// <param name="Selector">Which buildings count: a <see cref="BuildingTypeSelector"/> or a <see cref="BuildingTagSelector"/>.</param>
/// <param name="Radius">The radius in tiles, at least 1.</param>
public sealed record NearSite(BuildingSelector Selector, int Radius) : SiteCondition;

/// <summary>
/// A bonus that applies to this one building when it stands in the right place, such as a windmill on hills:
/// <c>{ "when": { "terrain": ["hills"] }, "stat": "productivity", "percent": 15 }</c>. It is building-local, not an effect.
/// </summary>
/// <param name="When">Where the building must stand.</param>
/// <param name="Stat">The stat changed on this building.</param>
/// <param name="Percent">The change in percent; negative reduces.</param>
public sealed record SiteBonusSpec(SiteCondition When, Stat Stat, int Percent);

/// <summary>
/// A modifier a building spreads over the buildings or residents within its coverage, such as a bathhouse raising house
/// capacity: <c>{ "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 16 }</c>.
/// </summary>
/// <param name="Stat">The stat changed.</param>
/// <param name="Percent">The change in percent; negative reduces.</param>
/// <param name="Target">What the aura applies to; never island-wide.</param>
/// <param name="Coverage">How far it reaches, in road-distance tiles, at least 1.</param>
public sealed record AuraSpec(Stat Stat, int Percent, ModifierTarget Target, int Coverage);

/// <summary>
/// A type of building, including houses, roads and bridges. A multi-stage monument or wonder is a building plus a
/// <see cref="MonumentDef"/> that names it.
/// </summary>
/// <param name="Id">The interned ID.</param>
/// <param name="Category">What the building is for.</param>
/// <param name="Era">The era (by <see cref="EraDef.Order"/>) the building belongs to.</param>
/// <param name="Footprint">The size in tiles before rotation.</param>
/// <param name="Placement">Where it may stand.</param>
/// <param name="Cost">What it costs to build; free when omitted.</param>
/// <param name="BuildDays">Build time in days; 0 for roads and pre-placed buildings.</param>
/// <param name="Workforce">The workers it needs, if any.</param>
/// <param name="Recipe">The recipe it runs, if it is a producer.</param>
/// <param name="Storage">Its storehouse behaviour, if it is one.</param>
/// <param name="Service">The service it provides, if any.</param>
/// <param name="Outputs">Knowledge, culture, coin and creativity it produces per day, if any.</param>
/// <param name="Slots">Great-work display slots it offers, sorted by type with "any" last.</param>
/// <param name="UpgradeOf">The building this one upgrades in place, if it is an upgrade.</param>
/// <param name="Fuel">The fuel it burns, if any.</param>
/// <param name="UpkeepCoin">Coin upkeep per day (Era II onward only); zero when none.</param>
/// <param name="Attractiveness">Attractiveness it adds; negative for heavy industry.</param>
/// <param name="Lodging">Visitors it lodges per day.</param>
/// <param name="RoadCost">For road types, the travel cost per tile in thousandths (dirt road 1000); <see langword="null"/> for non-roads.</param>
/// <param name="BridgeSpan">For bridge road types, the longest river span in tiles; <see langword="null"/> for non-bridges.</param>
/// <param name="SiteBonuses">Bonuses that apply to this building only, depending on where it stands, in file order.</param>
/// <param name="Aura">A modifier it spreads over its surroundings, if any.</param>
/// <param name="Unique">Whether only one may exist per island.</param>
/// <param name="Start">Whether it is available from the start without being unlocked.</param>
/// <param name="Tags">Free-form tags that systems and modifiers select on, in file order.</param>
public sealed record BuildingDef(
    BuildingTypeId Id,
    BuildingCategory Category,
    int Era,
    Footprint Footprint,
    PlacementSpec Placement,
    CostSpec Cost,
    int BuildDays,
    WorkforceSpec? Workforce,
    RecipeId? Recipe,
    StorageSpec? Storage,
    ServiceSpec? Service,
    OutputsSpec? Outputs,
    ImmutableArray<SlotSpec> Slots,
    BuildingTypeId? UpgradeOf,
    FuelSpec? Fuel,
    Amount UpkeepCoin,
    int Attractiveness,
    int Lodging,
    int? RoadCost,
    int? BridgeSpan,
    ImmutableArray<SiteBonusSpec> SiteBonuses,
    AuraSpec? Aura,
    bool Unique,
    bool Start,
    ImmutableArray<string> Tags);
