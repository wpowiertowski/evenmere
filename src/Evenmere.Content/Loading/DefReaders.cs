using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Content.Loading;

/// <summary>
/// One reader per content type. Each takes the open JSON object of an item and builds its definition, resolving
/// references through the intern tables. A reader keeps going after an error so one run reports every problem; the
/// caller drops the item if its <see cref="ItemReader.ErrorCount"/> grew.
/// </summary>
internal static class DefReaders
{
    private const int MaxCount = 1_000_000;
    private const int MaxPoints = 1_000_000_000;

    public static GoodDef ReadGood(ObjectView o, GoodId id, string name)
    {
        if (name == CostKeys.Coin)
        {
            o.Reader.Error("/id", $"'{CostKeys.Coin}' is reserved for coin in cost objects and cannot be a good");
        }

        GoodCategory category = o.ReqEnum<GoodCategory>("category");
        int era = o.ReqEra();
        return new GoodDef(id, category, era, o.Tags());
    }

    public static BuildingDef ReadBuilding(ObjectView o, BuildingTypeId id)
    {
        ItemReader r = o.Reader;
        ResolveTables t = r.Tables;

        BuildingCategory category = o.ReqEnum<BuildingCategory>("category");
        int era = o.ReqEra();
        Footprint footprint = ReadFootprint(o);
        PlacementSpec placement = ReadPlacement(o);
        CostSpec cost = o.Cost("cost");
        int buildDays = o.ReqInt("buildDays", 0, 100_000);
        WorkforceSpec? workforce = ReadWorkforce(o);
        RecipeId? recipe = o.OptRef(t.Recipes, "recipe");
        StorageSpec? storage = ReadStorage(o);
        ServiceSpec? service = ReadService(o);
        OutputsSpec? outputs = ReadOutputs(o, "outputs");
        ImmutableArray<SlotSpec> slots = ReadSlots(o);
        BuildingTypeId? upgradeOf = o.OptRef(t.Buildings, "upgradeOf");
        if (upgradeOf == id)
        {
            r.Error(o.Child("upgradeOf"), "a building cannot be an upgrade of itself");
        }

        FuelSpec? fuel = ReadFuel(o);
        Amount upkeepCoin = o.OptAmount("upkeepCoin", QuantityRange.NonNegative);
        int attractiveness = o.OptInt("attractiveness", 0, -1000, 1000);
        int lodging = o.OptInt("lodging", 0, 0, MaxCount);
        int? roadCost = o.OptNullableInt("roadCost", 1, MaxCount);
        int? bridgeSpan = o.OptNullableInt("bridgeSpan", 1, 64);
        if (bridgeSpan.HasValue && !roadCost.HasValue)
        {
            r.Error(o.Child("bridgeSpan"), "a bridge is a road type, so it also needs 'roadCost'");
        }

        ImmutableArray<SiteBonusSpec> siteBonuses = ReadSiteBonuses(o);
        AuraSpec? aura = ReadAura(o);
        bool unique = o.OptBool("unique", false);
        bool start = o.OptBool("start", false);
        ImmutableArray<string> tags = o.Tags();

        return new BuildingDef(
            id, category, era, footprint, placement, cost, buildDays, workforce, recipe, storage, service, outputs, slots,
            upgradeOf, fuel, upkeepCoin, attractiveness, lodging, roadCost, bridgeSpan, siteBonuses, aura, unique, start, tags);
    }

    public static RecipeDef ReadRecipe(ObjectView o, RecipeId id)
    {
        ImmutableArray<GoodAmount> inputs = o.GoodMap("inputs", required: false);
        int errorsBefore = o.Reader.ErrorCount;
        ImmutableArray<GoodAmount> outputs = o.GoodMap("outputs", required: true);
        if (outputs.IsEmpty && o.Reader.ErrorCount == errorsBefore)
        {
            o.Reader.Error(o.Child("outputs"), "a recipe must produce at least one good");
        }

        int cycleSeconds = o.ReqInt("cycleSeconds", 1, 86_400);
        bool start = o.OptBool("start", false);
        return new RecipeDef(id, inputs, outputs, cycleSeconds, start);
    }

