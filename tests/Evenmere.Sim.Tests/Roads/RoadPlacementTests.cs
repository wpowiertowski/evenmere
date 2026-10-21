using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>Road validation (the ghost preview) and the PlaceRoad and RemoveRoad handlers, which share one check.</summary>
public sealed class RoadPlacementTests
{
    private static TileCoord T(int x, int y) => new(x, y);

    private static TileCoord[] Row(int y, int from, int to) => RoadsHarness.Straight((from, y), (to, y));

    private static PlacementResult Validate(RoadsHarness h, TileCoord[] path, BuildingTypeId? type = null) =>
        h.Roads.Placement.ValidateRoad(path, type ?? RoadsHarness.Dirt);

    // --- Validation: what the ghost shows ---

    [Fact]
    public void ValidateRoad_FreePath_CanBePlaced()
    {
        var h = new RoadsHarness();

        PlacementResult result = Validate(h, Row(5, 2, 8));

        result.CanPlace.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateRoad_EmptyPath_IsOk()
    {
        var h = new RoadsHarness();

        h.Roads.Placement.ValidateRoad([], RoadsHarness.Dirt).CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void ValidateRoad_TilesOffTheMap_ReportOutOfBounds()
    {
        var h = new RoadsHarness();

        PlacementResult result = Validate(h, Row(5, 30, 33));

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.OutOfBounds);
        error.Tiles.ShouldBe([T(32, 5), T(33, 5)]);
    }

    [Fact]
    public void ValidateRoad_TileUnderABuilding_ReportsOccupied()
    {
        var h = new RoadsHarness();
        h.Add("storehouse", 5, 5);

        PlacementResult result = Validate(h, Row(5, 3, 8));

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.Occupied);
        error.Tiles.ShouldBe([T(5, 5), T(6, 5)]);
    }

    [Fact]
    public void ValidateRoad_SiteAndRuin_AlsoCountAsOccupied()
    {
        var h = new RoadsHarness();
        h.Add("storehouse", 5, 5, BuildingState.Site);
        h.Add("hut", 8, 5, BuildingState.Ruin);

        PlacementResult result = Validate(h, Row(5, 4, 9));

        result.Errors.ShouldHaveSingleItem().Tiles.ShouldBe([T(5, 5), T(6, 5), T(8, 5)]);
    }

