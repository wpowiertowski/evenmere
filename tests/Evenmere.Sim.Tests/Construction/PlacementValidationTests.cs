using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>One test per placement error and warning code, driven by the building's content data, plus the guarantee that the ghost and the command agree.</summary>
public sealed class PlacementValidationTests
{
    private static PlacementFinding Error(PlacementResult result, PlacementIssue issue) =>
        result.Errors.Where(f => f.Issue == issue).ShouldHaveSingleItem();

    private static PlacementFinding Warning(PlacementResult result, PlacementIssue issue) =>
        result.Warnings.Where(f => f.Issue == issue).ShouldHaveSingleItem();

    private static TileCoord[] Footprint(int x, int y, int width, int height) =>
        [.. BuildingPlacementTiles.Of(x, y, width, height)];

    [Fact]
    public void Validate_OpenPlainsNextToARoadInReach_HasNoErrorsAndNoWarnings()
    {
        var world = ConstructionWorld.Create();
        world.StorehouseWithRoad();
        world.GrantFootprintReach(5, 3, 2, 2);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 3, Rotation.Deg0);

        result.ShouldBe(PlacementResult.Ok);
        result.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Validate_FootprintLeavesTheMap_ReportsOutOfBoundsOnlyOnTheTilesOffTheMap()
    {
        var world = ConstructionWorld.Create();

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 31, 31, Rotation.Deg0);