    public static TierDef ReadTier(ObjectView o, TierId id)
    {
        ItemReader r = o.Reader;
        int order = o.ReqInt("order", 1, 64);
        int era = o.ReqEra();
        int capacity = o.ReqInt("capacity", 1, 1000);
        TierId? advanceFrom = o.OptRef(r.Tables.Tiers, "advanceFrom");
        if (advanceFrom == id)
        {
            r.Error(o.Child("advanceFrom"), "a tier cannot advance from itself");
        }

        CostSpec upgradeCost = o.Cost("upgradeCost");

        ImmutableArray<GoodAmount> basicGoods = [];
        ImmutableArray<ServiceKind> basicServices = [];
        using (ObjectView? basic = o.OptObject("basicNeeds"))
        {
            if (basic is not null)
            {
                basicGoods = basic.GoodMap("goods", required: false);
                basicServices = basic.EnumList<ServiceKind>("services");
            }
        }

        ImmutableArray<GoodAmount> comfortGoods = o.GoodMap("comfortNeeds", required: false);
        OutputsSpec outputs = ReadOutputs(o, "outputs") ?? OutputsSpec.None;
        bool start = o.OptBool("start", false);
        return new TierDef(id, order, era, capacity, advanceFrom, upgradeCost, basicGoods, basicServices, comfortGoods, outputs, start);
    }

    public static TechDef ReadTech(ObjectView o, TechId id)
    {
        ImmutableArray<TechId> requires = o.RefList(o.Reader.Tables.Techs, "requires");
        if (requires.Contains(id))
        {
            o.Reader.Error(o.Child("requires"), "a tech cannot require itself");
        }

        ReadNodeCommon(o, out int era, out Amount cost, out ImmutableArray<Effect> effects, out Condition? inspiration, out bool gateway, out RepeatableSpec? repeatable);
        return new TechDef(id, requires, era, cost, effects, inspiration, gateway, repeatable);
    }

    public static CivicDef ReadCivic(ObjectView o, CivicId id)
    {
        ImmutableArray<CivicId> requires = o.RefList(o.Reader.Tables.Civics, "requires");
        if (requires.Contains(id))
        {
            o.Reader.Error(o.Child("requires"), "a civic cannot require itself");
        }

        ReadNodeCommon(o, out int era, out Amount cost, out ImmutableArray<Effect> effects, out Condition? inspiration, out bool gateway, out RepeatableSpec? repeatable);
        return new CivicDef(id, requires, era, cost, effects, inspiration, gateway, repeatable);
    }

    public static TraditionDef ReadTradition(ObjectView o, TraditionId id)
    {
        TraditionSlotType slot = o.ReqEnum<TraditionSlotType>("slot");
        ImmutableArray<Effect> effects = o.Effects();
        ImmutableArray<string> tags = o.Tags();
        string? style = o.OptString("style");
        if (style is not null && !Ids.IsValid(style))
        {
            o.Reader.Error(o.Child("style"), $"'{style}' is not a valid style key; use snake_case");
        }

        bool start = o.OptBool("start", false);
        return new TraditionDef(id, slot, effects, tags, style, start);
    }

    public static FestivalDef ReadFestival(ObjectView o, FestivalId id)
    {
        ImmutableArray<Season> seasons = o.EnumList<Season>("seasons");
        CostSpec cost = o.Cost("cost", allowFree: false);
        int days = o.ReqInt("days", 1, 365);
        Amount culturePerDay = o.ReqAmount("culturePerDay", QuantityRange.NonNegative);

        SeasonBonus? seasonBonus = null;
        using (ObjectView? bonus = o.OptObject("seasonBonus"))
        {
            if (bonus is not null)
            {
                Season season = bonus.ReqEnum<Season>("season");
                seasonBonus = new SeasonBonus(season, bonus.ReqInt("percent", -100, 1000));
            }
        }

        ImmutableArray<Effect> effects = o.Effects();
        bool start = o.OptBool("start", false);
        return new FestivalDef(id, seasons, cost, days, culturePerDay, seasonBonus, effects, start);
    }

