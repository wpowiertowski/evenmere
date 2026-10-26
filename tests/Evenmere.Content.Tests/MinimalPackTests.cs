using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Content.Locale;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Tests;

public sealed class MinimalPackTests
{
    [Fact]
    public void Load_MinimalPack_HasZeroDiagnostics()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        result.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        result.HasErrors.ShouldBeFalse();
        result.Db.ShouldNotBeNull();
    }

    [Fact]
    public void Load_MinimalPack_ReadsEveryItemOfEveryType()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.Eras.Count.ShouldBe(2);
        db.Terrains.Count.ShouldBe(4);
        db.Goods.Count.ShouldBe(3);
        db.Recipes.Count.ShouldBe(3);
        db.Buildings.Count.ShouldBe(7);
        db.Tiers.Count.ShouldBe(2);
        db.Techs.Count.ShouldBe(2);
        db.Civics.Count.ShouldBe(1);
        db.Traditions.Count.ShouldBe(1);
        db.Festivals.Count.ShouldBe(1);
        db.GreatWorks.Count.ShouldBe(0);
        db.Monuments.Count.ShouldBe(0);
        db.Happenings.Count.ShouldBe(1);
    }

    [Fact]
    public void Required_MinimalPack_NamesTheRequiredItems()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.NameOf(db.Required.FoundersHall).ShouldBe("founders_hall");
        db.NameOf(db.Required.SettlerHouse).ShouldBe("settler_house");
        db.NameOf(db.Required.DirtRoad).ShouldBe("dirt_road");
        db.NameOf(db.Required.Storehouse).ShouldBe("storehouse");
        db.NameOf(db.Required.Settlers).ShouldBe("settlers");
    }

    [Fact]
    public void Load_MinimalPack_ProducesTheWoodTimberFishChain()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();
        GoodId wood = db.IdOf<GoodId>("wood");
        GoodId timber = db.IdOf<GoodId>("timber");
        GoodId fish = db.IdOf<GoodId>("fish");

        RecipeDef cutWood = db.Get(db.Get(db.IdOf<BuildingTypeId>("woodcutters_hut")).Recipe!.Value);
        cutWood.Inputs.ShouldBeEmpty();
        cutWood.Outputs.ShouldBe([new GoodAmount(wood, Amount.FromUnits(1))]);

        RecipeDef sawTimber = db.Get(db.Get(db.IdOf<BuildingTypeId>("sawpit")).Recipe!.Value);
        sawTimber.Inputs.ShouldBe([new GoodAmount(wood, Amount.FromUnits(1))]);
        sawTimber.Outputs.ShouldBe([new GoodAmount(timber, Amount.FromUnits(1))]);

        RecipeDef catchFish = db.Get(db.Get(db.IdOf<BuildingTypeId>("fishing_hut")).Recipe!.Value);
        catchFish.Outputs.ShouldBe([new GoodAmount(fish, Amount.FromUnits(1))]);

        TierDef settlers = db.Get(db.Required.Settlers);
        settlers.BasicGoods.ShouldBe([new GoodAmount(fish, Amount.FromUnits(4)), new GoodAmount(wood, Amount.FromUnits(3))]);
        settlers.BasicServices.ShouldBe([ServiceKind.Market]);

        TierDef villagers = db.Get(db.IdOf<TierId>("villagers"));
        villagers.BasicGoods.ShouldBe([new GoodAmount(fish, Amount.FromUnits(4)), new GoodAmount(wood, Amount.FromUnits(4))]);
        villagers.AdvanceFrom.ShouldBe(db.Required.Settlers);
        villagers.Outputs.Knowledge.ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Load_MinimalPack_ReadsBuildingFieldsAsAuthored()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        BuildingDef hall = db.Get(db.Required.FoundersHall);
        hall.Category.ShouldBe(BuildingCategory.Infrastructure);
        hall.Footprint.ShouldBe(new Footprint(4, 4));
        hall.Storage.ShouldBe(new StorageSpec(24, 3));
        hall.Service.ShouldBe(new ServiceSpec(ServiceKind.Market, 16));
        hall.Outputs!.Knowledge.ShouldBe(Amount.FromUnits(2));
        hall.Outputs.Culture.ShouldBe(Amount.FromUnits(1));
        hall.Placement.RoadAccess.ShouldBeFalse();
        hall.Unique.ShouldBeTrue();
        hall.Start.ShouldBeTrue();
        hall.Cost.IsFree.ShouldBeTrue();

        BuildingDef road = db.Get(db.Required.DirtRoad);
        road.RoadCost.ShouldBe(1000);
        road.BridgeSpan.ShouldBeNull();

        BuildingDef fishingHut = db.Get(db.IdOf<BuildingTypeId>("fishing_hut"));
        fishingHut.Placement.Coastline.ShouldBeTrue();
        fishingHut.Placement.RoadAccess.ShouldBeTrue("roadAccess defaults to true.");
        AreaYieldSpec yield = fishingHut.Placement.AreaYield.ShouldNotBeNull();
        yield.Kind.ShouldBe(AreaYieldKind.Radius);
        yield.Terrain.ShouldBe([db.IdOf<TerrainId>("sea")]);
        yield.Radius.ShouldBe(5);
        yield.FullAt.ShouldBe(30);
        yield.Exclusive.ShouldBeTrue();
        fishingHut.Cost.Goods.ShouldBe([new GoodAmount(db.IdOf<GoodId>("timber"), Amount.FromUnits(3))]);
        fishingHut.Workforce.ShouldBe(new WorkforceSpec(db.Required.Settlers, 3));
        fishingHut.Tags.ShouldBe(["fishing", "gatherer"]);
    }

    [Fact]
    public void Load_MinimalPack_ReadsTechsCivicsAndTraditions()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        TechDef netWeaving = db.Get(db.IdOf<TechId>("net_weaving"));
        netWeaving.Cost.ShouldBe(Amount.FromUnits(50));
        netWeaving.Effects.ShouldBe(
        [
            new ModifierEffect(Stat.Productivity, new BuildingsTarget(new BuildingTagSelector("fishing")), 25, null),
        ]);
        netWeaving.Inspiration.ShouldBe(new CountCondition(
            new BuildingTypeSelector(db.IdOf<BuildingTypeId>("fishing_hut")), null, 5));

        TechDef townCharter = db.Get(db.IdOf<TechId>("town_charter"));
        townCharter.Gateway.ShouldBeTrue();
        townCharter.Requires.ShouldBe([db.IdOf<TechId>("net_weaving")]);

        CivicDef villageMoot = db.Get(db.IdOf<CivicId>("village_moot"));
        villageMoot.Gateway.ShouldBeTrue();
        villageMoot.Effects.ShouldBe(
        [
            new TraditionSlotEffect(TraditionSlotType.Hearth),
            new UnlockTraditionEffect(db.IdOf<TraditionId>("common_hearth")),
        ]);

        TraditionDef hearth = db.Get(db.IdOf<TraditionId>("common_hearth"));
        hearth.Slot.ShouldBe(TraditionSlotType.Hearth);

        EraDef township = db.Get(db.IdOf<EraId>("township"));
        township.Order.ShouldBe(2);
        township.RequiresTech.ShouldBe(db.IdOf<TechId>("town_charter"));
        township.RequiresCivic.ShouldBe(db.IdOf<CivicId>("village_moot"));
        db.ErasByOrder.Select(db.NameOf).ShouldBe(["founding", "township"]);
    }

    [Fact]
    public void Load_MinimalPack_ReadsRules()
    {
        RulesDef rules = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly().Rules;

        rules.CartTicksPerTile.ShouldBe(2);
        rules.OutputBuffer.ShouldBe(Amount.FromUnits(4));
        rules.CartLoad.ShouldBe(Amount.FromUnits(2));
        rules.DeclineGraceDays.ShouldBe(10);
        rules.AdvanceStreakDays.ShouldBe(3);
        rules.InspirationDiscount.ShouldBe(Ratio.FromPerMille(400));
        rules.MarketGating.ShouldBeTrue();
        rules.Seasons.Length.ShouldBe(1);
        rules.Seasons[0].Tag.ShouldBe("seasonal");
        rules.Seasons[0].Of(Core.Time.Season.Summer).ShouldBe(Ratio.FromPerMille(1200));
        rules.Seasons[0].Of(Core.Time.Season.Winter).ShouldBe(Ratio.FromPerMille(700));
    }

    [Fact]
    public void Locale_MinimalPack_LoadsWithZeroDiagnostics()
    {
        LocaleLoadResult locale = LocaleTable.Load(TestPaths.MinimalPack, "en");

        locale.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(locale.Diagnostics));
        locale.Table.Get("good.wood.name").ShouldBe("Wood");
    }

    [Fact]
    public void Locale_MinimalPack_HasNameAndDescriptionForEveryItem()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();
        LocaleTable locale = LocaleTable.Load(TestPaths.MinimalPack, "en").Table;

        var missing = new List<string>();
        void Check<TId, TDef>(ContentType type, ContentTable<TId, TDef> table)
            where TId : struct, IContentId<TId>
        {
            foreach (string name in table.Names)
            {
                foreach (string suffix in new[] { "name", "desc" })
                {
                    string key = $"{type.SingularName()}.{name}.{suffix}";
                    if (!locale.ContainsKey(key))
                    {
                        missing.Add(key);
                    }
                }
            }
        }

        Check(ContentType.Goods, db.Goods);
        Check(ContentType.Buildings, db.Buildings);
        Check(ContentType.Recipes, db.Recipes);
        Check(ContentType.Tiers, db.Tiers);
        Check(ContentType.Techs, db.Techs);
        Check(ContentType.Civics, db.Civics);
        Check(ContentType.Traditions, db.Traditions);
        Check(ContentType.Festivals, db.Festivals);
        Check(ContentType.Happenings, db.Happenings);
        Check(ContentType.Eras, db.Eras);
        Check(ContentType.Terrains, db.Terrains);

        missing.ShouldBeEmpty();
        locale.Count.ShouldBe(2 * (3 + 7 + 3 + 2 + 2 + 1 + 1 + 1 + 1 + 2 + 4));
    }
}
