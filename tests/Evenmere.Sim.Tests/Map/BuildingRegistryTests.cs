using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Map;

public sealed class BuildingRegistryTests
{
    private static readonly BuildingTypeId _house = MapWorld.Type("settler_house");
    private static readonly BuildingTypeId _hall = MapWorld.Type("founders_hall");
    private static readonly BuildingTypeId _shed = MapWorld.Type("long_shed");
    private static readonly BuildingTypeId _barn = MapWorld.Type("long_barn");

    // ---- add

    [Fact]
    public void Add_Site_RegistersTheBuildingAndItsOccupancy()
    {
        var world = MapWorld.WithFixture("test_16");

        EntityId id = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        id.ShouldBe(new EntityId(1));
        world.Queries.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        info.ShouldBe(new BuildingInfo(id, _house, new TileCoord(8, 9), Rotation.Deg0, 2, 2, BuildingState.Site, 0));
        foreach ((int x, int y) in new[] { (8, 9), (9, 9), (8, 10), (9, 10) })
        {
            world.Queries.BuildingAt(x, y).ShouldBe(id);
        }

        world.Queries.BuildingAt(10, 9).ShouldBe(EntityId.None);
        world.Queries.BuildingAt(8, 11).ShouldBe(EntityId.None);
        world.Queries.BuildingAt(7, 9).ShouldBe(EntityId.None);
    }