    public static GreatWorkTypeDef ReadGreatWorkType(ObjectView o, GreatWorkTypeId id)
    {
        GreatWorkSource source = o.ReqEnum<GreatWorkSource>("source");
        Amount baseCreativity = Amount.Zero;
        int growthPercent = 0;
        if (source == GreatWorkSource.Creativity)
        {
            baseCreativity = o.ReqAmount("baseCreativity", QuantityRange.Positive);
            growthPercent = o.ReqInt("growthPercent", 0, 1000);
        }
        else
        {
            foreach (string name in new[] { "baseCreativity", "growthPercent" })
            {
                if (o.TryGet(name, out _))
                {
                    o.Reader.Error(o.Child(name), $"'{name}' applies only to great works whose source is 'creativity'");
                }
            }
        }

        ImmutableArray<Amount>.Builder cultureByEra = ImmutableArray.CreateBuilder<Amount>();
        if (o.Require("cultureByEra", out JsonElement array))
        {
            int errorsBefore = o.Reader.ErrorCount;
            o.ForEach(array, o.Child("cultureByEra"), (element, pointer) =>
            {
                if (o.Reader.TryAmount(element, pointer, QuantityRange.NonNegative, out Amount amount))
                {
                    cultureByEra.Add(amount);
                }
            });

            if (cultureByEra.Count == 0 && o.Reader.ErrorCount == errorsBefore)
            {
                o.Reader.Error(o.Child("cultureByEra"), "list the culture per day for at least era 1");
            }
        }

        int attractiveness = o.OptInt("attractiveness", 0, -1000, 1000);
        return new GreatWorkTypeDef(id, source, baseCreativity, growthPercent, cultureByEra.ToImmutable(), attractiveness);
    }

    public static MonumentDef ReadMonument(ObjectView o, MonumentId id)
    {
        BuildingTypeId building = o.ReqRef(o.Reader.Tables.Buildings, "building");

        ImmutableArray<CostSpec>.Builder stages = ImmutableArray.CreateBuilder<CostSpec>();
        if (o.Require("stages", out JsonElement array))
        {
            int errorsBefore = o.Reader.ErrorCount;
            o.ForEach(array, o.Child("stages"), (element, pointer) => stages.Add(o.Reader.ReadCostObject(element, pointer, allowFree: false)));
            if (stages.Count == 0 && o.Reader.ErrorCount == errorsBefore)
            {
                o.Reader.Error(o.Child("stages"), "a monument needs at least one stage");
            }
        }

        return new MonumentDef(id, building, stages.ToImmutable(), o.Effects());
    }

    public static HappeningDef ReadHappening(ObjectView o, HappeningId id)
    {
        Condition? trigger = o.ReqCondition("trigger");
        int chance = o.ReqInt("chancePerMille", 1, 1000);
        int cooldown = o.ReqInt("cooldownDays", 0, 3650);

        ImmutableArray<HappeningOption>.Builder options = ImmutableArray.CreateBuilder<HappeningOption>();
        if (o.Require("options", out JsonElement array))
        {
            int errorsBefore = o.Reader.ErrorCount;
            o.ForEach(array, o.Child("options"), (element, pointer) =>
            {
                using ObjectView? option = o.Reader.OpenObject(element, pointer);
                if (option is not null)
                {
                    options.Add(new HappeningOption(option.Effects(), option.OptInt("durationDays", 0, 0, 3650)));
                }
            });

            if (options.Count == 0 && o.Reader.ErrorCount == errorsBefore)
            {
                o.Reader.Error(o.Child("options"), "a happening needs at least one option");
            }
        }

        int defaultOption = o.OptInt("defaultOption", 0, 0, 1000);
        if (o.TryGet("defaultOption", out _) && defaultOption >= options.Count && options.Count > 0)
        {
            o.Reader.Error(o.Child("defaultOption"), $"there is no option {defaultOption}; the happening has {options.Count}");
        }

        // A missing or invalid trigger has already been reported, so the placeholder never reaches a ContentDb.
        Condition checkedTrigger = trigger ?? new AllCondition(ImmutableArray<Condition>.Empty);
        return new HappeningDef(id, checkedTrigger, chance, cooldown, options.ToImmutable(), defaultOption);
    }