        result.CanPlace.ShouldBeFalse();
        Error(result, PlacementIssue.OutOfBounds).Tiles.ShouldBe([new TileCoord(32, 31), new TileCoord(31, 32), new TileCoord(32, 32)]);
    }

    [Fact]
    public void Validate_BuildingInTheWay_ReportsOccupiedOnTheSharedTilesOnly()
    {
        var world = ConstructionWorld.Create();
        world.Build("sawpit", 5, 5);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 6, Rotation.Deg0);

        Error(result, PlacementIssue.Occupied).Tiles.ShouldBe([new TileCoord(6, 6)]);
    }

    [Fact]
    public void Validate_SiteAndRuinInTheWay_AlsoCountAsOccupied()
    {
        var world = ConstructionWorld.Create();
        world.Build("sawpit", 5, 5, BuildingState.Site);
        world.Build("sawpit", 10, 5, BuildingState.Ruin);

        Error(world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 5, Rotation.Deg0), PlacementIssue.Occupied).Tiles.Length.ShouldBe(4);
        Error(world.Placement().Validate(ConstructionWorld.Type("sawpit"), 10, 5, Rotation.Deg0), PlacementIssue.Occupied).Tiles.Length.ShouldBe(4);
    }

    [Fact]
    public void Validate_RoadInTheWay_ReportsOccupied()
    {
        var world = ConstructionWorld.Create();
        world.Sim.Queries.Roads.SetRoad(10, 10, ConstructionWorld.Type("dirt_road"));

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 10, 10, Rotation.Deg0);

        Error(result, PlacementIssue.Occupied).Tiles.ShouldBe([new TileCoord(10, 10)]);
    }

    [Fact]
    public void Validate_NonBuildableTerrainWithoutATerrainList_ReportsTerrainNotAllowed()
    {
        var world = ConstructionWorld.Create();
        world.Paint("sea", 3, 3, 1, 1);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 3, 3, Rotation.Deg0);

        Error(result, PlacementIssue.TerrainNotAllowed).Tiles.ShouldBe([new TileCoord(3, 3)]);
        result.Has(PlacementIssue.TerrainAccessLocked).ShouldBeFalse("a locked flag is not mentioned for terrain the building could not use anyway");
    }

    [Fact]
    public void Validate_TerrainOutsideTheBuildingsTerrainList_ReportsTerrainNotAllowed()
    {
        var world = ConstructionWorld.Create();
        world.Paint("hills", 4, 4, 4, 1);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sheep_farm"), 4, 3, Rotation.Deg0);

        Error(result, PlacementIssue.TerrainNotAllowed).Tiles.ShouldBe(Footprint(4, 4, 4, 1));
    }

    [Fact]
    public void Validate_BuildableTerrainWhoseAccessIsLocked_ReportsLockedUntilResearchOpensIt()
    {
        var world = ConstructionWorld.Create();
        world.Paint("highlands", 6, 6, 1, 1);

        PlacementResult locked = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 6, Rotation.Deg0);
        world.Sim.Queries.Progression.Open(TerrainAccess.Highlands);
        PlacementResult opened = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 6, Rotation.Deg0);

        Error(locked, PlacementIssue.TerrainAccessLocked).Tiles.ShouldBe([new TileCoord(6, 6)]);
        locked.Has(PlacementIssue.TerrainNotAllowed).ShouldBeFalse();
        opened.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Validate_SlopeAboveTheLimit_ReportsSlopeTooSteepOnThoseTiles()
    {
        var world = ConstructionWorld.Create();
        world.Edit(8, 8, tile => tile with { Slope = SlopeClass.Moderate });
        world.Edit(9, 8, tile => tile with { Slope = SlopeClass.Gentle });

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 8, 8, Rotation.Deg0);

        Error(result, PlacementIssue.SlopeTooSteep).Tiles.ShouldBe([new TileCoord(8, 8)]);
    }

    [Fact]
    public void Validate_TypeNotUnlocked_ReportsNotUnlockedForTheWholeFootprint()
    {
        var world = ConstructionWorld.Create(unlockAll: false);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 5, Rotation.Deg0);

        Error(result, PlacementIssue.NotUnlocked).Tiles.ShouldBe(Footprint(5, 5, 2, 2));
    }

    [Fact]
    public void Validate_UniqueBuildingAlreadyStanding_ReportsUniqueAlreadyBuilt()
    {
        var world = ConstructionWorld.Create();
        world.Build("test_unique_shrine", 1, 1, BuildingState.Site);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("test_unique_shrine"), 10, 10, Rotation.Deg0);

        Error(result, PlacementIssue.UniqueAlreadyBuilt).Tiles.ShouldBe(Footprint(10, 10, 2, 2));
    }

    [Fact]
    public void Validate_UniqueBuildingBeingUpgradedInto_AlsoCountsAsBuilt()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 1, 1);
        world.State.Sites.Add(market, new SiteRecord { Kind = SiteKind.Upgrade, Target = ConstructionWorld.Type("test_unique_shrine") });

        world.Placement().Validate(ConstructionWorld.Type("test_unique_shrine"), 10, 10, Rotation.Deg0)
            .Has(PlacementIssue.UniqueAlreadyBuilt).ShouldBeTrue();
    }

    [Fact]
    public void Validate_UniqueBuildingNotYetBuilt_IsAllowed()
    {
        var world = ConstructionWorld.Create();

        world.Placement().Validate(ConstructionWorld.Type("test_unique_shrine"), 10, 10, Rotation.Deg0).CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Validate_CoastlineBuildingAwayFromTheCoast_ReportsNeedsCoastlineUntilAFootprintTileIsOnTheCoast()
    {
        var world = ConstructionWorld.Create();

        PlacementResult inland = world.Placement().Validate(ConstructionWorld.Type("fishing_hut"), 5, 5, Rotation.Deg0);
        world.Edit(6, 6, tile => tile with { Features = TileFeatures.Coast });
        PlacementResult shore = world.Placement().Validate(ConstructionWorld.Type("fishing_hut"), 5, 5, Rotation.Deg0);

        Error(inland, PlacementIssue.NeedsCoastline).Tiles.ShouldBe(Footprint(5, 5, 2, 2));
        shore.Has(PlacementIssue.NeedsCoastline).ShouldBeFalse();
    }

    [Fact]
    public void Validate_RiversideBuilding_NeedsARiverTouchingAnEdgeNotACorner()
    {
        var world = ConstructionWorld.Create();
        world.Edit(4, 4, tile => tile with { Features = TileFeatures.River });

        PlacementResult corner = world.Placement().Validate(ConstructionWorld.Type("test_water_mill"), 5, 5, Rotation.Deg0);
        world.Edit(4, 5, tile => tile with { Features = TileFeatures.River });
        PlacementResult edge = world.Placement().Validate(ConstructionWorld.Type("test_water_mill"), 5, 5, Rotation.Deg0);

        Error(corner, PlacementIssue.NeedsRiverside).Tiles.ShouldBe(Footprint(5, 5, 2, 2));
        edge.Has(PlacementIssue.NeedsRiverside).ShouldBeFalse();
    }

    [Fact]
    public void Validate_DepositBuildingWithoutThatDeposit_ReportsNeedsDepositUntilOneTileHasIt()
    {
        var world = ConstructionWorld.Create();
        world.Edit(7, 7, tile => tile with { Deposit = DepositKind.Clay });

        PlacementResult wrongKind = world.Placement().Validate(ConstructionWorld.Type("test_quarry"), 7, 7, Rotation.Deg0);
        world.Edit(8, 8, tile => tile with { Deposit = DepositKind.Stone });
        PlacementResult right = world.Placement().Validate(ConstructionWorld.Type("test_quarry"), 7, 7, Rotation.Deg0);

        Error(wrongKind, PlacementIssue.NeedsDeposit).Tiles.ShouldBe(Footprint(7, 7, 2, 2));
        right.Has(PlacementIssue.NeedsDeposit).ShouldBeFalse();
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void Validate_GrainFarm_NeedsAtLeastAQuarterOfTheFootprintFertile(int fertileTiles, bool allowed)
    {
        var world = ConstructionWorld.Create();
        uint grain = TileInfo.FertilityBit(CropKind.Grain);
        for (int i = 0; i < fertileTiles; i++)
        {
            world.Edit(10 + (i % 5), 10 + (i / 5), tile => tile with { FertilityMask = grain });
        }

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("grain_farm"), 10, 10, Rotation.Deg0);

        result.Has(PlacementIssue.FertilityTooLow).ShouldBe(!allowed);
        if (!allowed)
        {
            // The finding lists the tiles that lack fertility, so the ghost can tint exactly those.
            Error(result, PlacementIssue.FertilityTooLow).Tiles.Length.ShouldBe(25 - fertileTiles);
        }
    }

    [Fact]
    public void Validate_RotationSwapsTheFootprint()
    {
        var world = ConstructionWorld.Create();

        PlacementResult flat = world.Placement().Validate(ConstructionWorld.Type("test_barn"), 29, 30, Rotation.Deg0);
        PlacementResult turned = world.Placement().Validate(ConstructionWorld.Type("test_barn"), 31, 29, Rotation.Deg90);
        PlacementResult turnedOut = world.Placement().Validate(ConstructionWorld.Type("test_barn"), 31, 30, Rotation.Deg90);

        flat.Has(PlacementIssue.OutOfBounds).ShouldBeFalse();
        turned.Has(PlacementIssue.OutOfBounds).ShouldBeFalse();
        Error(turnedOut, PlacementIssue.OutOfBounds).Tiles.ShouldBe([new TileCoord(31, 32)]);
    }

    [Fact]
    public void Validate_ErrorsAreAllReported_InTheOrderTheRulesAreChecked()
    {
        var world = ConstructionWorld.Create(unlockAll: false);
        world.Paint("sea", 31, 31, 1, 1);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("fishing_hut"), 31, 31, Rotation.Deg0);

        result.Errors.Select(static f => f.Issue).ShouldBe(
        [
            PlacementIssue.OutOfBounds, PlacementIssue.TerrainNotAllowed, PlacementIssue.NotUnlocked, PlacementIssue.NeedsCoastline,
        ]);
    }

    [Fact]
    public void Validate_BlockedPlacement_ReportsNoWarnings()
    {
        var world = ConstructionWorld.Create(unlockAll: false);

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 5, Rotation.Deg0);

        result.CanPlace.ShouldBeFalse();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NoRoadTouchesTheFootprint_WarnsNoRoadAccess()
    {
        var world = ConstructionWorld.Create();
        world.StorehouseWithRoad();

        // A road that touches only a corner gives no access.
        world.Sim.Queries.Roads.SetRoad(14, 14, ConstructionWorld.Type("dirt_road"));
        PlacementResult cornerOnly = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 15, 15, Rotation.Deg0);
        PlacementResult touching = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 3, Rotation.Deg0);

        cornerOnly.CanPlace.ShouldBeTrue();
        Warning(cornerOnly, PlacementIssue.NoRoadAccess).Tiles.ShouldBe(Footprint(15, 15, 2, 2));
        touching.Has(PlacementIssue.NoRoadAccess).ShouldBeFalse();
    }

    [Fact]
    public void Validate_BuildingThatNeedsNoRoad_NeverWarnsAboutRoadsOrReach()
    {
        var world = ConstructionWorld.Create();

        world.Placement().Validate(ConstructionWorld.Type("test_instant"), 5, 5, Rotation.Deg0).ShouldBe(PlacementResult.Ok);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, false)]
    [InlineData(22, true)]
    public void Validate_RoadRunsPastTheStorehouseReach_WarnsOutsideStorehouseReach(int x, bool outside)
    {
        // The storehouse (reach 20) stands at (0, 0) and a dirt road runs along row 2. A building whose access tile is at
        // column x is x − 1 tiles of road from the storehouse's own access tile.
        var world = ConstructionWorld.Create();
        world.StorehouseWithRoad();
        if (!outside)
        {
            world.GrantFootprintReach(x, 3, 2, 2);
        }

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), x, 3, Rotation.Deg0);

        result.CanPlace.ShouldBeTrue();
        result.Has(PlacementIssue.NoRoadAccess).ShouldBeFalse();
        result.Has(PlacementIssue.OutsideStorehouseReach).ShouldBe(outside);
        if (outside)
        {
            Warning(result, PlacementIssue.OutsideStorehouseReach).Tiles.ShouldBe(Footprint(x, 3, 2, 2));
        }
    }

    [Fact]
    public void Validate_RoadNotConnectedToAnyStorehouse_WarnsOutsideStorehouseReach()
    {
        var world = ConstructionWorld.Create();
        world.StorehouseWithRoad();
        for (int x = 4; x < 12; x++)
        {
            world.Sim.Queries.Roads.SetRoad(x, 10, ConstructionWorld.Type("dirt_road"));
        }

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 11, Rotation.Deg0);

        result.Has(PlacementIssue.OutsideStorehouseReach).ShouldBeTrue();
    }

    [Fact]
    public void Validate_NoFinishedStorehouseAnywhere_WarnsOutsideStorehouseReach()
    {
        var world = ConstructionWorld.Create();
        world.StorehouseWithRoad(storehouseState: BuildingState.Site);

        world.Placement().Validate(ConstructionWorld.Type("sawpit"), 5, 3, Rotation.Deg0).Has(PlacementIssue.OutsideStorehouseReach).ShouldBeTrue();
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(11, true)]
    public void Validate_WoodcutterWithLittleForestAround_WarnsLowYieldBelowHalf(int forestTiles, bool low)
    {
        // Full yield is 24 forest tiles within 4 tiles of the hut; half of that is 12.
        var world = ConstructionWorld.Create();
        for (int i = 0; i < forestTiles; i++)
        {
            world.Paint("forest", 6 + (i % 10), 6 + (i / 10), 1, 1);
        }

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("woodcutters_hut"), 10, 10, Rotation.Deg0);

        result.CanPlace.ShouldBeTrue();
        result.Has(PlacementIssue.LowYield).ShouldBe(low);
    }

    [Fact]
    public void Validate_FishingHutSharingWaterWithAnother_WarnsLowYield()
    {
        // Exclusive water: tiles already inside another hut's window do not count for the new one.
        var world = ConstructionWorld.Create();
        world.Paint("sea", 14, 5, 12, 12);
        foreach (int y in new[] { 10, 11, 13, 14 })
        {
            world.Edit(13, y, tile => tile with { Features = TileFeatures.Coast });
        }

        PlacementResult alone = world.Placement().Validate(ConstructionWorld.Type("fishing_hut"), 12, 13, Rotation.Deg0);
        world.Build("fishing_hut", 12, 10);
        PlacementResult crowded = world.Placement().Validate(ConstructionWorld.Type("fishing_hut"), 12, 13, Rotation.Deg0);

        alone.Has(PlacementIssue.LowYield).ShouldBeFalse();
        crowded.Has(PlacementIssue.LowYield).ShouldBeTrue();
    }

    [Fact]
    public void Validate_FarmWithFewFertileTiles_WarnsLowYield()
    {
        var world = ConstructionWorld.Create();
        uint grain = TileInfo.FertilityBit(CropKind.Grain);
        for (int i = 0; i < 8; i++)
        {
            world.Edit(10 + (i % 5), 10 + (i / 5), tile => tile with { FertilityMask = grain });
        }

        PlacementResult result = world.Placement().Validate(ConstructionWorld.Type("grain_farm"), 10, 10, Rotation.Deg0);

        result.CanPlace.ShouldBeTrue();
        result.Has(PlacementIssue.LowYield).ShouldBeTrue();
    }

    [Fact]
    public void Validate_QuarryOverMostlyDeposit_DoesNotWarnLowYield()
    {
        var world = ConstructionWorld.Create();
        world.Edit(7, 7, tile => tile with { Deposit = DepositKind.Stone });
        world.Edit(8, 7, tile => tile with { Deposit = DepositKind.Stone });

        world.Placement().Validate(ConstructionWorld.Type("test_quarry"), 7, 7, Rotation.Deg0).Has(PlacementIssue.LowYield).ShouldBeFalse();

        world.Edit(8, 7, tile => tile with { Deposit = null });
        world.Placement().Validate(ConstructionWorld.Type("test_quarry"), 7, 7, Rotation.Deg0).Has(PlacementIssue.LowYield).ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownTypeOrRotation_Throws()
    {
        var world = ConstructionWorld.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => world.Placement().Validate(new BuildingTypeId(9999), 0, 0, Rotation.Deg0));
        Should.Throw<ArgumentOutOfRangeException>(() => world.Placement().Validate(ConstructionWorld.Type("sawpit"), 0, 0, (Rotation)4));
    }

    [Fact]
    public void PlaceBuilding_ValidPlacement_RegistersASiteAndEmitsBuildingPlaced()
    {
        var world = ConstructionWorld.Create();
        var activated = new List<BuildingInfo>();
        world.Sim.Services.BuildingLifecycle.Subscribe(new RecordingListener(activated, [], []));

        EntityId site = world.Place("sawpit", 5, 5, Rotation.Deg90);

        site.IsNone.ShouldBeFalse();
        BuildingInfo info = world.Info(site);
        (info.State, info.Stage, info.Type, info.Origin, info.Rotation).ShouldBe(
            (BuildingState.Site, 0, ConstructionWorld.Type("sawpit"), new TileCoord(5, 5), Rotation.Deg90));
        BuildingPlaced placed = world.Sim.DrainEvents().OfType<BuildingPlaced>().ShouldHaveSingleItem();
        placed.ShouldBe(new BuildingPlaced(1, site, ConstructionWorld.Type("sawpit"), new TileCoord(5, 5), Rotation.Deg90));
        activated.ShouldBeEmpty("a site raises no activation until it is finished");
    }

    [Fact]
    public void PlaceBuilding_NeedsNoStockUpFront()
    {
        var world = ConstructionWorld.Create();

        world.Place("sawpit", 5, 5).IsNone.ShouldBeFalse();
    }

    [Fact]
    public void PlaceBuilding_PlacementWithErrors_IsRefusedAndNamesTheFirstError()
    {
        var world = ConstructionWorld.Create();
        world.Build("sawpit", 5, 5);

        world.Place("sawpit", 6, 5);

        CommandRejected rejection = world.Rejection().ShouldNotBeNull();
        rejection.Code.ShouldBe(RejectReason.PlacementInvalid);
        rejection.Detail.ShouldContain("Occupied");
        rejection.Detail.ShouldContain("(6, 5)");
        world.Sim.Queries.Map.AllBuildings.Count.ShouldBe(1);
    }

    [Fact]
    public void PlaceBuilding_RoadType_IsRefusedAsNotApplicable()
    {
        var world = ConstructionWorld.Create();

        world.Place("dirt_road", 5, 5);

        CommandRejected rejection = world.Rejection().ShouldNotBeNull();
        rejection.Code.ShouldBe(RejectReason.NotApplicable);
        rejection.Detail.ShouldContain("PlaceRoad");
        world.Sim.Queries.Map.AllBuildings.ShouldBeEmpty();
    }

    [Fact]
    public void PlaceBuilding_UnknownTypeOrBadRotation_IsRefusedAsInvalid()
    {
        var world = ConstructionWorld.Create();

        world.Enqueue(new PlaceBuilding(new BuildingTypeId(9999), 5, 5, Rotation.Deg0));
        world.Enqueue(new PlaceBuilding(ConstructionWorld.Type("sawpit"), 5, 5, (Rotation)7));
        world.Tick();

        CommandRejected[] rejections = [.. world.Sim.DrainEvents().OfType<CommandRejected>()];
        rejections.Select(static r => r.Code).ShouldBe([RejectReason.Invalid, RejectReason.Invalid]);
    }

    [Fact]
    public void PlaceBuilding_TwoCommandsForTheSameSpotInOneTick_AcceptOnlyTheFirst()
    {
        var world = ConstructionWorld.Create();

        world.Enqueue(new PlaceBuilding(ConstructionWorld.Type("sawpit"), 5, 5, Rotation.Deg0));
        world.Enqueue(new PlaceBuilding(ConstructionWorld.Type("sawpit"), 6, 5, Rotation.Deg0));
        world.Tick();

        world.Sim.Queries.Map.AllBuildings.Count.ShouldBe(1);
        world.Sim.DrainEvents().OfType<CommandRejected>().ShouldHaveSingleItem().Code.ShouldBe(RejectReason.PlacementInvalid);
    }

    [Fact]
    public void GhostAndCommand_ForManyPlacements_AgreeOnWhetherAndWhy()
    {
        // The ghost (IBuildingPlacement, as the roster wires it) and the command run the same code, so for every spot below
        // the command is accepted exactly when the ghost has no error, and a refusal names the ghost's first error.
        var world = ConstructionWorld.Create();
        world.Paint("sea", 0, 20, 4, 4);
        world.Paint("highlands", 8, 20, 4, 4);
        world.Edit(14, 20, tile => tile with { Slope = SlopeClass.Steep });
        world.Build("sawpit", 20, 20);
        var ghost = new BuildingPlacement(
            ConstructionWorld.Content, world.State, world.Sim.Queries.Map, world.Sim.Queries.Roads, world.Sim.Queries.Progression);
        (int X, int Y)[] spots = [(5, 5), (31, 31), (1, 21), (9, 21), (14, 20), (20, 21), (12, 12), (-1, 0)];

        foreach ((int x, int y) in spots)
        {
            PlacementResult expected = ghost.Validate(ConstructionWorld.Type("sawpit"), x, y, Rotation.Deg0);
            world.Sim.DrainEvents();

            world.Enqueue(new PlaceBuilding(ConstructionWorld.Type("sawpit"), x, y, Rotation.Deg0));
            world.Tick();
            CommandRejected? rejection = world.Rejection();

            (rejection is null).ShouldBe(expected.CanPlace, $"spot ({x}, {y})");
            if (rejection is not null)
            {
                rejection.Code.ShouldBe(RejectReason.PlacementInvalid);
                rejection.Detail.ShouldContain(expected.Errors[0].Issue.ToString(), customMessage: $"spot ({x}, {y})");
            }
        }
    }

    [Fact]
    public void GhostAndCommand_AfterAnEarlierCommandInTheSameTick_StillAgree()
    {
        var world = ConstructionWorld.Create();
        BuildingPlacement ghost = world.Placement();
        PlacementResult before = ghost.Validate(ConstructionWorld.Type("sawpit"), 6, 5, Rotation.Deg0);

        world.Place("sawpit", 5, 5);
        PlacementResult after = ghost.Validate(ConstructionWorld.Type("sawpit"), 6, 5, Rotation.Deg0);

        before.CanPlace.ShouldBeTrue();
        after.CanPlace.ShouldBeFalse();
        Error(after, PlacementIssue.Occupied).Tiles.ShouldBe([new TileCoord(6, 5), new TileCoord(6, 6)]);
    }

    [Fact]
    public void PlacementResult_SameFindings_AreEqualSoTheGhostCanBeComparedWithTheCommand()
    {
        var world = ConstructionWorld.Create();
        world.Build("sawpit", 5, 5);

        PlacementResult first = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 6, Rotation.Deg0);
        PlacementResult second = world.Placement().Validate(ConstructionWorld.Type("sawpit"), 6, 6, Rotation.Deg0);

        first.ShouldBe(second);
        first.Errors.Select(static f => f.Tiles).ShouldAllBe(static tiles => tiles.SequenceEqual(tiles.OrderBy(static t => t.Y).ThenBy(static t => t.X)));
    }
}

/// <summary>Lists footprint tiles in the order findings promise (rows top to bottom, left to right within a row).</summary>
internal static class BuildingPlacementTiles
{
    public static ImmutableArray<TileCoord> Of(int x, int y, int width, int height) =>
        BuildingPlacement.Footprint(x, y, new Footprint(width, height));
}
