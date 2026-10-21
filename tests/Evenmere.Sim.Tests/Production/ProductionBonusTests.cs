using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionBonusTests
{
    private static void SetHills(ProductionHarness h, int x, int y, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                h.Map.SetTile(x + dx, y + dy, h.Tile("hills"));
            }
        }
    }

    // A coast: plains on the left, sea from column 10 on, so a hut at (8, 10) reaches 40 sea tiles (more than the 30 for a full yield).
    private static void MakeCoast(ProductionHarness h)
    {
        TileInfo sea = h.Tile("sea");
        for (int y = 0; y < h.Map.Height; y++)
        {
            for (int x = 10; x < h.Map.Width; x++)
            {
                h.Map.SetTile(x, y, sea);
            }
        }
    }

    // --- Modifiers from progression ---

    [Fact]
    public void Modifier_NetWeavingPlusTwentyFivePercent_SpeedsUpFishingHuts()
    {
        var h = ProductionHarness.Create();
        MakeCoast(h);
        EntityId hut = h.AddReady("fishing_hut", 8, 10);
        BuildingTypeId fishingHut = h.Type("fishing_hut");
        var netWeaving = EffectSource.ForTech(h.Content.IdOf<TechId>("net_weaving"));
        h.Sim.Queries.Progression.AddModifier(
            Stat.Productivity, new ModifierContribution(netWeaving, 25, true), context => context.BuildingType == fishingHut);

        // A 20 s cycle is 80,000 progress, at 1,250 per tick: 64 ticks instead of 80.
        h.RunTicks(63);
        h.Output(hut, "fish").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(hut, "fish").ShouldBe(Amount.FromUnits(1));
        h.Info(hut).BonusPercent.ShouldBe(25);
        h.Info(hut).Productivity.PerMille.ShouldBe(1250);
    }

    [Fact]
    public void Modifier_ForAnotherKindOfBuilding_DoesNotApply()
    {
        var h = ProductionHarness.Create();
        MakeCoast(h);
        EntityId hut = h.AddReady("fishing_hut", 8, 10);
        BuildingTypeId sawpit = h.Type("sawpit");
        h.Sim.Queries.Progression.AddModifier(
            Stat.Productivity, new ModifierContribution(default, 25, true), context => context.BuildingType == sawpit);

        h.RunTicks(80);

        h.Output(hut, "fish").ShouldBe(Amount.FromUnits(1));
        h.Info(hut).BonusPercent.ShouldBe(0);
    }

    [Fact]
    public void Modifier_APenaltyOfOneHundredPercent_StopsProgressWithoutStalling()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.Sim.Queries.Progression.AddModifier(Stat.Productivity, new ModifierContribution(default, -100, true));

        h.RunTicks(300);

        h.Info(farm).Productivity.PerMille.ShouldBe(0);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
    }

    // --- Site bonuses: terrain ---

    [Fact]
    public void SiteBonus_WindmillOnHills_IsFifteenPercentFaster()
    {
        var h = ProductionHarness.Create();
        SetHills(h, 4, 4, 2, 2);
        EntityId mill = h.AddReady("windmill", 4, 4);
        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(2));

        // A 30 s cycle is 120,000 progress, at 1,150 per tick: 104 ticks give 119,600, so the cycle ends on tick 105.
        h.RunTicks(104);
        h.Output(mill, "flour").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(mill, "flour").ShouldBe(Amount.FromUnits(1));
        h.Info(mill).BonusPercent.ShouldBe(15);
        h.Info(mill).Productivity.PerMille.ShouldBe(1150);
    }

    [Fact]
    public void SiteBonus_WindmillPartlyOnHills_GetsNothing()
    {
        var h = ProductionHarness.Create();

        // Three of the four footprint tiles are hills: every tile must be one of the listed terrains.
        SetHills(h, 4, 4, 2, 1);
        h.Map.SetTile(4, 5, h.Tile("hills"));
        EntityId mill = h.AddReady("windmill", 4, 4);

        h.RunTicks(1);

        h.Info(mill).BonusPercent.ShouldBe(0);
    }

    [Fact]
    public void SiteBonus_WindmillOnPlains_GetsNothing()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 4, 4);

        h.RunTicks(1);

        h.Info(mill).BonusPercent.ShouldBe(0);
    }

    [Fact]
    public void SiteBonus_AddsToProgressionModifiersInsteadOfMultiplyingThem()
    {
        var h = ProductionHarness.Create();
        SetHills(h, 4, 4, 2, 2);
        EntityId mill = h.AddReady("windmill", 4, 4);
        h.Sim.Queries.Progression.AddModifier(Stat.Productivity, new ModifierContribution(default, 10, true));

        h.RunTicks(1);

        // 15 + 10 = 25 percent, applied once: 1,250 and not 1.15 × 1.10.
        h.Info(mill).BonusPercent.ShouldBe(25);
    }

    // --- Site bonuses: near ---

    [Fact]
    public void SiteBonus_NearABuilding_AppliesWhileItStandsWithinTheRadius()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);
        EntityId lookout = h.AddReady("lookout", 10, 10);

        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(0);

        // A storehouse at (12, 8) to (13, 9) is within 4 tiles of the lookout at (10, 10) to (11, 11).
        EntityId storehouse = h.Add("storehouse", 12, 8);
        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(10);

        h.Sim.Services.BuildingRegistry.Remove(storehouse);
        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(0);
    }

    [Fact]
    public void SiteBonus_NearABuildingStillUnderConstruction_DoesNotApplyUntilItIsFinished()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);
        EntityId lookout = h.AddReady("lookout", 10, 10);
        EntityId storehouse = h.Add("storehouse", 12, 8, BuildingState.Site);

        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(0);

        h.Sim.Services.BuildingRegistry.SetState(storehouse, BuildingState.Active);
        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(10);
    }

    [Fact]
    public void SiteBonus_NearABuildingOutsideTheRadius_DoesNotApply()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);
        EntityId lookout = h.AddReady("lookout", 10, 10);

        // The nearest tile of a storehouse at (20, 10) is 9 columns from the lookout.
        h.Add("storehouse", 20, 10);
        h.RunTicks(1);

        h.Info(lookout).BonusPercent.ShouldBe(0);
    }

    [Fact]
    public void SiteBonus_NearBuildingsPickedByTag_CanBePenalties()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);
        EntityId lookout = h.AddReady("lookout", 10, 10);
        h.Add("storehouse", 12, 8);

        // A farm at (14, 10) is 3 tiles from the lookout: within the radius of the -5% rule for farms.
        EntityId farm = h.Add("sheep_farm", 14, 10);
        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(10 - 5);

        // The same farm one column further out is not.
        h.Sim.Services.BuildingRegistry.Remove(farm);
        h.Add("sheep_farm", 15, 10);
        h.RunTicks(1);
        h.Info(lookout).BonusPercent.ShouldBe(10);
    }
}
