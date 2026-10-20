using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Progression;

public sealed class StartUnlockTests
{
    [Fact]
    public void Pack_Content_HasStartAndLockedItemsOfEveryKind()
    {
        // The tests below compare against the content's own start flags, so each kind needs both a start and a non-start item.
        ContentDb content = ProgressionPack.Content;

        content.Buildings.Items.ShouldContain(static b => b.Start);
        content.Buildings.Items.ShouldContain(static b => !b.Start);
        content.Recipes.Items.ShouldContain(static r => r.Start);
        content.Recipes.Items.ShouldContain(static r => !r.Start);
        content.Tiers.Items.ShouldContain(static t => t.Start);
        content.Tiers.Items.ShouldContain(static t => !t.Start);
        content.Traditions.Items.ShouldContain(static t => t.Start);
        content.Traditions.Items.ShouldContain(static t => !t.Start);
        content.Festivals.Items.ShouldContain(static f => f.Start);
        content.Festivals.Items.ShouldContain(static f => !f.Start);
    }

    [Fact]
    public void Queries_BeforeStartUnlocks_NothingIsUnlocked()
    {
        var harness = ProgressionHarness.Create();

        for (int i = 0; i < harness.Content.Buildings.Count; i++)
        {
            harness.Queries.IsUnlocked(BuildingTypeId.From((ushort)i)).ShouldBeFalse();
        }

        for (int i = 0; i < harness.Content.Tiers.Count; i++)
        {
            harness.Queries.IsUnlocked(TierId.From((ushort)i)).ShouldBeFalse();
        }
    }

    [Fact]
    public void ApplyStartUnlocks_PackContent_UnlocksExactlyTheBuildingsMarkedStart()
    {
        var harness = ProgressionHarness.Create();

        harness.ApplyStartUnlocks();

        for (int i = 0; i < harness.Content.Buildings.Count; i++)
        {
            string name = harness.Content.Buildings.Names[i];
            harness.Queries.IsUnlocked(BuildingTypeId.From((ushort)i)).ShouldBe(harness.Content.Buildings.Items[i].Start, name);
        }

        harness.Queries.IsUnlocked(harness.Building("grain_farm")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyStartUnlocks_PackContent_UnlocksExactlyTheRecipesMarkedStart()
    {
        var harness = ProgressionHarness.Create();

        harness.ApplyStartUnlocks();

        for (int i = 0; i < harness.Content.Recipes.Count; i++)
        {
            harness.Queries.IsUnlocked(RecipeId.From((ushort)i)).ShouldBe(harness.Content.Recipes.Items[i].Start, harness.Content.Recipes.Names[i]);
        }
    }

    [Fact]
    public void ApplyStartUnlocks_PackContent_UnlocksExactlyTheTiersMarkedStart()
    {
        var harness = ProgressionHarness.Create();

        harness.ApplyStartUnlocks();

        for (int i = 0; i < harness.Content.Tiers.Count; i++)
        {
            harness.Queries.IsUnlocked(TierId.From((ushort)i)).ShouldBe(harness.Content.Tiers.Items[i].Start, harness.Content.Tiers.Names[i]);
        }

        harness.Queries.IsUnlocked(harness.Tier("settlers")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Tier("townsfolk")).ShouldBeFalse();
    }

    [Fact]
    public void ApplyStartUnlocks_PackContent_UnlocksExactlyTheTraditionsAndFestivalsMarkedStart()
    {
        var harness = ProgressionHarness.Create();

        harness.ApplyStartUnlocks();

        for (int i = 0; i < harness.Content.Traditions.Count; i++)
        {
            harness.Queries.IsUnlocked(TraditionId.From((ushort)i)).ShouldBe(harness.Content.Traditions.Items[i].Start, harness.Content.Traditions.Names[i]);
        }

        for (int i = 0; i < harness.Content.Festivals.Count; i++)
        {
            harness.Queries.IsUnlocked(FestivalId.From((ushort)i)).ShouldBe(harness.Content.Festivals.Items[i].Start, harness.Content.Festivals.Names[i]);
        }
    }

    [Fact]
    public void ApplyStartUnlocks_PackContent_PutsTheIslandInTheLowestOrderEraNotEraZero()
    {
        var harness = ProgressionHarness.Create();
        EraId awakening = harness.Content.IdOf<EraId>("awakening");
        EraId founding = harness.Content.IdOf<EraId>("founding");
        awakening.Value.ShouldBe((ushort)0, "the pack's era 'awakening' must sort first by name for this test to mean anything");
        harness.Content.Get(founding).Order.ShouldBeLessThan(harness.Content.Get(awakening).Order);

        harness.ApplyStartUnlocks();

        harness.Queries.CurrentEra.ShouldBe(founding);
    }

    [Fact]
    public void ApplyStartUnlocks_CalledTwice_ChangesNothing()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyStartUnlocks();
        byte[] once = harness.Save();

        harness.ApplyStartUnlocks();

        harness.Save().ShouldBe(once);
    }

    [Fact]
    public void ApplyStartUnlocks_DoesNotRelockWhatAnEffectUnlocked()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyTech("milling");

        harness.ApplyStartUnlocks();

        harness.Queries.IsUnlocked(harness.Building("bakery")).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("grain_farm")).ShouldBeTrue();
    }

    [Fact]
    public void ApplyStartUnlocks_RealContent_UnlocksTheFoundersHallAndTheSettlers()
    {
        var harness = ProgressionHarness.Create(ProgressionPack.Real);

        harness.ApplyStartUnlocks();

        RequiredIds required = harness.Content.Required;
        harness.Queries.IsUnlocked(required.FoundersHall).ShouldBeTrue();
        harness.Queries.IsUnlocked(required.SettlerHouse).ShouldBeTrue();
        harness.Queries.IsUnlocked(required.DirtRoad).ShouldBeTrue();
        harness.Queries.IsUnlocked(required.Storehouse).ShouldBeTrue();
        harness.Queries.IsUnlocked(required.Settlers).ShouldBeTrue();
        harness.Queries.IsUnlocked(harness.Building("windmill")).ShouldBeFalse();
        harness.Queries.CurrentEra.ShouldBe(harness.Content.ErasByOrder[0]);
    }

    [Fact]
    public void ApplyStartUnlocks_NullContext_Throws()
    {
        var harness = ProgressionHarness.Create();

        Should.Throw<ArgumentNullException>(() => harness.Progression.ApplyStartUnlocks(null!));
    }
}