    public static EraDef ReadEra(ObjectView o, EraId id)
    {
        ItemReader r = o.Reader;
        int order = o.ReqInt("order", 1, 64);
        TechId? tech = null;
        CivicId? civic = null;
        using (ObjectView? requires = o.OptObject("requires"))
        {
            if (requires is not null)
            {
                tech = requires.ReqRef(r.Tables.Techs, "tech");
                civic = requires.ReqRef(r.Tables.Civics, "civic");
            }
        }

        return new EraDef(id, order, tech, civic);
    }

    public static TerrainDef ReadTerrain(ObjectView o, TerrainId id)
    {
        bool buildable = o.ReqBool("buildable");
        bool blocksRoads = o.OptBool("blocksRoads", false);
        TerrainAccess? access = o.OptEnum<TerrainAccess>("access");
        return new TerrainDef(id, buildable, blocksRoads, access);
    }

    public static RulesDef ReadRules(ObjectView o)
    {
        int cartTicksPerTile = o.ReqInt("cartTicksPerTile", 1, 1000);
        Amount outputBuffer = o.ReqAmount("outputBuffer", QuantityRange.Positive);
        Amount cartLoad = o.ReqAmount("cartLoad", QuantityRange.Positive);
        if (cartLoad > outputBuffer)
        {
            o.Reader.Error(o.Child("cartLoad"), "a cart load larger than the output buffer could never be picked up");
        }

        int declineGraceDays = o.ReqInt("declineGraceDays", 1, 3650);
        int advanceStreakDays = o.ReqInt("advanceStreakDays", 1, 3650);
        var inspirationDiscount = Ratio.FromPerMille(o.ReqInt("inspirationDiscountPerMille", 0, 1000));
        bool marketGating = o.ReqBool("marketGating");

        var seasons = new List<SeasonCurve>();
        using (ObjectView? curves = o.OptObject("seasons"))
        {
            if (curves is not null)
            {
                ReadSeasonCurves(curves, seasons);
            }
        }

        seasons.Sort(static (a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        return new RulesDef(cartTicksPerTile, outputBuffer, cartLoad, declineGraceDays, advanceStreakDays, inspirationDiscount, marketGating, [.. seasons]);
    }

    private static void ReadSeasonCurves(ObjectView curves, List<SeasonCurve> seasons)
    {
        foreach (JsonProperty property in curves.Properties())
        {
            string tag = property.Name;
            string pointer = curves.Child(tag);
            if (!Ids.IsValid(tag))
            {
                curves.Reader.Error(pointer, $"'{tag}' is not a valid tag; use snake_case");
                continue;
            }

            using ObjectView? curve = curves.Reader.OpenObject(property.Value, pointer);
            if (curve is not null)
            {
                seasons.Add(new SeasonCurve(
                    tag,
                    Ratio.FromPerMille(curve.ReqInt("spring", 0, 10_000)),
                    Ratio.FromPerMille(curve.ReqInt("summer", 0, 10_000)),
                    Ratio.FromPerMille(curve.ReqInt("autumn", 0, 10_000)),
                    Ratio.FromPerMille(curve.ReqInt("winter", 0, 10_000))));
            }
        }
    }

    private static void ReadNodeCommon(
        ObjectView o,
        out int era,
        out Amount cost,
        out ImmutableArray<Effect> effects,
        out Condition? inspiration,
        out bool gateway,
        out RepeatableSpec? repeatable)
    {
        era = o.ReqEra();

        // Node costs are whole points; the ledger holds them as Amounts.
        cost = Amount.FromUnits(o.ReqInt("cost", 1, MaxPoints));
        effects = o.Effects();

        inspiration = null;
        using (ObjectView? inspirationObject = o.OptObject("inspiration"))
        {
            if (inspirationObject is not null)
            {
                inspiration = inspirationObject.ReqCondition("when");
            }
        }

        gateway = o.OptBool("gateway", false);

        repeatable = null;
        using (ObjectView? repeatableObject = o.OptObject("repeatable"))
        {
            if (repeatableObject is not null)
            {
                repeatable = new RepeatableSpec(repeatableObject.ReqInt("costGrowthPercent", 1, 1000));
            }
        }
    }

    private static Footprint ReadFootprint(ObjectView o)
    {
        ItemReader r = o.Reader;
        if (!o.Require("footprint", out JsonElement array))
        {
            return new Footprint(1, 1);
        }

        string pointer = o.Child("footprint");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 2)
        {
            r.Error(pointer, "expected [width, height], an array of two integers");
            return new Footprint(1, 1);
        }

        int width = 1;
        int height = 1;
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (r.TryInt(element, JsonPointer.Append(pointer, index), 1, 64, out int value))
            {
                if (index == 0)
                {
                    width = value;
                }
                else
                {
                    height = value;
                }
            }

            index++;
        }

        return new Footprint(width, height);
    }

