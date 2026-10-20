using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;

namespace Evenmere.Content.Tests;

/// <summary>Reads the kitchen-sink pack and checks every effect kind, condition kind and optional field.</summary>
public sealed class VocabularyTests : IDisposable
{
    private readonly TempContent _content = KitchenSink.Create();
    private readonly Lazy<ContentDb> _db;

    public VocabularyTests()
    {
        _db = new Lazy<ContentDb>(() => _content.Load().ShouldLoadCleanly());
    }

    public void Dispose() => _content.Dispose();

    private ContentDb Db => _db.Value;

    [Fact]
    public void Load_KitchenSink_HasZeroDiagnostics()
    {
        _content.Load().Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(_content.Load().Diagnostics));
    }

    [Fact]
    public void Effects_EveryKind_ReadIntoTheirSealedRecord()
    {
        TechDef tech = Db.Get(Db.IdOf<TechId>("kitchen_tech"));
        ImmutableArray<Effect> effects = tech.Effects;
        effects.Length.ShouldBe(25);

        effects[0].ShouldBe(new UnlockBuildingEffect(Db.IdOf<BuildingTypeId>("bakery")));
        effects[1].ShouldBe(new UnlockRecipeEffect(Db.IdOf<RecipeId>("bake_bread")));
        effects[2].ShouldBe(new UnlockTierEffect(Db.IdOf<TierId>("villagers")));
        effects[3].ShouldBe(new UnlockTraditionEffect(Db.IdOf<TraditionId>("common_hearth")));
        effects[4].ShouldBe(new UnlockFestivalEffect(Db.IdOf<FestivalId>("harvest_fair")));
        effects[5].ShouldBe(new UnlockTerrainAccessEffect(TerrainAccess.NarrowRivers));
        effects[6].ShouldBe(new UnlockFeatureEffect(FeatureKind.Coin, null));
        effects[7].ShouldBe(new UnlockFeatureEffect(FeatureKind.GreatWorks, Db.IdOf<GreatWorkTypeId>("writing")));
        effects[8].ShouldBe(new TraditionSlotEffect(TraditionSlotType.Craft));

        ModifierEffect autumnFarms = effects[9].ShouldBeOfType<ModifierEffect>();
        autumnFarms.Stat.ShouldBe(Stat.Productivity);
        autumnFarms.Target.ShouldBe(new BuildingsTarget(new BuildingTagSelector("farm")));
        autumnFarms.Percent.ShouldBe(20);
        autumnFarms.When.ShouldBe(new SeasonCondition(Season.Autumn));

        ModifierEffect knowledge = effects[10].ShouldBeOfType<ModifierEffect>();
        knowledge.Stat.ShouldBe(Stat.OutputKnowledge);
        knowledge.Target.ShouldBe(new BuildingsTarget(new BuildingTypeSelector(Db.IdOf<BuildingTypeId>("scribes_hall"))));
        knowledge.When.ShouldBeNull();

        ModifierEffect construction = effects[11].ShouldBeOfType<ModifierEffect>();
        construction.Stat.ShouldBe(Stat.ConstructionSpeed);
        construction.Target.ShouldBe(new BuildingsTarget(new BuildingCategorySelector(BuildingCategory.Production)));
        construction.Percent.ShouldBe(-10);

        effects[12].ShouldBe(new ModifierEffect(Stat.Consumption, new GoodTarget(Db.IdOf<GoodId>("bread")), 5, null));
        effects[13].ShouldBe(new ModifierEffect(Stat.Workforce, new TierTarget(Db.IdOf<TierId>("villagers")), 15, null));
        effects[14].ShouldBe(new ModifierEffect(Stat.DeclineGrace, new IslandTarget(), 50, null));

        GrantGoodsEffect goods = effects[15].ShouldBeOfType<GrantGoodsEffect>();
        goods.Goods.Length.ShouldBe(2);
        goods.Goods.ShouldBe(
        [
            new GoodAmount(Db.IdOf<GoodId>("stone"), Amount.FromMilli(5500)),
            new GoodAmount(Db.IdOf<GoodId>("timber"), Amount.FromUnits(10)),
        ]);

        GrantPointsEffect points = effects[16].ShouldBeOfType<GrantPointsEffect>();
        points.Points.ShouldBe(
        [
            new PointAmount(PointKind.Knowledge, Amount.FromUnits(50)),
            new PointAmount(PointKind.Coin, Amount.FromMilli(2500)),
        ]);

        tech.Repeatable.ShouldBe(new RepeatableSpec(10));
    }

    [Fact]
    public void Effects_RevealCoastlineAndRequestFestival_ReadIntoTheirSealedRecords()
    {
        ImmutableArray<Effect> effects = Db.Get(Db.IdOf<TechId>("kitchen_tech")).Effects;

        effects[17].ShouldBe(new RevealCoastlineEffect());
        effects[18].ShouldBe(new RequestFestivalEffect(Db.IdOf<FestivalId>("harvest_fair"), 25));
    }

    [Fact]
    public void Effects_DiscoveryTargetAndTheNewStats_AreReadAsAuthored()
    {
        ImmutableArray<Effect> effects = Db.Get(Db.IdOf<TechId>("kitchen_tech")).Effects;

        effects[19].ShouldBe(new ModifierEffect(Stat.OutputCulture, new DiscoveryTarget(DiscoveryKind.NaturalWonder), 50, null));
        effects[20].ShouldBe(new ModifierEffect(Stat.ExpeditionSpeed, new IslandTarget(), 50, null));
        effects[21].ShouldBe(new ModifierEffect(Stat.ComfortBonus, new TierTarget(Db.IdOf<TierId>("villagers")), 50, null));
        effects[22].ShouldBe(new ModifierEffect(Stat.FestivalCost, new IslandTarget(), -25, null));
        effects[23].ShouldBe(new ModifierEffect(Stat.FestivalDuration, new IslandTarget(), 50, null));
        effects[24].ShouldBe(new ModifierEffect(Stat.SettlerArrival, new IslandTarget(), 50, new SeasonCondition(Season.Spring)));
    }

    [Fact]
    public void Conditions_EveryKind_ReadIntoTheirSealedRecord()
    {
        CivicDef civic = Db.Get(Db.IdOf<CivicId>("kitchen_civic"));
        AllCondition all = civic.Inspiration.ShouldBeOfType<AllCondition>();
        all.Conditions.Length.ShouldBe(23);

        BuildingTypeId bakery = Db.IdOf<BuildingTypeId>("bakery");
        TierId villagers = Db.IdOf<TierId>("villagers");
        GreatWorkTypeId writing = Db.IdOf<GreatWorkTypeId>("writing");

        all.Conditions[0].ShouldBe(new CountCondition(new BuildingTypeSelector(bakery), null, 2));
        all.Conditions[1].ShouldBe(new CountCondition(new BuildingTagSelector("farm"), Db.IdOf<TerrainId>("hills"), 3));
        all.Conditions[2].ShouldBe(new CountCondition(new BuildingCategorySelector(BuildingCategory.Knowledge), null, 1));
        all.Conditions[3].ShouldBe(new CountCondition(null, Db.IdOf<TerrainId>("hills"), 10));
        all.Conditions[4].ShouldBe(new ProducedCondition(Db.IdOf<GoodId>("grain"), Amount.FromUnits(30)));
        all.Conditions[5].ShouldBe(new ConsumedCondition(Db.IdOf<GoodId>("bread"), Amount.FromMilli(12500)));
        all.Conditions[6].ShouldBe(new PopulationCondition(villagers, 50));
        all.Conditions[7].ShouldBe(new PopulationCondition(null, 1500));
        all.Conditions[8].ShouldBe(new RoadTilesCondition(null, 40));
        all.Conditions[9].ShouldBe(new RoadTilesCondition(Db.Required.DirtRoad, 60));
        all.Conditions[10].ShouldBe(new ExpeditionsCondition(4));
        all.Conditions[11].ShouldBe(new FestivalsHeldCondition(2));
        all.Conditions[12].ShouldBe(new GreatWorksCondition(writing, true, 3));
        all.Conditions[13].ShouldBe(new GreatWorksCondition(null, false, 1));
        all.Conditions[14].ShouldBe(new VisitorsPerDayCondition(200));
        all.Conditions[15].ShouldBe(new ResearchedCondition(Db.IdOf<TechId>("net_weaving")));
        all.Conditions[16].ShouldBe(new AdoptedCondition(Db.IdOf<CivicId>("other_civic")));
        all.Conditions[17].ShouldBe(new DiscoveredCondition(DiscoveryKind.IronDeposit, null, 1));
        all.Conditions[18].ShouldBe(new DiscoveredCondition(null, Db.IdOf<TerrainId>("marsh"), 3));
        all.Conditions[19].ShouldBe(new SeasonCondition(Season.Autumn));
        all.Conditions[20].ShouldBe(new EraCondition(Db.IdOf<EraId>("township")));
        all.Conditions[21].ShouldBe(new StockCondition(Db.IdOf<GoodId>("timber"), Amount.FromUnits(40)));

        AnyCondition any = all.Conditions[22].ShouldBeOfType<AnyCondition>();
        any.Conditions.Length.ShouldBe(2);
        any.Conditions[0].ShouldBe(new SeasonCondition(Season.Spring));
        any.Conditions[1].ShouldBe(new NotCondition(new SeasonCondition(Season.Winter)));
    }

    [Fact]
    public void Buildings_OptionalFields_ReadAsAuthored()
    {
        BuildingDef bakery = Db.Get(Db.IdOf<BuildingTypeId>("bakery"));
        bakery.Era.ShouldBe(2);
        bakery.Cost.Coin.ShouldBe(Amount.FromUnits(10));
        bakery.Cost.Goods.ShouldBe([new GoodAmount(Db.IdOf<GoodId>("timber"), Amount.FromUnits(6))]);
        bakery.UpkeepCoin.ShouldBe(Amount.FromUnits(2));

        BuildingDef farm = Db.Get(Db.IdOf<BuildingTypeId>("grain_farm"));
        farm.Placement.Fertility.ShouldBe(new FertilitySpec(CropKind.Grain, 25));
        farm.Placement.AreaYield!.Kind.ShouldBe(AreaYieldKind.FootprintFertility);
        farm.Placement.AreaYield.Terrain.ShouldBeEmpty();
        farm.Tags.ShouldBe(["farm", "seasonal"]);

        BuildingDef quarry = Db.Get(Db.IdOf<BuildingTypeId>("quarry"));
        quarry.Placement.Deposit.ShouldBe(DepositKind.Stone);
        quarry.Placement.AreaYield!.Kind.ShouldBe(AreaYieldKind.FootprintDeposit);

        Db.Get(Db.IdOf<BuildingTypeId>("paper_mill")).Placement.Riverside.ShouldBeTrue();

        BuildingDef scribes = Db.Get(Db.IdOf<BuildingTypeId>("scribes_hall"));
        scribes.Outputs!.Knowledge.ShouldBe(Amount.FromUnits(4));
        scribes.Slots.ShouldBe([new SlotSpec(Db.IdOf<GreatWorkTypeId>("writing"), 1), new SlotSpec(null, 2)]);

        BuildingDef circle = Db.Get(Db.IdOf<BuildingTypeId>("storytellers_circle"));
        circle.Outputs!.Culture.ShouldBe(Amount.FromUnits(3));
        circle.Outputs.Creativity.ShouldBe([new CreativityAmount(Db.IdOf<GreatWorkTypeId>("writing"), Amount.FromUnits(1))]);

        BuildingDef gasworks = Db.Get(Db.IdOf<BuildingTypeId>("gasworks"));
        gasworks.Fuel.ShouldBe(new FuelSpec(Db.IdOf<GoodId>("coal"), Amount.FromUnits(1)));
        gasworks.Service.ShouldBe(new ServiceSpec(ServiceKind.Gas, 24));

        BuildingDef bridge = Db.Get(Db.IdOf<BuildingTypeId>("timber_bridge"));
        bridge.RoadCost.ShouldBe(800);
        bridge.BridgeSpan.ShouldBe(3);

        Db.Get(Db.IdOf<BuildingTypeId>("stone_storehouse")).UpgradeOf.ShouldBe(Db.Required.Storehouse);
        Db.Get(Db.IdOf<BuildingTypeId>("inn")).Lodging.ShouldBe(40);
        Db.Get(Db.IdOf<BuildingTypeId>("smelter")).Attractiveness.ShouldBe(-2);
        Db.Get(Db.IdOf<BuildingTypeId>("standing_stones")).Category.ShouldBe(BuildingCategory.Monument);
        Db.Get(Db.IdOf<BuildingTypeId>("great_lighthouse")).Category.ShouldBe(BuildingCategory.Wonder);
    }

    [Fact]
    public void Buildings_SiteBonuses_ReadTerrainAndNearConditions()
    {
        BuildingDef windmill = Db.Get(Db.IdOf<BuildingTypeId>("windmill"));
        windmill.SiteBonuses.Length.ShouldBe(1);
        windmill.SiteBonuses[0].Stat.ShouldBe(Stat.Productivity);
        windmill.SiteBonuses[0].Percent.ShouldBe(15);
        windmill.SiteBonuses[0].When.ShouldBeOfType<TerrainSite>().Terrain.ShouldBe([Db.IdOf<TerrainId>("hills")]);

        BuildingDef observatory = Db.Get(Db.IdOf<BuildingTypeId>("observatory"));
        observatory.SiteBonuses[0].Stat.ShouldBe(Stat.OutputKnowledge);
        observatory.SiteBonuses[0].Percent.ShouldBe(25);

        // The terrains keep file order (hills, then highlands), not ID order.
        observatory.SiteBonuses[0].When.ShouldBeOfType<TerrainSite>().Terrain
            .ShouldBe([Db.IdOf<TerrainId>("hills"), Db.IdOf<TerrainId>("highlands")]);

        BuildingDef apiary = Db.Get(Db.IdOf<BuildingTypeId>("apiary"));
        apiary.SiteBonuses.Length.ShouldBe(2);
        apiary.SiteBonuses[0].When.ShouldBe(new NearSite(new BuildingTagSelector("orchard"), 6));
        apiary.SiteBonuses[0].Percent.ShouldBe(20);
        apiary.SiteBonuses[1].When.ShouldBe(new NearSite(new BuildingTypeSelector(Db.IdOf<BuildingTypeId>("grain_farm")), 3));
        apiary.SiteBonuses[1].Percent.ShouldBe(-5);

        Db.Get(Db.IdOf<BuildingTypeId>("quarry")).SiteBonuses.ShouldBeEmpty();
        Db.Get(Db.IdOf<BuildingTypeId>("quarry")).Aura.ShouldBeNull();
    }

    [Fact]
    public void Buildings_Aura_ReadsStatPercentTargetAndCoverage()
    {
        AuraSpec aura = Db.Get(Db.IdOf<BuildingTypeId>("bathhouse")).Aura.ShouldNotBeNull();

        aura.Stat.ShouldBe(Stat.Capacity);
        aura.Percent.ShouldBe(10);
        aura.Target.ShouldBe(new BuildingsTarget(new BuildingCategorySelector(BuildingCategory.Residence)));
        aura.Coverage.ShouldBe(16);
    }

    [Fact]
    public void Monuments_StagesAreArraysOfCostObjects()
    {
        MonumentDef stones = Db.Get(Db.IdOf<MonumentId>("standing_stones"));
        stones.Building.ShouldBe(Db.IdOf<BuildingTypeId>("standing_stones"));
        stones.Stages.Length.ShouldBe(3);
        stones.Stages[0].Goods.ShouldBe([new GoodAmount(Db.IdOf<GoodId>("stone"), Amount.FromUnits(20))]);
        stones.Stages[1].Goods.ShouldBe(
        [
            new GoodAmount(Db.IdOf<GoodId>("stone"), Amount.FromUnits(30)),
            new GoodAmount(Db.IdOf<GoodId>("timber"), Amount.FromUnits(10)),
        ]);
        stones.Effects.Length.ShouldBe(2);

        MonumentDef lighthouse = Db.Get(Db.IdOf<MonumentId>("great_lighthouse"));
        lighthouse.Stages[1].Coin.ShouldBe(Amount.FromUnits(100));
        lighthouse.Effects.ShouldBeEmpty();

        Db.TryGetMonumentOf(Db.IdOf<BuildingTypeId>("standing_stones"), out MonumentId found).ShouldBeTrue();
        found.ShouldBe(Db.IdOf<MonumentId>("standing_stones"));
        Db.TryGetMonumentOf(Db.Required.Storehouse, out _).ShouldBeFalse();
    }

    [Fact]
    public void Festivals_ReadSeasonsCostsAndBonuses()
    {
        FestivalDef harvest = Db.Get(Db.IdOf<FestivalId>("harvest_fair"));
        harvest.Seasons.ShouldBeEmpty();
        harvest.Days.ShouldBe(5);
        harvest.CulturePerDay.ShouldBe(Amount.FromUnits(5));
        harvest.SeasonBonus.ShouldBe(new SeasonBonus(Season.Autumn, 50));
        harvest.Cost.Goods.Length.ShouldBe(2);
        harvest.Effects.ShouldBe([new ModifierEffect(Stat.Visitors, new IslandTarget(), 100, null)]);
        harvest.Start.ShouldBeFalse();

        FestivalDef midsummer = Db.Get(Db.IdOf<FestivalId>("midsummer_fair"));
        midsummer.Seasons.ShouldBe([Season.Spring, Season.Summer]);
        midsummer.Start.ShouldBeTrue();
    }

    [Fact]
    public void GreatWorks_ReadCreationRulesAndCultureByEra()
    {
        GreatWorkTypeDef writing = Db.Get(Db.IdOf<GreatWorkTypeId>("writing"));
        writing.Source.ShouldBe(GreatWorkSource.Creativity);
        writing.BaseCreativity.ShouldBe(Amount.FromUnits(100));
        writing.GrowthPercent.ShouldBe(25);
        writing.CultureByEra.ShouldBe([Amount.FromUnits(2), Amount.FromUnits(4), Amount.FromUnits(6), Amount.FromUnits(8)]);
        writing.Attractiveness.ShouldBe(1);

        GreatWorkTypeDef relic = Db.Get(Db.IdOf<GreatWorkTypeId>("relic"));
        relic.Source.ShouldBe(GreatWorkSource.Discovery);
        relic.BaseCreativity.ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Happenings_ReadTriggerOptionsAndDefault()
    {
        HappeningDef scholar = Db.Get(Db.IdOf<HappeningId>("travelling_scholar"));
        scholar.Trigger.ShouldBe(new PopulationCondition(null, 100));
        scholar.ChancePerMille.ShouldBe(20);
        scholar.CooldownDays.ShouldBe(30);
        scholar.Options.Length.ShouldBe(3);
        scholar.Options[0].DurationDays.ShouldBe(0);
        scholar.Options[1].DurationDays.ShouldBe(10);
        scholar.Options[2].Effects.ShouldBeEmpty();
        scholar.DefaultOption.ShouldBe(2);
    }

    [Fact]
    public void Terrains_ReadAccessFlags()
    {
        TerrainDef marsh = Db.Get(Db.IdOf<TerrainId>("marsh"));
        marsh.Buildable.ShouldBeFalse();
        marsh.Access.ShouldBe(TerrainAccess.MarshDrainage);

        TerrainDef river = Db.Get(Db.IdOf<TerrainId>("river"));
        river.BlocksRoads.ShouldBeTrue();
        river.Access.ShouldBeNull();
    }

    [Fact]
    public void Traditions_ReadStyleKeysAndStartFlags()
    {
        TraditionDef style = Db.Get(Db.IdOf<TraditionId>("style_island_stone"));
        style.Style.ShouldBe("island_stone");
        style.Tags.ShouldBe(["style"]);
        style.Slot.ShouldBe(TraditionSlotType.Heritage);
        style.Start.ShouldBeTrue();
        style.Effects.ShouldBeEmpty();

        Db.Get(Db.IdOf<TraditionId>("harvest_festival")).Style.ShouldBeNull();
    }

    [Fact]
    public void Tables_EveryNameRoundTripsThroughEveryLookup()
    {
        AssertRoundTrip<GoodId, GoodDef>(Db.Goods);
        AssertRoundTrip<BuildingTypeId, BuildingDef>(Db.Buildings);
        AssertRoundTrip<RecipeId, RecipeDef>(Db.Recipes);
        AssertRoundTrip<TierId, TierDef>(Db.Tiers);
        AssertRoundTrip<TechId, TechDef>(Db.Techs);
        AssertRoundTrip<CivicId, CivicDef>(Db.Civics);
        AssertRoundTrip<TraditionId, TraditionDef>(Db.Traditions);
        AssertRoundTrip<FestivalId, FestivalDef>(Db.Festivals);
        AssertRoundTrip<GreatWorkTypeId, GreatWorkTypeDef>(Db.GreatWorks);
        AssertRoundTrip<MonumentId, MonumentDef>(Db.Monuments);
        AssertRoundTrip<HappeningId, HappeningDef>(Db.Happenings);
        AssertRoundTrip<EraId, EraDef>(Db.Eras);
        AssertRoundTrip<TerrainId, TerrainDef>(Db.Terrains);
    }

    private void AssertRoundTrip<TId, TDef>(ContentTable<TId, TDef> table)
        where TId : struct, IContentId<TId>
    {
        table.Count.ShouldBeGreaterThan(0);
        table.Items.Length.ShouldBe(table.Names.Length);
        table.Names.SequenceEqual(table.Names.Order(StringComparer.Ordinal)).ShouldBeTrue("Names are in ordinal order.");

        for (int i = 0; i < table.Count; i++)
        {
            TId id = TId.From((ushort)i);
            string name = table.Names[i];

            table.NameOf(id).ShouldBe(name);
            table.IdOf(name).ShouldBe(id);
            table.TryGet(name, out TId found).ShouldBeTrue();
            found.ShouldBe(id);
            table.Get(id).ShouldBeSameAs(table.Items[i]);
            table[id].ShouldBeSameAs(table.Items[i]);

            // The same lookups through the database's generic methods.
            Db.NameOf(id).ShouldBe(name);
            Db.IdOf<TId>(name).ShouldBe(id);
            Db.TryGet(name, out TId viaDb).ShouldBeTrue();
            viaDb.ShouldBe(id);
        }

        table.TryGet("no_such_item", out _).ShouldBeFalse();
        Should.Throw<KeyNotFoundException>(() => table.IdOf("no_such_item"));
        Db.TryGet("no_such_item", out TId _).ShouldBeFalse();
    }
}
