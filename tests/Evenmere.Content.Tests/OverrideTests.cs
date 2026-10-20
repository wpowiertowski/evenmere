using Evenmere.Content.Definitions;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Tests;

/// <summary>Content packs load after the base game; an item with the same ID replaces the earlier one whole.</summary>
public sealed class OverrideTests
{
    [Fact]
    public void Load_PackItemWithTheSameId_ReplacesTheBaseItemWhole()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("buildings/fishing_hut.json", """
            {
              "id": "fishing_hut", "category": "production", "era": 1, "footprint": [3, 3],
              "cost": { "timber": 9 }, "buildDays": 4, "recipe": "catch_fish"
            }
            """);

        ContentDb db = ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly();

        BuildingDef hut = db.Get(db.IdOf<BuildingTypeId>("fishing_hut"));
        hut.Footprint.ShouldBe(new Footprint(3, 3));
        hut.Cost.Goods.ShouldBe([new GoodAmount(db.IdOf<GoodId>("timber"), Amount.FromUnits(9))]);
        hut.BuildDays.ShouldBe(4);

        // Nothing is merged field by field: what the base item had and the pack did not say is gone.
        hut.Workforce.ShouldBeNull();
        hut.Placement.Coastline.ShouldBeFalse();
        hut.Placement.AreaYield.ShouldBeNull();
        hut.Tags.ShouldBeEmpty();
        hut.Start.ShouldBeFalse();
    }

    [Fact]
    public void Load_PackItemWithTheSameId_LeavesOtherItemsAlone()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("goods/fish.json", """{ "id": "fish", "category": "raw", "era": 1 }""");

        ContentDb overridden = ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly();
        ContentDb original = ContentLoader.Load(baseGame.Root).ShouldLoadCleanly();

        overridden.Get(overridden.IdOf<GoodId>("fish")).Category.ShouldBe(GoodCategory.Raw);
        original.Get(original.IdOf<GoodId>("fish")).Category.ShouldBe(GoodCategory.Consumer);
        overridden.Goods.Names.ShouldBe(original.Goods.Names);
        GoodDef wood = overridden.Get(overridden.IdOf<GoodId>("wood"));
        wood.Category.ShouldBe(GoodCategory.Raw);
        wood.Tags.ShouldBe(["fuel"]);
    }

    [Fact]
    public void Load_PackNewItem_IsInternedInOrdinalOrderWithTheBaseItems()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("goods/grain.json", """{ "id": "grain", "category": "raw", "era": 1 }""");

        ContentDb db = ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly();

        db.Goods.Names.ShouldBe(["fish", "grain", "timber", "wood"]);
        db.IdOf<GoodId>("wood").Value.ShouldBe((ushort)3);
    }

    [Fact]
    public void Load_PackRules_ReplaceTheBaseRules()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("rules/core.json", """
            {
              "cartTicksPerTile": 3, "outputBuffer": 6, "cartLoad": 3, "declineGraceDays": 14, "advanceStreakDays": 5,
              "inspirationDiscountPerMille": 300, "marketGating": false
            }
            """);

        RulesDef rules = ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly().Rules;

        rules.CartTicksPerTile.ShouldBe(3);
        rules.MarketGating.ShouldBeFalse();
        rules.Seasons.ShouldBeEmpty("Rules are replaced whole, so the base game's season curves are gone.");
    }

    [Fact]
    public void Load_RootOrder_DecidesWhichItemWins()
    {
        using var a = TempContent.FromMinimal();
        using var b = TempContent.Empty();
        b.Write("goods/wood.json", """{ "id": "wood", "category": "intermediate", "era": 1 }""");

        ContentDb baseThenPack = ContentLoader.Load([a.Root, b.Root]).ShouldLoadCleanly();
        ContentDb packThenBase = ContentLoader.Load([b.Root, a.Root]).ShouldLoadCleanly();

        baseThenPack.Get(baseThenPack.IdOf<GoodId>("wood")).Category.ShouldBe(GoodCategory.Intermediate);
        packThenBase.Get(packThenBase.IdOf<GoodId>("wood")).Category.ShouldBe(GoodCategory.Raw);
    }

    [Fact]
    public void Load_BrokenBaseItemThatAPackReplaces_ReportsNothing()
    {
        using var baseGame = TempContent.FromMinimal();
        baseGame.Write("goods/fish.json", "{ this is not json");
        using var pack = TempContent.Empty();
        pack.Write("goods/fish.json", """{ "id": "fish", "category": "consumer", "era": 1 }""");

        ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly();
    }

    [Fact]
    public void Load_BrokenPackItem_IsReportedWithAPathRelativeToThePack()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("goods/grain.json", """{ "id": "grain", "category": "raw", "era": 7 }""");

        ContentLoadResult result = ContentLoader.Load([baseGame.Root, pack.Root]);

        result.ShouldFailWith("EVL002", "goods/grain.json", "/era");
    }

    [Fact]
    public void Load_PackItemsCanReferenceBaseItemsAndTheOtherWayRound()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("goods/grain.json", """{ "id": "grain", "category": "raw", "era": 1 }""");
        pack.Write("recipes/grow_grain.json", """{ "id": "grow_grain", "inputs": { "wood": 1 }, "outputs": { "grain": 1 }, "cycleSeconds": 30 }""");
        pack.Write("buildings/sawpit.json", """
            {
              "id": "sawpit", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1,
              "recipe": "grow_grain"
            }
            """);

        ContentDb db = ContentLoader.Load([baseGame.Root, pack.Root]).ShouldLoadCleanly();

        BuildingDef sawpit = db.Get(db.IdOf<BuildingTypeId>("sawpit"));
        db.NameOf(sawpit.Recipe!.Value).ShouldBe("grow_grain");
    }

    [Fact]
    public void Load_SeveralRoots_HashDiffersFromTheBaseAlone()
    {
        using var baseGame = TempContent.FromMinimal();
        using var pack = TempContent.Empty();
        pack.Write("goods/grain.json", """{ "id": "grain", "category": "raw", "era": 1 }""");

        ContentLoader.Load([baseGame.Root, pack.Root]).Raw.ContentHash
            .ShouldNotBe(ContentLoader.Load(baseGame.Root).Raw.ContentHash);
    }
}