    private static PlacementSpec ReadPlacement(ObjectView o)
    {
        ItemReader r = o.Reader;
        using ObjectView? p = o.OptObject("placement");
        if (p is null)
        {
            return PlacementSpec.Default;
        }

        ImmutableArray<TerrainId> terrain = p.RefList(r.Tables.Terrains, "terrain");
        bool roadAccess = p.OptBool("roadAccess", true);
        bool coastline = p.OptBool("coastline", false);
        bool riverside = p.OptBool("riverside", false);
        DepositKind? deposit = p.OptEnum<DepositKind>("deposit");

        FertilitySpec? fertility = null;
        using (ObjectView? f = p.OptObject("fertility"))
        {
            if (f is not null)
            {
                CropKind crop = f.ReqEnum<CropKind>("crop");
                fertility = new FertilitySpec(crop, f.ReqInt("minPercent", 1, 100));
            }
        }

        AreaYieldSpec? areaYield = null;
        using (ObjectView? a = p.OptObject("areaYield"))
        {
            if (a is not null)
            {
                areaYield = ReadAreaYield(a, deposit, fertility);
            }
        }

        return new PlacementSpec(terrain, roadAccess, coastline, riverside, deposit, fertility, areaYield);
    }

    private static AreaYieldSpec ReadAreaYield(ObjectView a, DepositKind? deposit, FertilitySpec? fertility)
    {
        ItemReader r = a.Reader;
        AreaYieldKind kind = a.ReqEnum<AreaYieldKind>("kind");
        switch (kind)
        {
            case AreaYieldKind.Radius:
                int errorsBefore = r.ErrorCount;
                ImmutableArray<TerrainId> terrain = a.RefList(r.Tables.Terrains, "terrain");
                if (terrain.IsEmpty && r.ErrorCount == errorsBefore)
                {
                    r.Error(a.Pointer, "a radius yield needs 'terrain' listing at least one terrain");
                }

                int radius = a.ReqInt("radius", 1, 64);
                int fullAt = a.ReqInt("fullAt", 1, 10_000);
                bool exclusive = a.OptBool("exclusive", false);
                return new AreaYieldSpec(kind, terrain, radius, fullAt, exclusive);
            case AreaYieldKind.FootprintFertility when fertility is null:
                r.Error(a.Pointer, "a footprint_fertility yield needs placement.fertility");
                break;
            case AreaYieldKind.FootprintDeposit when deposit is null:
                r.Error(a.Pointer, "a footprint_deposit yield needs placement.deposit");
                break;
            default:
                break;
        }

        return new AreaYieldSpec(kind, [], 0, 0, false);
    }

    private static WorkforceSpec? ReadWorkforce(ObjectView o)
    {
        using ObjectView? w = o.OptObject("workforce");
        return w is null ? null : new WorkforceSpec(w.ReqRef(o.Reader.Tables.Tiers, "tier"), w.ReqInt("count", 1, MaxCount));
    }

