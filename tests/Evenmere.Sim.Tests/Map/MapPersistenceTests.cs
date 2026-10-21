using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Tests.Map;

public sealed class MapPersistenceTests
{
    private static readonly BuildingTypeId _house = MapWorld.Type("settler_house");
    private static readonly BuildingTypeId _hall = MapWorld.Type("founders_hall");
    private static readonly BuildingTypeId _shed = MapWorld.Type("long_shed");

    private static MapWorld BuiltWorld()
    {
        var world = MapWorld.WithFixture("test_16");
        world.Registry.Add(_hall, 9, 9, Rotation.Deg0, BuildingState.Active);
        EntityId site = world.Registry.Add(_house, 3, 3, Rotation.Deg0, BuildingState.Site);
        world.Registry.SetStage(site, 6);
        EntityId paused = world.Registry.Add(_shed, 3, 7, Rotation.Deg90, BuildingState.Active);
        world.Registry.SetState(paused, BuildingState.Paused);
        EntityId gone = world.Registry.Add(_house, 5, 7, Rotation.Deg0, BuildingState.Active);
        world.Registry.Remove(gone);
        return world;
    }

    [Fact]
    public void NewGameWithoutAMap_RoundTripsWithAStableHash()
    {
        var world = MapWorld.Empty();

        MapWorld restored = world.SaveAndRestore();

        restored.Sim.ComputeStateHash().ShouldBe(world.Sim.ComputeStateHash());
        restored.Queries.Width.ShouldBe(0);
    }

    [Fact]
    public void SaveAndRestore_MapWithBuildings_KeepsTheHashAndEveryAnswer()
    {
        MapWorld world = BuiltWorld();

        MapWorld restored = world.SaveAndRestore();

        restored.Sim.ComputeStateHash().ShouldBe(world.Sim.ComputeStateHash());
        restored.Sim.SerializeState().ShouldBe(world.Sim.SerializeState());
        restored.AllBuildings().ShouldBe(world.AllBuildings());
        restored.ActiveBuildings().ShouldBe(world.ActiveBuildings());
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                restored.Queries.GetTile(x, y).ShouldBe(world.Queries.GetTile(x, y));
                restored.Queries.BuildingAt(x, y).ShouldBe(world.Queries.BuildingAt(x, y));
            }
        }
    }

    [Fact]
    public void Restored_KeepsHandingOutFreshEntityIds_AndKeepsTheRegistryWorking()
    {
        MapWorld world = BuiltWorld();
        int next = world.Sim.World.EntityIds.NextValue;

        MapWorld restored = world.SaveAndRestore();
        EntityId added = restored.Registry.Add(_house, 12, 3, Rotation.Deg0, BuildingState.Active);
        restored.Registry.Remove(new EntityId(1)).ShouldBeTrue();

        added.Value.ShouldBe(next);
        restored.AllBuildings().Select(b => b.Id.Value).ShouldBe([2, 3, next]);
        restored.Queries.BuildingAt(9, 9).ShouldBe(EntityId.None);
    }

    [Fact]
    public void SameOperationsOnTwoWorlds_GiveTheSameBytesAndHash()
    {
        MapWorld first = BuiltWorld();
        MapWorld second = BuiltWorld();

        second.Sim.SerializeState().ShouldBe(first.Sim.SerializeState());
        second.Sim.ComputeStateHash().ShouldBe(first.Sim.ComputeStateHash());
    }

    [Fact]
    public void AnyBuildingChange_ChangesTheHash()
    {
        MapWorld world = BuiltWorld();
        ulong before = world.Sim.ComputeStateHash();

        world.Registry.SetStage(new EntityId(2), 7);
        ulong staged = world.Sim.ComputeStateHash();
        world.Registry.SetState(new EntityId(3), BuildingState.Active);
        ulong resumed = world.Sim.ComputeStateHash();

        new[] { before, staged, resumed }.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void RestoredWorld_TicksToTheSameHashAsTheOriginal()
    {
        MapWorld world = BuiltWorld();
        MapWorld restored = world.SaveAndRestore();

        for (int i = 0; i < 300; i++)
        {
            world.Sim.Tick();
            restored.Sim.Tick();
        }

        restored.Sim.ComputeStateHash().ShouldBe(world.Sim.ComputeStateHash());
    }

    // ---- a corrupt map state fails when the simulation is built, not far into a run

    private static MapState SoundState(out EntityIdAllocator ids)
    {
        MapWorld restored = BuiltWorld().SaveAndRestore();
        ids = restored.Sim.World.EntityIds;
        return restored.Sim.World.Map;
    }

    [Fact]
    public void MapSystem_SoundState_IsAccepted()
    {
        MapState state = SoundState(out EntityIdAllocator ids);

        Should.NotThrow(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_LayerOfTheWrongLength_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Elevation = new byte[10];

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_NullMember_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Occupancy = null!;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_OneSideZero_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Width = 0;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_TerrainBeyondTheContent_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Terrain[5] = (byte)MapWorld.Content.Terrains.Count;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_ValueOutOfRange_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Elevation[0] = 16;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_OccupancyThatDisagreesWithTheRegistry_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Occupancy[(9 * 16) + 9] = 0;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_OccupancyNoBuildingClaims_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        state.Occupancy[(14 * 16) + 14] = 2;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_BuildingOutsideTheMap_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        ref BuildingRecord hall = ref state.Buildings.GetRef(new EntityId(1));
        hall.Origin = new TileCoord(14, 14);

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_BuildingIdNotBelowTheAllocator_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);

        Should.Throw<InvalidDataException>(() => new MapSystem(state, new EntityIdAllocator(3), MapWorld.Content));
    }

    [Fact]
    public void MapSystem_BuildingTypeBeyondTheContent_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        ref BuildingRecord hall = ref state.Buildings.GetRef(new EntityId(1));
        hall.Type = new BuildingTypeId(999);

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }

    [Fact]
    public void MapSystem_StageOnAFinishedBuilding_IsRejected()
    {
        MapState state = SoundState(out EntityIdAllocator ids);
        ref BuildingRecord hall = ref state.Buildings.GetRef(new EntityId(1));
        hall.Stage = 3;

        Should.Throw<InvalidDataException>(() => new MapSystem(state, ids, MapWorld.Content));
    }
}
