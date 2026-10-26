using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The condition-codec mirror: every operation <see cref="ConditionCodec"/> encodes remaps through
/// <see cref="ConditionRemap"/> and decodes to the same condition rebuilt with the new IDs. The save's table sorts an
/// extra good first, so old indices are resolved by name (old bread is not raw 1 once content grows past it);
/// the expectations use the new IDs.
/// </summary>
public sealed class ConditionRemapTests
{
    private static ContentDb Content => SiteFinder.Content;

    [Fact]
    public void Remap_EveryOperation_MapsIdsAndDecodesToRebuiltCondition()
    {
        ContentDb content = Content;
        string[] oldGoods = ["aardvark", .. content.Goods.Names];
        int OldIndex(string name) => Array.IndexOf(oldGoods, name);

        // Old raws in the shifted table, resolved by name: aardvark 0, then the content order.
        // Everything else is the identity.
        Condition condition = new AllCondition(
        [
            new ProducedCondition(GoodId.From((ushort)OldIndex("bread")), Amount.FromMilli(30_000)),
            new ConsumedCondition(GoodId.From((ushort)OldIndex("flour")), Amount.FromMilli(1_000)),
            new StockCondition(GoodId.From((ushort)OldIndex("bread")), Amount.FromMilli(2_000)),
            new CountCondition(new BuildingTypeSelector(content.IdOf<BuildingTypeId>("windmill")), null, 2),
            new CountCondition(new BuildingTagSelector("fishing"), TerrainId.From(1), 3),
            new CountCondition(new BuildingCategorySelector(BuildingCategory.Production), null, 1),
            new CountCondition(null, null, 4),
            new PopulationCondition(content.IdOf<TierId>("settlers"), 10),
            new PopulationCondition(null, 5),
            new RoadTilesCondition(content.IdOf<BuildingTypeId>("windmill"), 6),
            new RoadTilesCondition(null, 7),
            new ResearchedCondition(content.IdOf<TechId>("milling")),
            new AdoptedCondition(content.IdOf<CivicId>("hearth_and_shrine")),
            new EraCondition(EraId.From(0)),
            new SeasonCondition(Season.Autumn),
            new AnyCondition([new ExpeditionsCondition(1), new FestivalsHeldCondition(2), new VisitorsPerDayCondition(3)]),
            new NotCondition(new GreatWorksCondition(null, false, 1)),
            new DiscoveredCondition(null, null, 2),
        ]);

        long[] encoded = ConditionCodec.Encode(condition);

        SaveTables tables = TablesWith(content, oldGoods);

        long[]? remapped = ConditionRemap.Remap(encoded, tables, out bool dropped);
        dropped.ShouldBeFalse();
        remapped.ShouldNotBeNull();
        remapped!.ShouldBe(ConditionCodec.Encode(Expected(content)));

        AllCondition actual = ConditionCodec.Decode(remapped!, content).ShouldBeOfType<AllCondition>();
        AllCondition want = Expected(content);
        actual.Conditions.Length.ShouldBe(want.Conditions.Length);
        for (int i = 0; i < want.Conditions.Length; i++)
        {
            ShouldEqual(actual.Conditions[i], want.Conditions[i], $"child {i}");
        }
    }

    private static void ShouldEqual(Condition actual, Condition want, string path)
    {
        if (actual is AllCondition actualAll && want is AllCondition wantAll)
        {
            actualAll.Conditions.Length.ShouldBe(wantAll.Conditions.Length, path);
            for (int i = 0; i < wantAll.Conditions.Length; i++)
            {
                ShouldEqual(actualAll.Conditions[i], wantAll.Conditions[i], $"{path}[{i}]");
            }

            return;
        }

        if (actual is AnyCondition actualAny && want is AnyCondition wantAny)
        {
            actualAny.Conditions.Length.ShouldBe(wantAny.Conditions.Length, path);
            for (int i = 0; i < wantAny.Conditions.Length; i++)
            {
                ShouldEqual(actualAny.Conditions[i], wantAny.Conditions[i], $"{path}[{i}]");
            }

            return;
        }

        if (actual is NotCondition actualNot && want is NotCondition wantNot)
        {
            ShouldEqual(actualNot.Condition, wantNot.Condition, path + ".not");
            return;
        }

        actual.ShouldBe(want, $"{path} ({want.GetType().Name})");
    }

    [Fact]
    public void Remap_UnknownId_DropsCondition()
    {
        ContentDb content = Content;
        string[] oldGoods = [.. content.Goods.Names, "gone_good"];
        SaveTables tables = TablesWith(content, oldGoods);

        Condition gone = new ProducedCondition(GoodId.From((ushort)(oldGoods.Length - 1)), Amount.FromMilli(1_000));
        long[] goneEncoded = ConditionCodec.Encode(gone);

        ConditionRemap.Remap(goneEncoded, tables, out bool dropped).ShouldBeNull();
        dropped.ShouldBeTrue();
    }

    [Fact]
    public void Remap_MalformedData_DropsCondition()
    {
        ContentDb content = Content;
        SaveTables tables = TablesWith(content, [.. content.Goods.Names]);

        ConditionRemap.Remap([9999], tables, out bool dropped).ShouldBeNull();
        dropped.ShouldBeTrue();

        ConditionRemap.Remap(null, tables, out dropped).ShouldBeNull();
        dropped.ShouldBeFalse();
    }

    private static AllCondition Expected(ContentDb content)
    {
        GoodId bread = content.IdOf<GoodId>("bread");
        GoodId flour = content.IdOf<GoodId>("flour");
        BuildingTypeId windmill = content.IdOf<BuildingTypeId>("windmill");
        return new AllCondition(
        [
            new ProducedCondition(bread, Amount.FromMilli(30_000)),
            new ConsumedCondition(flour, Amount.FromMilli(1_000)),
            new StockCondition(bread, Amount.FromMilli(2_000)),
            new CountCondition(new BuildingTypeSelector(windmill), null, 2),
            new CountCondition(new BuildingTagSelector("fishing"), TerrainId.From(1), 3),
            new CountCondition(new BuildingCategorySelector(BuildingCategory.Production), null, 1),
            new CountCondition(null, null, 4),
            new PopulationCondition(content.IdOf<TierId>("settlers"), 10),
            new PopulationCondition(null, 5),
            new RoadTilesCondition(windmill, 6),
            new RoadTilesCondition(null, 7),
            new ResearchedCondition(content.IdOf<TechId>("milling")),
            new AdoptedCondition(content.IdOf<CivicId>("hearth_and_shrine")),
            new EraCondition(EraId.From(0)),
            new SeasonCondition(Season.Autumn),
            new AnyCondition([new ExpeditionsCondition(1), new FestivalsHeldCondition(2), new VisitorsPerDayCondition(3)]),
            new NotCondition(new GreatWorksCondition(null, false, 1)),
            new DiscoveredCondition(null, null, 2),
        ]);
    }

    private static SaveTables TablesWith(ContentDb content, string[] goods)
    {
        var header = new SaveHeader
        {
            Goods = goods,
            Buildings = [.. content.Buildings.Names],
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = [.. content.Techs.Names],
            Civics = [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = [.. content.Eras.Names],
            Terrains = [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }
}