    private static StorageSpec? ReadStorage(ObjectView o)
    {
        using ObjectView? s = o.OptObject("storage");
        return s is null ? null : new StorageSpec(s.ReqInt("reach", 1, 1000), s.ReqInt("carts", 1, 1000));
    }

    private static ServiceSpec? ReadService(ObjectView o)
    {
        using ObjectView? s = o.OptObject("service");
        return s is null ? null : new ServiceSpec(s.ReqEnum<ServiceKind>("kind"), s.ReqInt("coverage", 1, 1000));
    }

    private static FuelSpec? ReadFuel(ObjectView o)
    {
        using ObjectView? f = o.OptObject("fuel");
        return f is null ? null : new FuelSpec(f.ReqRef(o.Reader.Tables.Goods, "good"), f.ReqAmount("perDay", QuantityRange.Positive));
    }

    /// <summary>Reads an <c>outputs</c> object: knowledge, culture, coin and a creativity map keyed by great-work type.</summary>
    private static OutputsSpec? ReadOutputs(ObjectView parent, string name)
    {
        using ObjectView? o = parent.OptObject(name);
        if (o is null)
        {
            return null;
        }

        Amount knowledge = o.OptAmount("knowledge", QuantityRange.NonNegative);
        Amount culture = o.OptAmount("culture", QuantityRange.NonNegative);
        Amount coin = o.OptAmount("coin", QuantityRange.NonNegative);

        var creativity = new List<CreativityAmount>();
        if (o.TryGet("creativity", out JsonElement map))
        {
            string pointer = o.Child("creativity");
            if (map.ValueKind != JsonValueKind.Object)
            {
                o.Reader.Error(pointer, $"expected an object of great-work type to creativity but found {ItemReader.Describe(map)}");
            }
            else
            {
                foreach (JsonProperty property in map.EnumerateObject())
                {
                    string at = JsonPointer.Append(pointer, property.Name);
                    if (o.Reader.TryRefName(property.Name, at, o.Reader.Tables.GreatWorks, out GreatWorkTypeId type)
                        && o.Reader.TryAmount(property.Value, at, QuantityRange.Positive, out Amount amount))
                    {
                        creativity.Add(new CreativityAmount(type, amount));
                    }
                }
            }
        }

        creativity.Sort(static (a, b) => a.Type.Value.CompareTo(b.Type.Value));
        return new OutputsSpec(knowledge, culture, coin, [.. creativity]);
    }

    /// <summary>Reads <c>siteBonus</c>: an array of <c>{ when, stat, percent }</c> that apply to this building only.</summary>
    private static ImmutableArray<SiteBonusSpec> ReadSiteBonuses(ObjectView o)
    {
        if (!o.TryGet("siteBonus", out JsonElement array))
        {
            return [];
        }

        var bonuses = new List<SiteBonusSpec>();
        o.ForEach(array, o.Child("siteBonus"), (element, pointer) =>
        {
            using ObjectView? bonus = o.Reader.OpenObject(element, pointer);
            if (bonus is null)
            {
                return;
            }

            SiteCondition? when = null;
            using (ObjectView? whenObject = bonus.OptObject("when"))
            {
                if (whenObject is not null)
                {
                    when = ReadSiteCondition(whenObject);
                }
                else if (!bonus.TryGet("when", out _))
                {
                    o.Reader.Error(bonus.Pointer, "missing required property 'when'");
                }
            }

            Stat stat = bonus.ReqEnum<Stat>("stat");
            int percent = bonus.ReqInt("percent", -1000, 10_000);
            if (when is not null)
            {
                bonuses.Add(new SiteBonusSpec(when, stat, percent));
            }
        });

        return [.. bonuses];
    }