    [Fact]
    public void Add_EntityIds_AreAllocatedFromTheWorldAndNeverReused()
    {
        var world = MapWorld.WithFixture("test_16");

        EntityId first = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);
        world.Registry.Remove(first).ShouldBeTrue();
        EntityId second = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        second.Value.ShouldBe(first.Value + 1);
        world.Sim.World.EntityIds.NextValue.ShouldBe(second.Value + 1);
    }

    [Theory]
    [InlineData(Rotation.Deg0, 3, 1)]
    [InlineData(Rotation.Deg90, 1, 3)]
    [InlineData(Rotation.Deg180, 3, 1)]
    [InlineData(Rotation.Deg270, 1, 3)]
    public void Add_Rotation_SwapsWidthAndHeightOnAQuarterTurn(Rotation rotation, int width, int height)
    {
        var world = MapWorld.WithFixture("test_16");

        EntityId id = world.Registry.Add(_shed, 8, 9, rotation, BuildingState.Active);

        world.Queries.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        info.Width.ShouldBe(width);
        info.Height.ShouldBe(height);
        info.Rotation.ShouldBe(rotation);
        world.Queries.BuildingAt(8 + width - 1, 9 + height - 1).ShouldBe(id);
        world.Queries.BuildingAt(8 + width, 9).ShouldBe(EntityId.None);
        world.Queries.BuildingAt(8, 9 + height).ShouldBe(EntityId.None);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    [InlineData(15, 5)]
    [InlineData(5, 15)]
    [InlineData(int.MaxValue, 5)]
    public void Add_FootprintLeavesTheMap_ThrowsAndChangesNothing(int x, int y)
    {
        var world = MapWorld.WithFixture("test_16");
        world.DrainAll();

        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, x, y, Rotation.Deg0, BuildingState.Site));

        world.AllBuildings().ShouldBeEmpty();
        world.Sim.World.EntityIds.NextValue.ShouldBe(1);
        world.DrainAll().ShouldBeEmpty();
    }

    [Fact]
    public void Add_OverlappingAnotherBuilding_ThrowsAndLeavesTheFirstIntact()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId first = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, 9, 10, Rotation.Deg0, BuildingState.Site));

        world.AllBuildings().Select(b => b.Id).ShouldBe([first]);
        world.Sim.World.EntityIds.NextValue.ShouldBe(2);
        world.Queries.BuildingAt(10, 11).ShouldBe(EntityId.None);
    }

    [Fact]
    public void Add_OnAnEmptyMap_Throws()
    {
        var world = MapWorld.Empty();

        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, 0, 0, Rotation.Deg0, BuildingState.Site));
    }

    [Fact]
    public void Add_BadArguments_Throw()
    {
        var world = MapWorld.WithFixture("test_16");

        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, 8, 9, (Rotation)4, BuildingState.Site));
        Should.Throw<ArgumentException>(() => world.Registry.Add(new BuildingTypeId(999), 8, 9, Rotation.Deg0, BuildingState.Site));
        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Paused));
        Should.Throw<ArgumentException>(() => world.Registry.Add(_house, 8, 9, Rotation.Deg0, (BuildingState)9));
    }

    [Fact]
    public void Add_Ruin_IsRegisteredWithoutNotifying()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("a", log));

        EntityId id = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Ruin);

        log.ShouldBeEmpty();
        world.Queries.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        info.State.ShouldBe(BuildingState.Ruin);
        world.Queries.BuildingAt(9, 10).ShouldBe(id);
    }

    // ---- remove

    [Fact]
    public void Remove_Building_FreesEveryTileAndReportsIt()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId id = world.Registry.Add(_hall, 9, 9, Rotation.Deg0, BuildingState.Active);

        world.Registry.Remove(id).ShouldBeTrue();

        world.Queries.TryGetBuilding(id, out _).ShouldBeFalse();
        world.AllBuildings().ShouldBeEmpty();
        for (int y = 9; y < 13; y++)
        {
            for (int x = 9; x < 13; x++)
            {
                world.Queries.BuildingAt(x, y).ShouldBe(EntityId.None);
            }
        }

        // The freed ground can be built on again.
        world.Registry.Add(_house, 10, 10, Rotation.Deg0, BuildingState.Site).ShouldBe(new EntityId(2));
    }

    [Fact]
    public void Remove_UnknownBuilding_ReturnsFalse()
    {
        var world = MapWorld.WithFixture("test_16");

        world.Registry.Remove(new EntityId(42)).ShouldBeFalse();
        world.Registry.Remove(EntityId.None).ShouldBeFalse();
    }

    // ---- states and the queries that follow them

    [Fact]
    public void SetState_SiteToActive_UpdatesBothSequences()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId site = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);
        world.ActiveBuildings().ShouldBeEmpty();

        world.Registry.SetState(site, BuildingState.Active);

        world.ActiveBuildings().Select(b => b.Id).ShouldBe([site]);
        world.Queries.TryGetBuilding(site, out BuildingInfo info).ShouldBeTrue();
        info.State.ShouldBe(BuildingState.Active);
        info.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void ActiveBuildings_PausedIsFinishedAndSitesAndRuinsAreNot()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId site = world.Registry.Add(_house, 3, 3, Rotation.Deg0, BuildingState.Site);
        EntityId active = world.Registry.Add(_house, 6, 3, Rotation.Deg0, BuildingState.Active);
        EntityId paused = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);
        EntityId ruin = world.Registry.Add(_house, 10, 9, Rotation.Deg0, BuildingState.Ruin);
        world.Registry.SetState(paused, BuildingState.Paused);

        world.AllBuildings().Select(b => b.Id).ShouldBe([site, active, paused, ruin]);
        world.ActiveBuildings().Select(b => b.Id).ShouldBe([active, paused]);
        world.ActiveBuildings().Select(b => b.State).ShouldBe([BuildingState.Active, BuildingState.Paused]);
    }

    [Fact]
    public void SetState_SiteToPaused_Throws()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId site = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        Should.Throw<InvalidOperationException>(() => world.Registry.SetState(site, BuildingState.Paused));

        world.Queries.TryGetBuilding(site, out BuildingInfo info).ShouldBeTrue();
        info.State.ShouldBe(BuildingState.Site);
    }

    [Fact]
    public void SetState_UnknownBuildingOrState_Throws()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId site = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        Should.Throw<KeyNotFoundException>(() => world.Registry.SetState(new EntityId(77), BuildingState.Active));
        Should.Throw<ArgumentException>(() => world.Registry.SetState(site, (BuildingState)9));
    }

    [Fact]
    public void SetStage_OnASite_ShowsInTheQueryAndResetsWhenTheSiteActivates()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId site = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        world.Registry.SetStage(site, 4);
        world.Queries.TryGetBuilding(site, out BuildingInfo building).ShouldBeTrue();
        building.Stage.ShouldBe(4);

        world.Registry.SetStage(site, 10);
        world.Registry.SetState(site, BuildingState.Active);
        world.Queries.TryGetBuilding(site, out building).ShouldBeTrue();
        building.Stage.ShouldBe(0);
    }

    [Fact]
    public void SetStage_OnAFinishedBuilding_ChangesNothing_AndNegativeThrows()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId done = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);

        world.Registry.SetStage(done, 5);

        world.Queries.TryGetBuilding(done, out BuildingInfo building).ShouldBeTrue();
        building.Stage.ShouldBe(0);
        Should.Throw<ArgumentOutOfRangeException>(() => world.Registry.SetStage(done, -1));
        Should.Throw<KeyNotFoundException>(() => world.Registry.SetStage(new EntityId(50), 1));
    }

    [Fact]
    public void SetType_SameFootprint_ChangesTheTypeInPlace()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId id = world.Registry.Add(_shed, 8, 9, Rotation.Deg90, BuildingState.Active);

        world.Registry.SetType(id, _barn);

        world.Queries.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        info.Type.ShouldBe(_barn);
        info.Width.ShouldBe(1);
        info.Height.ShouldBe(3);
        world.Queries.BuildingAt(8, 11).ShouldBe(id);
    }

    [Fact]
    public void SetType_DifferentFootprintOrUnknownType_ThrowsAndKeepsTheType()
    {
        var world = MapWorld.WithFixture("test_16");
        EntityId id = world.Registry.Add(_shed, 8, 9, Rotation.Deg0, BuildingState.Active);

        Should.Throw<ArgumentException>(() => world.Registry.SetType(id, _house));
        Should.Throw<ArgumentException>(() => world.Registry.SetType(id, new BuildingTypeId(999)));
        Should.Throw<KeyNotFoundException>(() => world.Registry.SetType(new EntityId(60), _barn));

        world.Queries.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        info.Type.ShouldBe(_shed);
    }

    // ---- lifecycle

    [Fact]
    public void Lifecycle_AddActive_RaisesActivatedButAddSiteDoesNot()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("a", log));

        world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);
        log.ShouldBeEmpty();

        world.Registry.Add(_house, 10, 9, Rotation.Deg0, BuildingState.Active);
        log.ShouldBe(["a:activated:2"]);
    }

    [Fact]
    public void Lifecycle_SiteBecomesActive_ActivatesOnceAndPauseResumeDoesNot()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("a", log));
        EntityId id = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Site);

        world.Registry.SetStage(id, 3);
        world.Registry.SetState(id, BuildingState.Active);
        world.Registry.SetState(id, BuildingState.Paused);
        world.Registry.SetState(id, BuildingState.Active);
        world.Registry.SetState(id, BuildingState.Active);

        log.ShouldBe(["a:activated:1"]);
    }

    [Fact]
    public void Lifecycle_RemoveInAnyState_RaisesRemovedWithTheBuildingAsItStood()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        BuildingInfo? seen = null;
        world.Lifecycle.Subscribe(new RecordingListener("a", log) { OnEvent = (_, b) => seen = b });
        EntityId site = world.Registry.Add(_house, 3, 3, Rotation.Deg0, BuildingState.Site);
        EntityId active = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);
        EntityId ruin = world.Registry.Add(_house, 10, 9, Rotation.Deg0, BuildingState.Ruin);
        log.Clear();

        world.Registry.Remove(site);
        seen.GetValueOrDefault().State.ShouldBe(BuildingState.Site);
        world.Registry.Remove(active);
        seen.GetValueOrDefault().State.ShouldBe(BuildingState.Active);
        world.Registry.Remove(ruin);
        seen.GetValueOrDefault().State.ShouldBe(BuildingState.Ruin);

        log.ShouldBe([$"a:removed:{site.Value}", $"a:removed:{active.Value}", $"a:removed:{ruin.Value}"]);
        world.Registry.Remove(site).ShouldBeFalse();
        log.Count.ShouldBe(3);
    }

    [Fact]
    public void Lifecycle_TypeChange_RaisesTypeChangedWithThePreviousType()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("a", log));
        EntityId id = world.Registry.Add(_shed, 8, 9, Rotation.Deg0, BuildingState.Active);
        log.Clear();

        world.Registry.SetType(id, _barn);
        world.Registry.SetType(id, _barn);

        log.ShouldBe([$"a:typechanged:{id.Value}:{_shed.Value}>{_barn.Value}"]);
    }

    [Fact]
    public void Lifecycle_Listeners_AreCalledInSubscriptionOrder()
    {
        var world = MapWorld.WithFixture("test_16");
        var log = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("first", log));
        world.Lifecycle.Subscribe(new RecordingListener("second", log));
        world.Lifecycle.Subscribe(new RecordingListener("third", log));

        world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);

        log.ShouldBe(["first:activated:1", "second:activated:1", "third:activated:1"]);
    }

    [Fact]
    public void Lifecycle_Listeners_ReceiveTheSimulationContextAndSeeTheFinishedChange()
    {
        var world = MapWorld.WithFixture("test_16");
        var seenInQueries = new List<string>();
        world.Lifecycle.Subscribe(new RecordingListener("a", []) { OnEvent = (ctx, b) => RecordQueries(ctx, b, seenInQueries) });
        world.Lifecycle.Subscribe(new RecordingListener("b", []) { OnEvent = (ctx, b) => ctx.ShouldBeSameAs(world.Sim.Context) });

        EntityId id = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);
        world.Registry.Remove(id);

        // On activation the registry already answers for the building; on removal it already no longer does.
        seenInQueries.ShouldBe(["found:True:Active:" + id.Value, "found:False:" + id.Value]);
    }

    private static void RecordQueries(SimContext ctx, BuildingInfo building, List<string> into)
    {
        bool found = ctx.Queries.Map.TryGetBuilding(building.Id, out BuildingInfo now);
        bool covered = ctx.Queries.Map.BuildingAt(building.Origin.X, building.Origin.Y) == building.Id;
        into.Add(found ? $"found:{covered}:{now.State}:{building.Id.Value}" : $"found:{covered}:{building.Id.Value}");
    }

    [Fact]
    public void Lifecycle_ListenerChangingBuildings_IsRefusedAndTheRegistryRecovers()
    {
        var world = MapWorld.WithFixture("test_16");
        InvalidOperationException? refused = null;
        world.Lifecycle.Subscribe(new RecordingListener("a", []) { OnEvent = (ctx, b) => refused = Record(() => ctx.BuildingRegistry.Remove(b.Id)) });

        EntityId id = world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active);

        refused.ShouldNotBeNull();
        world.Queries.TryGetBuilding(id, out _).ShouldBeTrue();

        // Once the notification is over the registry works again.
        world.Registry.Add(_house, 10, 9, Rotation.Deg0, BuildingState.Site);
        world.AllBuildings().Count.ShouldBe(2);
    }

    private static InvalidOperationException? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Lifecycle_ListenerThatThrows_LeavesTheRegistryUsable()
    {
        var world = MapWorld.WithFixture("test_16");
        world.Lifecycle.Subscribe(new RecordingListener("a", []) { OnEvent = (_, _) => throw new FormatException("boom") });

        Should.Throw<FormatException>(() => world.Registry.Add(_house, 8, 9, Rotation.Deg0, BuildingState.Active));

        world.Registry.Add(_house, 10, 9, Rotation.Deg0, BuildingState.Site);
        world.AllBuildings().Count.ShouldBe(2);
    }

    [Fact]
    public void Lifecycle_SubscribeTwiceOrNull_Throws()
    {
        var world = MapWorld.WithFixture("test_16");
        var listener = new RecordingListener("a", []);
        world.Lifecycle.Subscribe(listener);

        Should.Throw<ArgumentException>(() => world.Lifecycle.Subscribe(listener));
        Should.Throw<ArgumentNullException>(() => world.Lifecycle.Subscribe(null!));
    }

    // ---- sequences

    [Fact]
    public void Buildings_RemovingWhileIterating_StillVisitsTheRestInOrder()
    {
        var world = MapWorld.WithFixture("test_16");
        var ids = new List<EntityId>();
        for (int i = 0; i < 6; i++)
        {
            ids.Add(world.Registry.Add(_house, 2 + (i * 2), 13, Rotation.Deg0, BuildingState.Active));
        }

        var visited = new List<EntityId>();
        foreach (BuildingInfo building in world.Queries.Buildings)
        {
            visited.Add(building.Id);

            // Remove the building just visited and the one after it.
            world.Registry.Remove(building.Id);
            if (building.Id == ids[1])
            {
                world.Registry.Remove(ids[2]);
            }
        }

        visited.ShouldBe([ids[0], ids[1], ids[3], ids[4], ids[5]]);
        world.AllBuildings().ShouldBeEmpty();
    }

    [Fact]
    public void Buildings_DefaultSequence_IsEmpty()
    {
        int count = 0;
        foreach (BuildingInfo _ in default(BuildingSequence))
        {
            count++;
        }

        count.ShouldBe(0);
    }
}