    [Fact]
    public void ValidateRoad_RiverTerrain_IsRefusedUntilBridges()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "river");

        PlacementResult result = Validate(h, Row(3, 2, 6));

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
        error.Tiles.ShouldBe([T(4, 3)]);
    }

    [Fact]
    public void ValidateRoad_RiverFeatureOnOtherwiseBuildableGround_IsRefused()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "plains", TileFeatures.River);

        PlacementResult result = Validate(h, Row(3, 2, 6));

        result.Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    [Fact]
    public void ValidateRoad_Sea_IsRefused()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "sea");

        Validate(h, Row(3, 2, 6)).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    [Fact]
    public void ValidateRoad_AccessLockedHighlands_IsRefusedUntilResearchOpensThem()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "highlands");
        h.SetTerrain(5, 3, "highlands");

        PlacementResult locked = Validate(h, Row(3, 2, 6));
        h.Sim.Queries.Progression.Open(TerrainAccess.Highlands);
        PlacementResult open = Validate(h, Row(3, 2, 6));

        PlacementFinding error = locked.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.TerrainAccessLocked);
        error.Tiles.ShouldBe([T(4, 3), T(5, 3)]);
        open.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void ValidateRoad_LockedGroundThatIsAlsoUnfit_ReportsLockedFirstThenUnfitOnceOpen()
    {
        // Marsh is locked behind Drainage and also blocks roads. Research is what changes the answer, so the player is told that first.
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "marsh");

        Validate(h, Row(3, 3, 5)).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainAccessLocked);
        h.Sim.Queries.Progression.Open(TerrainAccess.MarshDrainage);
        Validate(h, Row(3, 3, 5)).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    [Fact]
    public void ValidateRoad_RoadTypeNotUnlocked_RefusesTheWholePath()
    {
        var h = new RoadsHarness();
        TileCoord[] path = Row(5, 2, 6);

        PlacementFinding error = Validate(h, path, RoadsHarness.Cobbled).Errors.ShouldHaveSingleItem();

        error.Issue.ShouldBe(PlacementIssue.NotUnlocked);
        error.Tiles.ShouldBe(path);

        h.Unlock(RoadsHarness.Cobbled);
        Validate(h, path, RoadsHarness.Cobbled).CanPlace.ShouldBeTrue();
    }

    [Theory]
    [InlineData("storehouse")]
    [InlineData("settler_house")]
    public void ValidateRoad_BuildingTypeThatIsNotARoad_IsRefused(string type)
    {
        var h = new RoadsHarness();
        BuildingTypeId id = RoadContent.Building(type);
        h.Unlock(id);

        Validate(h, Row(5, 2, 4), id).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.NotUnlocked);
    }

    [Fact]
    public void ValidateRoad_UnknownBuildingType_IsRefusedWithoutThrowing()
    {
        var h = new RoadsHarness();

        Validate(h, Row(5, 2, 4), new BuildingTypeId(ushort.MaxValue)).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.NotUnlocked);
    }

    [Fact]
    public void ValidateRoad_SeveralIssues_AreGroupedInTheOrderTheyFirstOccurAlongThePath()
    {
        var h = new RoadsHarness();
        h.SetTerrain(6, 5, "river");
        h.SetTerrain(4, 5, "river");
        h.Add("hut", 5, 5);

        // Walking east to west: the river at 6 comes first, then the hut at 5, then the river at 4.
        PlacementResult result = Validate(h, Row(5, 8, 2));

        result.Errors.Length.ShouldBe(2);
        result.Errors[0].Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
        result.Errors[0].Tiles.ShouldBe([T(4, 5), T(6, 5)], "tiles of a finding are in ascending (Y, X) order, not path order");
        result.Errors[1].Issue.ShouldBe(PlacementIssue.Occupied);
        result.Errors[1].Tiles.ShouldBe([T(5, 5)]);
    }

    [Fact]
    public void ValidateRoad_FirstErrorHoldsTheTileWhereTheValidPrefixEnds()
    {
        var h = new RoadsHarness();
        h.SetTerrain(5, 5, "river");
        h.Add("hut", 3, 5);

        // The hut at x = 3 comes before the river at x = 5, so Occupied must be the first error even though a river is the
        // earlier rule in any fixed order of rules.
        PlacementResult result = Validate(h, Row(5, 1, 7));

        result.Errors[0].Issue.ShouldBe(PlacementIssue.Occupied);
        result.Errors[0].Tiles.ShouldContain(T(3, 5));
    }

    [Fact]
    public void ValidateRoad_ARoadOverAnExistingRoad_IsFine()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled);
        h.PlaceDirt((5, 5), (6, 5));

        Validate(h, Row(5, 4, 7), RoadsHarness.Cobbled).CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void ValidateRoad_AsAQuery_DoesNotChangeTheState()
    {
        var h = new RoadsHarness();
        ulong before = h.StateHash();

        Validate(h, Row(5, 2, 8));
        Validate(h, Row(5, 30, 40));

        h.StateHash().ShouldBe(before);
        h.Sim.World.Roads.Layer.ShouldBeEmpty();
    }

    // --- PlaceRoad ---

    [Fact]
    public void PlaceRoad_ValidPath_LaysTheRoadAndEmitsRoadsChanged()
    {
        var h = new RoadsHarness();

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad([T(2, 3), T(3, 3), T(4, 3), T(4, 4), T(4, 5)], RoadsHarness.Dirt));

        foreach (TileCoord tile in new[] { T(2, 3), T(3, 3), T(4, 3), T(4, 4), T(4, 5) })
        {
            h.Queries.RoadAt(tile.X, tile.Y).ShouldBe(RoadsHarness.Dirt);
        }

        h.Queries.RoadAt(5, 3).ShouldBeNull();
        RoadsChanged changed = events.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        changed.Min.ShouldBe(T(2, 3));
        changed.Max.ShouldBe(T(4, 5));
    }

    [Fact]
    public void PlaceRoad_PathWithABadTile_PlacesOnlyTheValidPrefix()
    {
        var h = new RoadsHarness();
        h.SetTerrain(5, 3, "river");

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 2, 8), RoadsHarness.Dirt));

        h.Queries.RoadAt(2, 3).ShouldBe(RoadsHarness.Dirt);
        h.Queries.RoadAt(4, 3).ShouldBe(RoadsHarness.Dirt);
        h.Queries.RoadAt(5, 3).ShouldBeNull();
        h.Queries.RoadAt(6, 3).ShouldBeNull("tiles after the first bad one are not placed even when they are fine");
        h.Queries.RoadAt(8, 3).ShouldBeNull();
        RoadsChanged changed = events.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        (changed.Min, changed.Max).ShouldBe((T(2, 3), T(4, 3)));
        h.Sim.Queries.Progression.Counter(CounterKey.RoadTilesLaid).ShouldBe(3);
    }

    [Fact]
    public void PlaceRoad_FirstTileInvalid_IsRejectedAndChangesNothing()
    {
        var h = new RoadsHarness();
        h.SetTerrain(2, 3, "river");
        ulong before = h.StateHash();

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 2, 6), RoadsHarness.Dirt));

        CommandRejected rejected = events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.PlacementInvalid);
        rejected.Detail.ShouldContain("TerrainNotAllowed");
        rejected.Detail.ShouldContain("(2, 3)");
        h.StateHash().ShouldBe(before);
        h.Queries.RoadAt(3, 3).ShouldBeNull();
    }

    [Fact]
    public void PlaceRoad_RoadTypeNotUnlocked_IsRejected()
    {
        var h = new RoadsHarness();

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 2, 6), RoadsHarness.Paved));

        events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.PlacementInvalid);
        h.Queries.RoadAt(2, 3).ShouldBeNull();
    }

    [Fact]
    public void PlaceRoad_TypeThatIsNotARoad_IsRejectedAsInvalid()
    {
        var h = new RoadsHarness();

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 2, 6), RoadContent.Building("storehouse")));

        CommandRejected rejected = events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.Invalid);
        rejected.Detail.ShouldContain("storehouse");
    }

    [Fact]
    public void PlaceRoad_EmptyPath_IsRejectedAsInvalid()
    {
        var h = new RoadsHarness();

        h.Send(new PlaceRoad([], RoadsHarness.Dirt)).ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void PlaceRoad_LockedHighlands_AreRejectedUntilAccessIsOpened()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "highlands");

        IReadOnlyList<SimEvent> refused = h.Send(new PlaceRoad(Row(3, 4, 6), RoadsHarness.Dirt));
        h.Sim.Queries.Progression.Open(TerrainAccess.Highlands);
        IReadOnlyList<SimEvent> allowed = h.Send(new PlaceRoad(Row(3, 4, 6), RoadsHarness.Dirt));

        CommandRejected rejected = refused.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.PlacementInvalid);
        rejected.Detail.ShouldContain("TerrainAccessLocked");
        allowed.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        h.Queries.RoadAt(4, 3).ShouldBe(RoadsHarness.Dirt);
    }

    [Fact]
    public void PlaceRoad_RiverTiles_AreRejected()
    {
        var h = new RoadsHarness();
        h.SetTerrain(4, 3, "river");

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 4, 6), RoadsHarness.Dirt));

        events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.PlacementInvalid);
        h.Queries.RoadAt(4, 3).ShouldBeNull();
    }

    [Fact]
    public void PlaceRoad_OverARoadOfAnotherType_UpgradesIt()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled);
        h.PlaceDirt((2, 3), (3, 3), (4, 3));

        IReadOnlyList<SimEvent> events = h.Send(new PlaceRoad(Row(3, 3, 5), RoadsHarness.Cobbled));

        h.Queries.RoadAt(2, 3).ShouldBe(RoadsHarness.Dirt);
        h.Queries.RoadAt(3, 3).ShouldBe(RoadsHarness.Cobbled);
        h.Queries.RoadAt(5, 3).ShouldBe(RoadsHarness.Cobbled);
        RoadsChanged changed = events.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        (changed.Min, changed.Max).ShouldBe((T(3, 3), T(5, 3)));
    }

    [Fact]
    public void PlaceRoad_UpgradeOnlyCountsTheNewlyLaidTiles()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled);

        h.PlaceDirt((2, 3), (3, 3), (4, 3));
        h.Send(new PlaceRoad(Row(3, 3, 5), RoadsHarness.Cobbled));

        // Three dirt tiles, then cobbled over two of them (upgrades) and one new tile: four tiles were ever laid.
        h.Sim.Queries.Progression.Counter(CounterKey.RoadTilesLaid).ShouldBe(4);
    }

    [Fact]
    public void PlaceRoad_SameRoadAgain_ChangesNothingAndSaysNothing()
    {
        var h = new RoadsHarness();
        h.PlaceDirt((2, 3), (3, 3));
        ulong before = h.StateHash();

        IReadOnlyList<SimEvent> events = h.PlaceDirt((2, 3), (3, 3));

        events.ShouldBeEmpty();
        h.StateHash().ShouldBe(before);
        h.Sim.Queries.Progression.Counter(CounterKey.RoadTilesLaid).ShouldBe(2);
    }

    [Fact]
    public void PlaceRoad_MarksTheChunksOfTheChangedTilesAndTheirNeighboursDirty()
    {
        var h = new RoadsHarness(size: 96);

        h.PlaceDirt((31, 5));

        // The strip at x = 31 joins the strip at x = 32, which lives in the next chunk, so both chunks are redrawn.
        var drained = new ChunkCoord[8];
        int count = h.Sim.Queries.Map.DrainDirtyChunks(drained);
        drained.Take(count).ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 0)]);
    }

    [Fact]
    public void PlaceRoad_AndTheGhostPreview_AgreeOnHowMuchIsPlaced()
    {
        var h = new RoadsHarness();
        h.SetTerrain(7, 5, "river");
        h.Add("hut", 4, 5);
        TileCoord[] path = Row(5, 1, 10);

        PlacementResult preview = Validate(h, path);
        TileCoord[] flagged = [.. preview.Errors.SelectMany(static e => e.Tiles)];
        int firstBad = Array.FindIndex(path, flagged.Contains);
        h.Send(new PlaceRoad(path, RoadsHarness.Dirt));

        int placed = path.Count(t => h.Queries.RoadAt(t.X, t.Y) is not null);
        placed.ShouldBe(firstBad);
        placed.ShouldBe(3);
    }

    // --- RemoveRoad ---

    [Fact]
    public void RemoveRoad_RoadTiles_ClearsThemAndEmitsRoadsChanged()
    {
        var h = new RoadsHarness();
        h.DirtLine((2, 3), (8, 3));

        IReadOnlyList<SimEvent> events = h.RemoveRoads((4, 3), (5, 3), (6, 3));

        h.Queries.RoadAt(3, 3).ShouldBe(RoadsHarness.Dirt);
        h.Queries.RoadAt(4, 3).ShouldBeNull();
        h.Queries.RoadAt(6, 3).ShouldBeNull();
        h.Queries.RoadAt(7, 3).ShouldBe(RoadsHarness.Dirt);
        RoadsChanged changed = events.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        (changed.Min, changed.Max).ShouldBe((T(4, 3), T(6, 3)));
    }

    [Fact]
    public void RemoveRoad_TilesWithoutARoadOrOffTheMap_AreSkipped()
    {
        var h = new RoadsHarness();
        h.DirtLine((2, 3), (4, 3));

        IReadOnlyList<SimEvent> events = h.RemoveRoads((3, 3), (9, 9), (-1, 3), (99, 99));

        h.Queries.RoadAt(3, 3).ShouldBeNull();
        h.Queries.RoadAt(2, 3).ShouldBe(RoadsHarness.Dirt);
        RoadsChanged changed = events.ShouldHaveSingleItem().ShouldBeOfType<RoadsChanged>();
        (changed.Min, changed.Max).ShouldBe((T(3, 3), T(3, 3)));
    }

    [Fact]
    public void RemoveRoad_NoListedTileHasARoad_IsRejectedAsUnknownTarget()
    {
        var h = new RoadsHarness();
        h.DirtLine((2, 3), (4, 3));

        IReadOnlyList<SimEvent> events = h.RemoveRoads((9, 9), (10, 10));

        events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.UnknownTarget);
        h.Queries.RoadAt(3, 3).ShouldBe(RoadsHarness.Dirt);
    }

    [Fact]
    public void RemoveRoad_BeforeAnyRoadWasLaid_IsRejectedWithoutTouchingTheMap()
    {
        var h = new RoadsHarness();

        h.RemoveRoads((1, 1)).ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.UnknownTarget);
        h.Sim.World.Roads.Layer.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveRoad_MarksTheChunksDirty()
    {
        var h = new RoadsHarness(size: 96);
        h.DirtLine((60, 40), (62, 40));
        h.Sim.Queries.Map.DrainDirtyChunks(new ChunkCoord[16]);

        h.RemoveRoads((61, 40));

        var drained = new ChunkCoord[4];
        int count = h.Sim.Queries.Map.DrainDirtyChunks(drained);
        drained.Take(count).ShouldBe([new ChunkCoord(1, 1)]);
    }

    // --- The state contract ---

    [Fact]
    public void Layer_AfterPlacingRoads_HoldsTheTypeValuePlusOne()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled);

        h.PlaceDirt((3, 4));
        h.Place(RoadsHarness.Cobbled, (5, 4));

        RoadsState state = h.Sim.World.Roads;
        (state.Width, state.Height).ShouldBe((32, 32));
        state.Layer.Length.ShouldBe(32 * 32);
        state.Layer[(4 * 32) + 3].ShouldBe((ushort)(RoadsHarness.Dirt.Value + 1));
        state.Layer[(4 * 32) + 5].ShouldBe((ushort)(RoadsHarness.Cobbled.Value + 1));
        state.Layer.Count(v => v != 0).ShouldBe(2);
    }

    [Fact]
    public void RoadAt_NoRoadOrOffTheMap_IsNull()
    {
        var h = new RoadsHarness();
        h.PlaceDirt((3, 4));

        h.Queries.RoadAt(0, 0).ShouldBeNull();
        h.Queries.RoadAt(-1, 4).ShouldBeNull();
        h.Queries.RoadAt(3, 400).ShouldBeNull();
        h.Queries.RoadAt(3, 4).ShouldBe(RoadsHarness.Dirt);
    }

    [Fact]
    public void RoadAt_BeforeAnyRoad_IsNullWithoutSizingTheLayer()
    {
        var h = new RoadsHarness();

        h.Queries.RoadAt(3, 4).ShouldBeNull();
        h.Sim.World.Roads.Layer.ShouldBeEmpty();
    }
}