    /// <summary>Reads a <c>siteBonus.when</c> object: exactly one of <c>terrain</c> or <c>near</c>.</summary>
    private static SiteCondition? ReadSiteCondition(ObjectView when)
    {
        ItemReader r = when.Reader;
        bool hasTerrain = when.TryGet("terrain", out _);
        bool hasNear = when.TryGet("near", out JsonElement nearElement);
        if (hasTerrain == hasNear)
        {
            r.Error(when.Pointer, $"'when' needs exactly one of: {string.Join(", ", ContentNames.SiteConditionKinds)}");
            return null;
        }

        if (hasTerrain)
        {
            int errorsBefore = r.ErrorCount;
            ImmutableArray<TerrainId> terrain = when.RefList(r.Tables.Terrains, "terrain");
            if (terrain.IsEmpty && r.ErrorCount == errorsBefore)
            {
                r.Error(when.Child("terrain"), "list at least one terrain");
            }

            return r.ErrorCount == errorsBefore ? new TerrainSite(terrain) : null;
        }

        using ObjectView? near = r.OpenObject(nearElement, when.Child("near"));
        if (near is null)
        {
            return null;
        }

        int before = r.ErrorCount;
        bool hasBuilding = near.TryGet("building", out JsonElement building);
        bool hasTag = near.TryGet("tag", out JsonElement tag);
        BuildingSelector? selector = null;
        if (hasBuilding == hasTag)
        {
            r.Error(near.Pointer, "'near' needs exactly one of 'building' or 'tag'");
        }
        else if (hasBuilding)
        {
            if (r.TryRef(building, near.Child("building"), r.Tables.Buildings, out BuildingTypeId id))
            {
                selector = new BuildingTypeSelector(id);
            }
        }
        else if (r.TryTag(tag, near.Child("tag"), out string name))
        {
            selector = new BuildingTagSelector(name);
        }

        int radius = near.ReqInt("radius", 1, 64);
        return selector is not null && r.ErrorCount == before ? new NearSite(selector, radius) : null;
    }

    /// <summary>Reads <c>aura</c>: <c>{ stat, percent, target, coverage }</c>; the target is required and never island-wide.</summary>
    private static AuraSpec? ReadAura(ObjectView o)
    {
        using ObjectView? aura = o.OptObject("aura");
        if (aura is null)
        {
            return null;
        }

        ItemReader r = aura.Reader;
        int errorsBefore = r.ErrorCount;
        Stat stat = aura.ReqEnum<Stat>("stat");
        int percent = aura.ReqInt("percent", -1000, 10_000);
        ModifierTarget? target = aura.Require("target", out JsonElement targetElement) ? r.ReadTarget(targetElement, aura.Child("target")) : null;
        int coverage = aura.ReqInt("coverage", 1, 1000);
        return target is not null && r.ErrorCount == errorsBefore ? new AuraSpec(stat, percent, target, coverage) : null;
    }

    private static ImmutableArray<SlotSpec> ReadSlots(ObjectView o)
    {
        if (!o.TryGet("slots", out JsonElement array))
        {
            return [];
        }

        var slots = new List<SlotSpec>();
        o.ForEach(array, o.Child("slots"), (element, pointer) =>
        {
            using ObjectView? slot = o.Reader.OpenObject(element, pointer);
            if (slot is null)
            {
                return;
            }

            GreatWorkTypeId? type = null;
            if (slot.Require("type", out JsonElement typeElement) && o.Reader.TryString(typeElement, slot.Child("type"), out string typeName)
                && typeName != "any" && o.Reader.TryRefName(typeName, slot.Child("type"), o.Reader.Tables.GreatWorks, out GreatWorkTypeId resolved))
            {
                type = resolved;
            }

            int count = slot.ReqInt("count", 1, MaxCount);
            if (slots.Exists(s => s.Type == type))
            {
                o.Reader.Error(pointer, "this slot type is listed twice; add the counts together");
            }

            slots.Add(new SlotSpec(type, count));
        });

        // Typed slots first in type-ID order, then the "any" slots.
        slots.Sort(static (a, b) => SlotRank(a).CompareTo(SlotRank(b)));
        return [.. slots];
    }

    private static int SlotRank(SlotSpec slot) => slot.Type is { } type ? type.Value : int.MaxValue;
}
