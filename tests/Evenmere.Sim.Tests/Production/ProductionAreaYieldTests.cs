using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionAreaYieldTests
{
    // The woodcutter's hut counts forest tiles within 4 tiles of its footprint and needs 24 for a full yield.
    private static void SetForest(ProductionHarness h, int minX, int maxX, int minY, int maxY)
    {
        TileInfo forest = h.Tile("forest");
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                h.Map.SetTile(x, y, forest);
            }
        }
    }

    [Fact]
    public void AreaYield_Woodcutter_IsForestTilesInRadiusOverFullAt()
    {
        var h = ProductionHarness.Create();

        // A block of 12 forest tiles, all within 4 tiles of the hut at (10, 10) to (11, 11).
        SetForest(h, 12, 14, 10, 13);
        EntityId hut = h.AddReady("woodcutters_hut", 10, 10);

        h.RunTicks(1);

        h.Info(hut).AreaYield.PerMille.ShouldBe(500);
    }

    [Fact]
    public void AreaYield_Woodcutter_HalfYieldHalvesTheRate()
    {
        var h = ProductionHarness.Create();
        SetForest(h, 12, 14, 10, 13);
        EntityId hut = h.AddReady("woodcutters_hut", 10, 10);

        // A 20 s cycle is 80,000 progress, at 500 per tick.
        h.RunTicks(159);
        h.Output(hut, "wood").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(hut, "wood").ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void AreaYield_Radius_ReachesTheEdgeOfTheCircleButNotItsSquareCorner()
    {
        var h = ProductionHarness.Create();
        SetForest(h, 12, 14, 10, 13);

        // (15, 10) is exactly 4 tiles from the footprint, so it counts. (15, 13) is 4 across and 2 down: inside a square
        // of radius 4 but 20 > 16 away, so it does not. (16, 10) is 5 away.
        h.Map.SetTile(15, 10, h.Tile("forest"));
        h.Map.SetTile(15, 13, h.Tile("forest"));
        h.Map.SetTile(16, 10, h.Tile("forest"));
        EntityId hut = h.AddReady("woodcutters_hut", 10, 10);

        h.RunTicks(1);

        h.Info(hut).AreaYield.PerMille.ShouldBe(13 * 1000 / 24);
    }

    [Fact]
    public void AreaYield_PlentyOfForest_IsCappedAtFull()
    {
        var h = ProductionHarness.Create();
        h.Map.DefaultTile = h.Tile("forest");
        EntityId hut = h.AddReady("woodcutters_hut", 10, 10);

        h.RunTicks(1);

        h.Info(hut).AreaYield.PerMille.ShouldBe(1000);
    }

    [Fact]
    public void AreaYield_NoMatchingTerrain_MakesNothingAndStallsNoReason()
    {
        var h = ProductionHarness.Create();
        EntityId hut = h.AddReady("woodcutters_hut", 10, 10);

        h.RunTicks(300);

        ProductionInfo info = h.Info(hut);
        info.AreaYield.PerMille.ShouldBe(0);
        info.Productivity.PerMille.ShouldBe(0);
        info.Stall.ShouldBe(StallReason.None);
        h.Output(hut, "wood").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void AreaYield_Farm_IsTheFertileShareOfTheFootprint()
    {
        var h = ProductionHarness.Create();
        uint grain = TileInfo.FertilityBit(CropKind.Grain);
        uint flax = TileInfo.FertilityBit(CropKind.Flax);

        // The farm covers (5, 5) to (9, 9): 25 tiles. Ten are fertile for grain, and five more only for flax.
        for (int i = 0; i < 10; i++)
        {
            h.Map.SetTile(5 + (i % 5), 5 + (i / 5), h.Tile("plains", grain));
        }

        for (int i = 0; i < 5; i++)
        {
            h.Map.SetTile(5 + i, 7, h.Tile("plains", flax));
        }

        EntityId farm = h.AddReady("grain_farm", 5, 5);

        // A 40 s cycle is 160,000 progress, at 400 per tick: the first grain on tick 400.
        h.RunTicks(399);
        h.Info(farm).AreaYield.PerMille.ShouldBe(400);
        h.Output(farm, "grain").ShouldBe(Amount.Zero);

        h.RunTicks(1);
        h.Output(farm, "grain").ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void AreaYield_DepositGatherer_IsTheShareOfFootprintOverTheDeposit()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);

        // The quarry covers (4, 4) to (5, 7): 8 tiles. Six lie over stone and two over iron, which does not count.
        for (int i = 0; i < 6; i++)
        {
            h.Map.SetTile(4 + (i % 2), 4 + (i / 2), h.Tile("plains", deposit: DepositKind.Stone));
        }

        h.Map.SetTile(4, 7, h.Tile("plains", deposit: DepositKind.Iron));
        h.Map.SetTile(5, 7, h.Tile("plains", deposit: DepositKind.Iron));
        EntityId quarry = h.AddReady("test_quarry", 4, 4);

        h.RunTicks(1);

        h.Info(quarry).AreaYield.PerMille.ShouldBe(750);
    }

    [Fact]
    public void AreaYield_BuildingWithoutAYieldRule_IsFull()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(1);

        h.Info(farm).AreaYield.PerMille.ShouldBe(1000);
    }

    // --- Fishing huts: water within 5 tiles that no lower-ID hut also reaches ---

    // A pond of sea at x 12 to 17, y 10 to 13 (24 tiles) in a plains map, with three huts around it. Hut A at (10, 11) reaches
    // 18 pond tiles, hut B at (14, 15) reaches 20 and hut C at (19, 11) reaches 14 (worked out by hand from dx² + dy² ≤ 25).
    private static ProductionHarness PondWorld()
    {
        var h = ProductionHarness.Create();
        TileInfo sea = h.Tile("sea");
        for (int y = 10; y <= 13; y++)
        {
            for (int x = 12; x <= 17; x++)
            {
                h.Map.SetTile(x, y, sea);
            }
        }

        return h;
    }

    private static int Yield(ProductionHarness h, EntityId hut) => h.Info(hut).AreaYield.PerMille;

    [Fact]
    public void AreaYield_OverlappingFishingHuts_ShareTheWaterByID()
    {
        ProductionHarness h = PondWorld();
        EntityId a = h.AddReady("fishing_hut", 10, 11);
        EntityId b = h.AddReady("fishing_hut", 14, 15);
        EntityId c = h.AddReady("fishing_hut", 19, 11);

        // A, the lowest ID, keeps all 18 tiles it reaches. B gets the 4 of its 20 that A cannot reach and C the 2 that A and B
        // cannot. 18 + 4 + 2 = 24: every pond tile is counted by exactly one hut.
        Yield(h, a).ShouldBe(18 * 1000 / 30);
        Yield(h, b).ShouldBe(4 * 1000 / 30);
        Yield(h, c).ShouldBe(2 * 1000 / 30);
    }

    [Fact]
    public void AreaYield_FishingHuts_DoNotDependOnTheOrderTheyActivateIn()
    {
        ProductionHarness h = PondWorld();
        EntityId a = h.Add("fishing_hut", 10, 11, BuildingState.Site);
        EntityId b = h.Add("fishing_hut", 14, 15, BuildingState.Site);
        EntityId c = h.Add("fishing_hut", 19, 11, BuildingState.Site);

        // Finish them highest ID first, so each is computed while the lower ones are still sites and claim nothing.
        h.Sim.Services.BuildingRegistry.SetState(c, BuildingState.Active);
        Yield(h, c).ShouldBe(14 * 1000 / 30);
        h.Sim.Services.BuildingRegistry.SetState(b, BuildingState.Active);
        h.Sim.Services.BuildingRegistry.SetState(a, BuildingState.Active);

        Yield(h, a).ShouldBe(18 * 1000 / 30);
        Yield(h, b).ShouldBe(4 * 1000 / 30);
        Yield(h, c).ShouldBe(2 * 1000 / 30);
    }

    [Fact]
    public void AreaYield_RemovingALowerHut_ReleasesItsWaterToTheOthers()
    {
        ProductionHarness h = PondWorld();
        EntityId a = h.AddReady("fishing_hut", 10, 11);
        EntityId b = h.AddReady("fishing_hut", 14, 15);
        EntityId c = h.AddReady("fishing_hut", 19, 11);

        h.Sim.Services.BuildingRegistry.Remove(a);

        // B now reaches all 20 of its tiles. C's 14 are mostly B's, so C keeps only the 2 it always had.
        Yield(h, b).ShouldBe(20 * 1000 / 30);
        Yield(h, c).ShouldBe(2 * 1000 / 30);
    }

    [Fact]
    public void AreaYield_ANewerHutBesideOlderOnes_GetsOnlyTheLeftovers()
    {
        ProductionHarness h = PondWorld();
        EntityId a = h.AddReady("fishing_hut", 10, 11);
        h.AddReady("fishing_hut", 14, 15);
        h.AddReady("fishing_hut", 19, 11);
        h.Sim.Services.BuildingRegistry.Remove(a);

        // A new hut where A stood reaches 18 tiles, but B and C already claim all except the two at (12, 10) and (13, 10).
        EntityId d = h.AddReady("fishing_hut", 10, 11);

        Yield(h, d).ShouldBe(2 * 1000 / 30);
    }

    [Fact]
    public void AreaYield_AHutOfAnotherKind_ClaimsNothing()
    {
        ProductionHarness h = PondWorld();
        h.AddReady("woodcutters_hut", 10, 11);
        EntityId hut = h.AddReady("fishing_hut", 14, 15);

        // Only fishing huts claim water from one another.
        Yield(h, hut).ShouldBe(20 * 1000 / 30);
    }
}
