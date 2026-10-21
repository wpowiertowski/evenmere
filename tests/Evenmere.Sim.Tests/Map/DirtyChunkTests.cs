using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Map;

public sealed class DirtyChunkTests
{
    private static readonly BuildingTypeIdHolder _types = new();

    private sealed class BuildingTypeIdHolder
    {
        public Core.Ids.BuildingTypeId House { get; } = MapWorld.Type("settler_house");

        public Core.Ids.BuildingTypeId Hall { get; } = MapWorld.Type("founders_hall");
    }

    private static MapWorld DrainedWorld(int width = 70, int height = 40)
    {
        var world = MapWorld.Plains(width, height);
        world.DrainAll();
        return world;
    }

    [Fact]
    public void LoadMap_MarksEveryChunkInRowMajorOrder()
    {
        var world = MapWorld.Plains(70, 40);

        world.DrainAll().ShouldBe(
        [
            new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(2, 0),
            new ChunkCoord(0, 1), new ChunkCoord(1, 1), new ChunkCoord(2, 1),
        ]);
    }

    [Fact]
    public void Drain_AfterDraining_ReturnsNothing()
    {
        MapWorld world = DrainedWorld();

        world.DrainAll().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(31, 31, 0, 0)]
    [InlineData(32, 0, 1, 0)]
    [InlineData(0, 32, 0, 1)]
    [InlineData(63, 39, 1, 1)]
    [InlineData(64, 39, 2, 1)]
    [InlineData(69, 0, 2, 0)]
    public void MarkTile_MarksTheChunkThatHoldsTheTile(int x, int y, int chunkX, int chunkY)
    {
        MapWorld world = DrainedWorld();

        world.Dirty.MarkTile(x, y);

        world.DrainAll().ShouldBe([new ChunkCoord(chunkX, chunkY)]);
    }

    [Fact]
    public void MarkRect_AcrossChunkBoundaries_MarksEveryChunkItTouches()
    {
        MapWorld world = DrainedWorld();

        world.Dirty.MarkRect(30, 30, 33, 33);

        world.DrainAll().ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(0, 1), new ChunkCoord(1, 1)]);
    }

    [Fact]
    public void MarkRect_EndingExactlyOnTheLastTileOfAChunk_DoesNotSpillOver()
    {
        MapWorld world = DrainedWorld();

        world.Dirty.MarkRect(0, 0, 31, 31);

        world.DrainAll().ShouldBe([new ChunkCoord(0, 0)]);
    }

    [Fact]
    public void MarkRect_PartlyOffTheMap_MarksOnlyChunksOnTheMap()
    {
        MapWorld world = DrainedWorld();

        world.Dirty.MarkRect(-10, -10, 1, 1);
        world.Dirty.MarkRect(60, 30, 500, 500);
        world.Dirty.MarkTile(-1, -1);
        world.Dirty.MarkTile(70, 40);
        world.Dirty.MarkRect(5, 5, 4, 4);

        world.DrainAll().ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(2, 0), new ChunkCoord(1, 1), new ChunkCoord(2, 1)]);
    }

    [Fact]
    public void Mark_SameChunkTwice_IsReportedOnce()
    {
        MapWorld world = DrainedWorld();

        world.Dirty.MarkTile(3, 3);
        world.Dirty.MarkTile(4, 4);
        world.Dirty.MarkRect(0, 0, 10, 10);

        world.DrainAll().ShouldBe([new ChunkCoord(0, 0)]);
    }

    [Fact]
    public void Drain_ShortSpan_ReturnsTheFirstChunksAndKeepsTheRest()
    {
        var world = MapWorld.Plains(70, 40);
        var two = new ChunkCoord[2];

        world.Queries.DrainDirtyChunks(two).ShouldBe(2);
        two.ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 0)]);

        world.Queries.DrainDirtyChunks(two).ShouldBe(2);
        two.ShouldBe([new ChunkCoord(2, 0), new ChunkCoord(0, 1)]);

        world.Dirty.MarkTile(0, 0);

        // The remaining marks and the new one come out together, still in row-major order.
        world.DrainAll().ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 1), new ChunkCoord(2, 1)]);
    }

    [Fact]
    public void Drain_EmptySpan_ReturnsZeroAndForgetsNothing()
    {
        MapWorld world = DrainedWorld();
        world.Dirty.MarkTile(40, 5);

        world.Queries.DrainDirtyChunks(Span<ChunkCoord>.Empty).ShouldBe(0);

        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);
    }

    [Fact]
    public void Add_BuildingAcrossAChunkCorner_MarksAllFourChunks()
    {
        MapWorld world = DrainedWorld();

        world.Registry.Add(_types.Hall, 30, 30, Rotation.Deg0, BuildingState.Site);

        world.DrainAll().ShouldBe([new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(0, 1), new ChunkCoord(1, 1)]);
    }

    [Fact]
    public void Registry_EveryChange_MarksTheBuildingsChunks()
    {
        MapWorld world = DrainedWorld();
        Core.Ids.EntityId id = world.Registry.Add(_types.House, 40, 4, Rotation.Deg0, BuildingState.Site);
        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);

        world.Registry.SetStage(id, 2);
        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);

        world.Registry.SetState(id, BuildingState.Active);
        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);

        world.Registry.SetState(id, BuildingState.Paused);
        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);

        world.Registry.Remove(id);
        world.DrainAll().ShouldBe([new ChunkCoord(1, 0)]);
    }

    [Fact]
    public void Registry_ChangesThatChangeNothing_MarkNothing()
    {
        MapWorld world = DrainedWorld();
        Core.Ids.EntityId id = world.Registry.Add(_types.House, 40, 4, Rotation.Deg0, BuildingState.Active);
        world.DrainAll();

        world.Registry.SetState(id, BuildingState.Active);
        world.Registry.SetType(id, _types.House);
        world.Registry.SetStage(id, 7);
        world.Registry.Remove(new Core.Ids.EntityId(99));

        world.DrainAll().ShouldBeEmpty();
    }

    [Fact]
    public void Marks_AreNotWorldState_SoDrainingNeverChangesTheHash()
    {
        var world = MapWorld.Plains(70, 40);
        ulong before = world.Sim.ComputeStateHash();

        world.Dirty.MarkTile(5, 5);
        world.DrainAll();

        world.Sim.ComputeStateHash().ShouldBe(before);
    }

    [Fact]
    public void RestoredSimulation_StartsWithEveryChunkDirty()
    {
        var world = MapWorld.Plains(70, 40);
        world.DrainAll();

        MapWorld restored = world.SaveAndRestore();

        restored.DrainAll().Count.ShouldBe(6);
    }
}
