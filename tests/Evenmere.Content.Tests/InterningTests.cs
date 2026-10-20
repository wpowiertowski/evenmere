using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Tests;

/// <summary>Integer IDs are assigned per type from 0, in ordinal order of the string IDs.</summary>
public sealed class InterningTests
{
    [Fact]
    public void Ids_MinimalPack_FollowOrdinalStringOrder()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.Goods.Names.ShouldBe(["fish", "timber", "wood"]);
        db.IdOf<GoodId>("fish").ShouldBe(new GoodId(0));
        db.IdOf<GoodId>("timber").ShouldBe(new GoodId(1));
        db.IdOf<GoodId>("wood").ShouldBe(new GoodId(2));

        db.Buildings.Names.ShouldBe(
            ["dirt_road", "fishing_hut", "founders_hall", "sawpit", "settler_house", "storehouse", "woodcutters_hut"]);
        db.IdOf<BuildingTypeId>("dirt_road").Value.ShouldBe((ushort)0);
        db.IdOf<BuildingTypeId>("woodcutters_hut").Value.ShouldBe((ushort)6);

        db.Tiers.Names.ShouldBe(["settlers", "villagers"]);
        db.Eras.Names.ShouldBe(["founding", "township"], "Era IDs are in string order; ErasByOrder is the timeline.");
    }

    [Fact]
    public void Ids_AreOrdinalNotCultureAwareOrder()
    {
        using var content = TempContent.FromMinimal();
        foreach (string name in new[] { "ab", "a_b", "a1", "a", "b", "z9", "z_9" })
        {
            content.Write($"goods/{name}.json", $$"""{ "id": "{{name}}", "category": "raw", "era": 1 }""");
        }

        ContentDb db = content.Load().ShouldLoadCleanly();

        // '1' (0x31) < '_' (0x5F) < 'b' (0x62), so a1 < a_b < ab; a shorter prefix sorts first.
        db.Goods.Names.ShouldBe(["a", "a1", "a_b", "ab", "b", "fish", "timber", "wood", "z9", "z_9"]);
        db.Goods.Names.ShouldBe(db.Goods.Names.Order(StringComparer.Ordinal).ToArray());
        for (int i = 0; i < db.Goods.Count; i++)
        {
            db.IdOf<GoodId>(db.Goods.Names[i]).Value.ShouldBe((ushort)i);
        }
    }

    [Fact]
    public void Ids_DoNotDependOnTheOrderFilesWereCreated()
    {
        string[] names = ["ore", "clay", "reed", "salt", "tar"];
        using var forward = TempContent.FromMinimal();
        using var backward = TempContent.FromMinimal();
        foreach (string name in names)
        {
            forward.Write($"goods/{name}.json", $$"""{ "id": "{{name}}", "category": "raw", "era": 1 }""");
        }

        foreach (string name in names.Reverse())
        {
            backward.Write($"goods/{name}.json", $$"""{ "id": "{{name}}", "category": "raw", "era": 1 }""");
        }

        ContentDb a = forward.Load().ShouldLoadCleanly();
        ContentDb b = backward.Load().ShouldLoadCleanly();

        a.Goods.Names.ShouldBe(b.Goods.Names);
        a.ContentHash.ShouldBe(b.ContentHash);
    }

    [Fact]
    public void Ids_LoadingTwice_GivesTheSameAnswers()
    {
        ContentDb first = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();
        ContentDb second = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        first.Goods.Names.ShouldBe(second.Goods.Names);
        first.Buildings.Names.ShouldBe(second.Buildings.Names);
        first.ContentHash.ShouldBe(second.ContentHash);
        first.Required.ShouldBe(second.Required);
    }

    [Fact]
    public void ErasByOrder_ListsErasChronologicallyEvenWhenIdsSortDifferently()
    {
        using var content = TempContent.FromMinimal();
        content.Write("eras/enlightenment.json", """
            { "id": "enlightenment", "order": 3, "requires": { "tech": "town_charter", "civic": "village_moot" } }
            """);

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Eras.Names.ShouldBe(["enlightenment", "founding", "township"]);
        db.ErasByOrder.Select(db.NameOf).ShouldBe(["founding", "township", "enlightenment"]);
    }

    [Fact]
    public void TiersByOrder_ListsTiersFromLowestToHighest()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.TiersByOrder.Select(db.NameOf).ShouldBe(["settlers", "villagers"]);
    }

    [Fact]
    public void Get_WithAnIdFromAnotherDatabase_IsOutOfRange()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        Should.Throw<IndexOutOfRangeException>(() => db.Get(new GoodId(3)));
    }

    [Fact]
    public void NameOf_GenericOverRawIdTypes_NeedsNoPerTypeOverload()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.NameOf(new TerrainId(0)).ShouldBe("forest");
        db.NameOf(new RecipeId(2)).ShouldBe("saw_timber");
        db.TryGet("river", out TerrainId river).ShouldBeTrue();
        river.Value.ShouldBe((ushort)2);
    }
}
