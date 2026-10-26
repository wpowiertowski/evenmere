using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Tests.Roads.Bridges;

/// <summary>Bridge validation: what the ghost preview reports for river crossings, and what PlaceRoad lays.</summary>
public sealed class BridgeValidationTests
{
    private static TileCoord T(int x, int y) => new(x, y);

    private static TileCoord[] Row(int y, int from, int to) => RoadsHarness.Straight((from, y), (to, y));

    private static RoadsHarness Harness() => new(32, BridgeContent.Db);

    private static TileInfo River(RiverWidthClass width) =>
        new(BridgeContent.Terrain("plains"), 0, SlopeClass.Flat, TileFeatures.River, width, 0, null, 0);

    private static void PaintRiver(RoadsHarness h, int y, int fromX, int toX, RiverWidthClass width)
    {
        for (int x = fromX; x <= toX; x++)
        {
            h.Sim.Queries.Map.SetTile(x, y, River(width));
        }
    }

    private static EntityId Add(RoadsHarness h, string type, int x, int y) =>
        h.Sim.Services.BuildingRegistry.Add(BridgeContent.Building(type), x, y, Rotation.Deg0, BuildingState.Active);

    private static PlacementResult Validate(RoadsHarness h, TileCoord[] path, BuildingTypeId type) =>
        h.Roads.Placement.ValidateRoad(path, type);

    // --- The shared-content assumption ---

    [Fact]
    public void BridgeContent_KeepsTheRoadsPacksRoadAndTerrainIds()
    {
        // RoadsHarness unlocks RoadContent's dirt road in its constructor and paints terrains through RoadContent, so the
        // bridge tests can reuse it only for the IDs both packs number alike.
        BridgeContent.Building("dirt_road").ShouldBe(RoadsHarness.Dirt);
        BridgeContent.Terrain("plains").ShouldBe(RoadContent.Terrain("plains"));
        BridgeContent.Terrain("river").ShouldBe(RoadContent.Terrain("river"));
        BridgeContent.Db.Buildings.Count.ShouldBe(RoadContent.Db.Buildings.Count + 2);
    }

    // --- Successful crossings ---

