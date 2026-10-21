using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Map;

public sealed class MapSystemTests
{
    private static TerrainId Terrain(string id) => MapWorld.Content.IdOf<TerrainId>(id);

    [Fact]
    public void NewGame_BeforeAMapIsLoaded_HasAnEmptyMap()
    {
        var world = MapWorld.Empty();

        world.Queries.Width.ShouldBe(0);
        world.Queries.Height.ShouldBe(0);
        world.Queries.BuildingAt(0, 0).ShouldBe(EntityId.None);
        world.AllBuildings().ShouldBeEmpty();
        world.DrainAll().ShouldBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => world.Queries.GetTile(0, 0));
    }

    [Fact]
    public void LoadMap_Fixture_SizesTheGridAndMarksEveryChunkDirty()
    {
        var world = MapWorld.WithFixture("test_16");

        world.Queries.Width.ShouldBe(16);
        world.Queries.Height.ShouldBe(16);
        world.DrainAll().ShouldBe([new ChunkCoord(0, 0)]);
    }

    [Fact]
    public void GetTile_Fixture_CopiesEveryLayer()
    {
        var world = MapWorld.WithFixture("test_16");

        TileInfo hills = world.Queries.GetTile(9, 3);
        hills.Terrain.ShouldBe(Terrain("hills"));
        hills.Elevation.ShouldBe(3);
        hills.Deposit.ShouldBe(DepositKind.Stone);
        hills.Region.ShouldBe((ushort)1);

        TileInfo river = world.Queries.GetTile(7, 10);
        river.Terrain.ShouldBe(Terrain("river"));
        river.Features.ShouldBe(TileFeatures.River);
        river.RiverWidth.ShouldBe(RiverWidthClass.Wide);
        world.Queries.GetTile(7, 5).RiverWidth.ShouldBe(RiverWidthClass.Narrow);
        world.Queries.GetTile(8, 5).RiverWidth.ShouldBe(RiverWidthClass.None);

        TileInfo farmland = world.Queries.GetTile(10, 10);
        farmland.IsFertileFor(CropKind.Grain).ShouldBeTrue();
        farmland.IsFertileFor(CropKind.Flax).ShouldBeFalse();
        farmland.Deposit.ShouldBeNull();
        world.Queries.GetTile(3, 7).IsFertileFor(CropKind.Flax).ShouldBeTrue();
        world.Queries.GetTile(11, 7).Region.ShouldBe((ushort)10);
    }

    [Fact]
    public void GetTile_Fixture_DerivesTheFeaturesOfEachTerrain()
    {
        var world = MapWorld.WithFixture("test_16");

        world.Queries.GetTile(0, 0).Features.ShouldBe(TileFeatures.DeepWater);
        world.Queries.GetTile(15, 14).Features.ShouldBe(TileFeatures.Reef);
        world.Queries.GetTile(3, 10).Features.ShouldBe(TileFeatures.Marsh);
        world.Queries.GetTile(5, 10).Features.ShouldBe(TileFeatures.None);
        world.Queries.GetTile(10, 4).Features.ShouldBe(TileFeatures.None);
        world.Queries.GetTile(8, 2).Features.ShouldBe(TileFeatures.None);
    }

    [Fact]
    public void GetTile_Fixture_MarksLandNextToOpenWaterAsCoast()
    {
        var world = MapWorld.WithFixture("test_16");

        // A beach with sea beside it, and a beach that touches a reef (reefs and deep water are open water too).
        world.Queries.GetTile(1, 5).Features.ShouldBe(TileFeatures.Beach | TileFeatures.Coast);
        world.Queries.GetTile(14, 14).Features.ShouldBe(TileFeatures.Beach | TileFeatures.Coast);
        world.Queries.GetTile(1, 2).Features.ShouldBe(TileFeatures.Beach | TileFeatures.Coast);

        // Plains with only beach and forest around it are inland.
        world.Queries.GetTile(2, 5).Features.ShouldBe(TileFeatures.None);

        // Open water is never coast, and neither is a river.
        world.Queries.GetTile(0, 5).Features.ShouldBe(TileFeatures.None);
        world.Queries.GetTile(7, 3).Features.ShouldBe(TileFeatures.River);
    }

    [Fact]
    public void GetTile_RiverMeetingTheSea_IsRiverButNotCoast()
    {
        // test_32's river starts at the northern edge, so its first tile has sea on both sides.
        var world = MapWorld.WithFixture("test_32");

        world.Queries.GetTile(16, 0).Features.ShouldBe(TileFeatures.River);
        world.Queries.GetTile(15, 1).Features.ShouldBe(TileFeatures.Beach | TileFeatures.Coast);
        world.Queries.GetTile(16, 5).Features.ShouldBe(TileFeatures.River);
    }

    [Theory]
    [InlineData(10, 10, SlopeClass.Flat)]
    [InlineData(1, 5, SlopeClass.Gentle)]
    [InlineData(9, 3, SlopeClass.Gentle)]
    [InlineData(11, 9, SlopeClass.Steep)]
    [InlineData(11, 5, SlopeClass.Steep)]
    [InlineData(12, 8, SlopeClass.Steep)]
    [InlineData(0, 0, SlopeClass.Flat)]
    [InlineData(15, 15, SlopeClass.Flat)]
    public void GetTile_Fixture_DerivesTheSlopeClassFromTheFourNeighbours(int x, int y, SlopeClass expected)
    {
        var world = MapWorld.WithFixture("test_16");

        world.Queries.GetTile(x, y).Slope.ShouldBe(expected);
    }

    [Fact]
    public void GetTile_ModerateSlope_IsTwoSteps()
    {
        // test_32 raises its hills to elevation 4 on plains at 2: the plains tile beside them differs by 2, and inside the hills it is flat.
        var world = MapWorld.WithFixture("test_32");

        world.Queries.GetTile(19, 6).Slope.ShouldBe(SlopeClass.Moderate);
        world.Queries.GetTile(21, 6).Slope.ShouldBe(SlopeClass.Flat);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(16, 0)]
    [InlineData(0, 16)]
    public void GetTile_OffTheMap_Throws(int x, int y)
    {
        var world = MapWorld.WithFixture("test_16");

        Should.Throw<ArgumentOutOfRangeException>(() => world.Queries.GetTile(x, y));
        world.Queries.BuildingAt(x, y).ShouldBe(EntityId.None);
    }

    [Fact]
    public void LoadMap_WhileBuildingsExist_Throws()
    {
        var world = MapWorld.WithFixture("test_16");
        world.Registry.Add(MapWorld.Type("settler_house"), 3, 3, Rotation.Deg0, BuildingState.Site);

        Should.Throw<InvalidOperationException>(() => world.Map.LoadMap(MapLoader.Load(MapWorld.Roots, "test_16", MapWorld.Content).GetMapOrThrow()));
    }

    [Fact]
    public void LoadMap_MapFromOtherContent_Throws()
    {
        // The simulation has the four terrains of the minimal pack; the map was loaded against twelve.
        ContentDb minimal = ContentLoader.Load(WorldBuilder.MinimalPackPath).GetDbOrThrow();
        MapDef foreign = MapLoader.Load(MapWorld.Roots, "test_32", MapWorld.Content).GetMapOrThrow();
        var sim = Simulation.CreateNew(minimal, new NewGameOptions { Seed = 1 });
        var map = (MapSystem)sim.Pipeline.Entries.Single(e => e.Id == SystemId.Map).System;

        Should.Throw<ArgumentException>(() => map.LoadMap(foreign));
        sim.Query.Map.Width.ShouldBe(0);
    }

    [Fact]
    public void LoadMap_Twice_ReplacesTheFirstMap()
    {
        var world = MapWorld.WithFixture("test_16");

        world.Map.LoadMap(MapLoader.Load(MapWorld.Roots, "test_32", MapWorld.Content).GetMapOrThrow());

        world.Queries.Width.ShouldBe(32);
        world.Queries.GetTile(16, 10).RiverWidth.ShouldBe(RiverWidthClass.Narrow);
    }

    [Fact]
    public void TileFeatures_Values_MatchTheMapFormatsTerrainFeatures()
    {
        // MapStateLoader copies the stored bits across without translating them, so the two enums must agree.
        ((byte)TileFeatures.River).ShouldBe((byte)MapTerrainFeatures.River);
        ((byte)TileFeatures.Beach).ShouldBe((byte)MapTerrainFeatures.Beach);
        ((byte)TileFeatures.DeepWater).ShouldBe((byte)MapTerrainFeatures.DeepWater);
        ((byte)TileFeatures.Reef).ShouldBe((byte)MapTerrainFeatures.Reef);
        ((byte)TileFeatures.Marsh).ShouldBe((byte)MapTerrainFeatures.Marsh);
        (MapTerrainRules.StoredFeatureMask & (MapTerrainFeatures)(byte)TileFeatures.Coast).ShouldBe(MapTerrainFeatures.None);
    }
}