    [Fact]
    public void TimberBridge_StraightCrossingWithinSpan_CanBePlaced()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);

        PlacementResult result = Validate(h, Row(5, 2, 8), BridgeContent.Timber);

        result.CanPlace.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void StoneBridge_WideCrossingWithinSpan_CanBePlaced()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Stone);
        h.Sim.Queries.Progression.Open(TerrainAccess.WideRivers);
        PaintRiver(h, 5, 4, 11, RiverWidthClass.Wide);

        Validate(h, Row(5, 2, 13), BridgeContent.Stone).CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Bridge_LandApproachesPavedAsBridgeType_CanBePlaced()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);

        // The whole crossing, banks included, is paved as the bridge type; only the river tiles need the span.
        Validate(h, Row(5, 2, 8), BridgeContent.Timber).CanPlace.ShouldBeTrue();
    }

    // --- TerrainAccessLocked ---

    [Fact]
    public void TimberBridge_WithoutResearch_IsTerrainAccessLocked()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);

        PlacementResult result = Validate(h, Row(5, 2, 8), BridgeContent.Timber);

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.TerrainAccessLocked);
        error.Tiles.ShouldBe([T(4, 5), T(5, 5), T(6, 5)]);
    }

    [Fact]
    public void StoneBridge_WideRiverWithOnlyNarrowAccess_IsTerrainAccessLocked()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Stone);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 8, RiverWidthClass.Wide);

        // Narrow-rivers research opens narrow crossings only; a wide river still cites its own lock.
        PlacementResult result = Validate(h, Row(5, 2, 10), BridgeContent.Stone);

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.TerrainAccessLocked);
        error.Tiles.ShouldBe([T(4, 5), T(5, 5), T(6, 5), T(7, 5), T(8, 5)]);

        h.Sim.Queries.Progression.Open(TerrainAccess.WideRivers);
        Validate(h, Row(5, 2, 10), BridgeContent.Stone).CanPlace.ShouldBeTrue();
    }

    // --- A road without a span can never cross ---

    [Fact]
    public void DirtRoad_OnAnOpenRiver_IsTerrainNotAllowed()
    {
        RoadsHarness h = Harness();
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);

        // Research opens rivers to bridges, not to span-less roads.
        Validate(h, Row(5, 2, 8), BridgeContent.Dirt).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    // --- BridgeTooLong ---

    [Fact]
    public void TimberBridge_RunLongerThanItsSpan_IsBridgeTooLong()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber, BridgeContent.Stone);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 7, RiverWidthClass.Narrow);

        // Four river tiles against a span of 3: the whole run is refused, not just the overrun.
        PlacementResult result = Validate(h, Row(5, 2, 9), BridgeContent.Timber);

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.BridgeTooLong);
        error.Tiles.ShouldBe([T(4, 5), T(5, 5), T(6, 5), T(7, 5)]);
    }

    [Fact]
    public void TimberBridge_OnAWideRiver_IsBridgeTooLong()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber, BridgeContent.Stone);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        h.Sim.Queries.Progression.Open(TerrainAccess.WideRivers);
        PaintRiver(h, 5, 4, 8, RiverWidthClass.Wide);

        Validate(h, Row(5, 2, 10), BridgeContent.Timber).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.BridgeTooLong);
        Validate(h, Row(5, 2, 10), BridgeContent.Stone).CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Bridge_RunExactlyAtSpan_CanBePlaced()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Stone);
        h.Sim.Queries.Progression.Open(TerrainAccess.WideRivers);
        PaintRiver(h, 5, 4, 11, RiverWidthClass.Wide);

        Validate(h, Row(5, 2, 13), BridgeContent.Stone).CanPlace.ShouldBeTrue();
    }

    // --- BridgeNotStraight ---

    [Fact]
    public void TimberBridge_BendingRun_IsBridgeNotStraight()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        h.Sim.Queries.Map.SetTile(4, 5, River(RiverWidthClass.Narrow));
        h.Sim.Queries.Map.SetTile(5, 5, River(RiverWidthClass.Narrow));
        h.Sim.Queries.Map.SetTile(5, 6, River(RiverWidthClass.Narrow));

        TileCoord[] path = [T(3, 5), T(4, 5), T(5, 5), T(5, 6), T(5, 7)];
        PlacementResult result = Validate(h, path, BridgeContent.Timber);

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.BridgeNotStraight);
        error.Tiles.ShouldBe([T(4, 5), T(5, 5), T(5, 6)]);
    }

    [Fact]
    public void TimberBridge_DiagonalSteps_AreNotAStraightRun()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        h.Sim.Queries.Map.SetTile(4, 5, River(RiverWidthClass.Narrow));
        h.Sim.Queries.Map.SetTile(5, 6, River(RiverWidthClass.Narrow));

        TileCoord[] path = [T(3, 5), T(4, 5), T(5, 6), T(6, 6)];

        Validate(h, path, BridgeContent.Timber).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.BridgeNotStraight);
    }

    // --- River tiles the span cannot be checked against ---

    [Fact]
    public void Bridge_RiverWithoutAWidth_IsTerrainNotAllowed()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        h.Sim.Queries.Map.SetTile(4, 5, RoadsHarness.Tile("plains", TileFeatures.River));

        // A river tile with no width (a map with no rivers layer) names no span, so nothing crosses it.
        Validate(h, Row(5, 2, 6), BridgeContent.Timber).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    [Fact]
    public void Bridge_TerrainRiverWithoutTheFeature_IsTerrainNotAllowed()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        h.SetTerrain(4, 5, "river");

        // The crossing reads the river feature, never the terrain ID: the M1 stand-in stays uncrossable.
        Validate(h, Row(5, 2, 6), BridgeContent.Timber).Errors.ShouldHaveSingleItem().Issue.ShouldBe(PlacementIssue.TerrainNotAllowed);
    }

    // --- Interaction with the other rules ---

    [Fact]
    public void Bridge_RiverTileUnderABuilding_IsOccupied()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);
        Add(h, "hut", 5, 5);

        PlacementResult result = Validate(h, Row(5, 2, 8), BridgeContent.Timber);

        PlacementFinding error = result.Errors.ShouldHaveSingleItem();
        error.Issue.ShouldBe(PlacementIssue.Occupied);
        error.Tiles.ShouldBe([T(5, 5)]);
    }

    [Fact]
    public void Bridge_TypeNotUnlocked_RefusesTheWholePath()
    {
        RoadsHarness h = Harness();
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);
        TileCoord[] path = Row(5, 2, 8);

        PlacementFinding error = Validate(h, path, BridgeContent.Timber).Errors.ShouldHaveSingleItem();

        error.Issue.ShouldBe(PlacementIssue.NotUnlocked);
        error.Tiles.ShouldBe(path);
    }

    // --- PlaceRoad ---

    [Fact]
    public void PlaceBridge_LaysBridgeTilesAndCountsThem()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 4, 10, 12, RiverWidthClass.Narrow);

        h.Place(BridgeContent.Dirt, (2, 4), (3, 4), (4, 4), (5, 4), (6, 4), (7, 4), (8, 4), (9, 4));
        h.Place(BridgeContent.Timber, (10, 4), (11, 4), (12, 4));
        h.Place(BridgeContent.Dirt, (13, 4), (14, 4));

        h.Queries.RoadAt(9, 4).ShouldBe(BridgeContent.Dirt);
        h.Queries.RoadAt(10, 4).ShouldBe(BridgeContent.Timber);
        h.Queries.RoadAt(11, 4).ShouldBe(BridgeContent.Timber);
        h.Queries.RoadAt(12, 4).ShouldBe(BridgeContent.Timber);
        h.Queries.RoadAt(13, 4).ShouldBe(BridgeContent.Dirt);
        h.Sim.Queries.Progression.Counter(CounterKey.RoadTilesLaid).ShouldBe(13);
    }

    [Fact]
    public void PlaceBridge_PathWithAnOverlongSpan_PlacesOnlyTheValidPrefix()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 3, 4, 7, RiverWidthClass.Narrow);

        h.Send(new PlaceRoad(Row(3, 2, 9), BridgeContent.Timber));

        h.Queries.RoadAt(2, 3).ShouldBe(BridgeContent.Timber);
        h.Queries.RoadAt(3, 3).ShouldBe(BridgeContent.Timber);
        h.Queries.RoadAt(4, 3).ShouldBeNull("the overlong run refuses its own tiles and everything after it");
        h.Queries.RoadAt(8, 3).ShouldBeNull();
        h.Sim.Queries.Progression.Counter(CounterKey.RoadTilesLaid).ShouldBe(2);
    }

    [Fact]
    public void PlaceBridge_AndTheGhostPreview_Agree()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 5, 4, 6, RiverWidthClass.Narrow);
        TileCoord[] path = Row(5, 2, 8);

        PlacementResult preview = Validate(h, path, BridgeContent.Timber);
        h.Send(new PlaceRoad(path, BridgeContent.Timber));

        preview.CanPlace.ShouldBeTrue();
        path.Count(t => h.Queries.RoadAt(t.X, t.Y) is not null).ShouldBe(path.Length);
    }
}
