using System.Collections.Immutable;
using System.Reflection;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>The write-service contract of M1-00: the context exposes one property per service, storage works, and the other services are stubs that name their task.</summary>
public sealed class ServiceContractTests
{
    private static readonly Assembly _simAssembly = typeof(SimContext).Assembly;

    // The write services a system reaches through SimContext, one fake each. IBindsContext and the listener are plumbing, the
    // building source belongs to the sequence type, and the two placement checks are halves of IPlacementQueries (FakePlacementQueries).
    private static readonly string[] _notWriteServices =
    [
        nameof(IBindsContext), nameof(IBuildingLifecycleListener), "IBuildingSource", nameof(IBuildingPlacement), nameof(IRoadPlacement),
    ];

    private static IEnumerable<Type> WriteServiceInterfaces() =>
        _simAssembly.GetTypes().Where(t => t.IsInterface
            && t.Namespace == "Evenmere.Sim.Services"
            && !_notWriteServices.Contains(t.Name));

    public static TheoryData<string> WriteServiceNames()
    {
        var data = new TheoryData<string>();
        foreach (Type type in WriteServiceInterfaces().OrderBy(static t => t.Name, StringComparer.Ordinal))
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Fact]
    public void WriteServiceInterfaces_Sim_AreInternal()
    {
        WriteServiceInterfaces().ShouldNotBeEmpty();
        WriteServiceInterfaces().ShouldAllBe(static t => !t.IsVisible, "write services are internal to the sim and not exposed to the game");
    }

    [Theory]
    [MemberData(nameof(WriteServiceNames))]
    public void Fakes_EveryWriteService_HasAFakeThatImplementsIt(string interfaceName)
    {
        Type service = WriteServiceInterfaces().Single(t => t.Name == interfaceName);

        Type[] fakes =
        [
            .. typeof(FakeSim).Assembly.GetTypes().Where(t =>
                t.Namespace == "Evenmere.Sim.Tests.Fakes" && t.Name.StartsWith("Fake", StringComparison.Ordinal) && service.IsAssignableFrom(t)),
        ];

        fakes.ShouldNotBeEmpty($"There is no fake for {interfaceName}");
    }

    [Theory]
    [MemberData(nameof(WriteServiceNames))]
    public void SimContext_EveryWriteService_IsExposedAsAnInternalProperty(string interfaceName)
    {
        Type service = WriteServiceInterfaces().Single(t => t.Name == interfaceName);

        PropertyInfo[] exposing =
        [
            .. typeof(SimContext).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.PropertyType == service),
        ];

        exposing.Length.ShouldBe(1, $"SimContext must expose {interfaceName} through exactly one property");
        exposing[0].GetMethod!.IsPublic.ShouldBeFalse("a write service is internal");
    }

    [Fact]
    public void SimContext_RealSimulation_HasEveryServiceWired()
    {
        SimContext ctx = new WorldBuilder().Build().Context;

        ctx.Storage.ShouldNotBeNull();
        ctx.BuildingRegistry.ShouldNotBeNull();
        ctx.BuildingLifecycle.ShouldNotBeNull();
        ctx.DirtyChunks.ShouldNotBeNull();
        ctx.ProgressionService.ShouldNotBeNull();
        ctx.PointsLedger.ShouldNotBeNull();
        ctx.Counters.ShouldNotBeNull();
        ctx.ProductionBuffers.ShouldNotBeNull();
        ctx.WorkforceSupply.ShouldNotBeNull();
    }

    [Fact]
    public void Storage_ThroughTheServiceInterface_ChangesWhatTheQueriesSee()
    {
        Simulation sim = new WorldBuilder().Build();
        GoodId timber = WorldBuilder.Content.IdOf<GoodId>("timber");

        sim.Context.Storage.Add(timber, Amount.FromUnits(12));
        sim.Context.Storage.TryTake(timber, Amount.FromUnits(5)).ShouldBeTrue();
        sim.Context.Storage.TryTake(timber, Amount.FromUnits(50)).ShouldBeFalse();

        sim.Context.Storage.Get(timber).ShouldBe(Amount.FromUnits(7));
        sim.Query.Storage.Stock(timber).ShouldBe(Amount.FromUnits(7));
        sim.Context.Storage.GoodCount.ShouldBe(WorldBuilder.Content.Goods.Count);
    }

    [Fact]
    public void StorageService_OverAPool_AddsNothingOfItsOwn()
    {
        var state = StorageState.ForGoodCount(2);
        var service = new StorageService(new StoragePool(state));

        service.Add(new GoodId(1), Amount.Parse("1.5"));

        state.Stock[1].ShouldBe(Amount.Parse("1.5"));
        Should.Throw<ArgumentOutOfRangeException>(() => service.Get(new GoodId(2)));
        Should.Throw<ArgumentOutOfRangeException>(() => service.Add(new GoodId(0), Amount.FromUnits(-1)));
    }

    [Fact]
    public void SimServices_BindTo_BindsEachServiceThatNeedsTheContextOnce()
    {
        // FakeSim creates a SimContext, whose constructor binds the lifecycle fake; raising a notification would throw if it had no context.
        var fake = FakeSim.Create();
        var seen = new List<SimContext>();
        fake.Services.BuildingLifecycle.Subscribe(new CapturingListener(seen));

        fake.Services.BuildingLifecycle.RaiseActivated(default);

        seen.ShouldHaveSingleItem().ShouldBeSameAs(fake.Context);
    }

    [Fact]
    public void Stubs_EveryUnimplementedService_ThrowsWithTheOwningTaskId()
    {
        var calls = new List<(string Name, string Task, Action Call)>
        {
            ("BuildingRegistry.Add", "M1-01", () => new UnimplementedBuildingRegistry().Add(default, 0, 0, Rotation.Deg0, BuildingState.Site)),
            ("BuildingRegistry.Remove", "M1-01", () => new UnimplementedBuildingRegistry().Remove(default)),
            ("BuildingRegistry.SetState", "M1-01", () => new UnimplementedBuildingRegistry().SetState(default, BuildingState.Active)),
            ("BuildingRegistry.SetType", "M1-01", () => new UnimplementedBuildingRegistry().SetType(default, default)),
            ("BuildingRegistry.SetStage", "M1-01", () => new UnimplementedBuildingRegistry().SetStage(default, 1)),
            ("BuildingLifecycle.Subscribe", "M1-01", () => new UnimplementedBuildingLifecycle().Subscribe(new CapturingListener([]))),
            ("DirtyChunks.MarkTile", "M1-01", () => new UnimplementedChunkDirtyTracker().MarkTile(0, 0)),
            ("DirtyChunks.MarkRect", "M1-01", () => new UnimplementedChunkDirtyTracker().MarkRect(0, 0, 1, 1)),
            ("ProductionBuffers.ListRequests", "M1-08", () => new UnimplementedProductionBuffers().ListRequests([])),
            ("ProductionBuffers.TryTakeOutput", "M1-08", () => new UnimplementedProductionBuffers().TryTakeOutput(default, default, Amount.Zero)),
            ("ProductionBuffers.AddInput", "M1-08", () => new UnimplementedProductionBuffers().AddInput(default, default, Amount.Zero)),
            ("WorkforceSupply.SetSupply", "M1-07", () => new UnimplementedWorkforceSupply().SetSupply(default, 1)),
            ("BuildingPlacement.Validate", "M1-06", () => new UnimplementedBuildingPlacement().Validate(default, 0, 0, Rotation.Deg0)),
            ("RoadPlacement.ValidateRoad", "M1-05", () => new UnimplementedRoadPlacement().ValidateRoad([], default)),
        };

        foreach ((string name, string task, Action call) in calls)
        {
            NotImplementedException thrown = Should.Throw<NotImplementedException>(call, name);
            thrown.Message.ShouldBe(task, name);
        }
    }

    [Fact]
    public void PlacementQueries_Facade_ForwardsToTheTwoChecks()
    {
        // The queries facade is real in M1-00: it only forwards, so the building rules (M1-06) and road rules (M1-05) land independently.
        var buildings = new RecordingBuildingPlacement();
        var roads = new RecordingRoadPlacement();
        var placement = new Evenmere.Sim.Queries.PlacementQueries(buildings, roads);

        placement.Validate(new BuildingTypeId(2), 3, 4, Rotation.Deg180).ShouldBeSameAs(RecordingBuildingPlacement.Answer);
        placement.ValidateRoad([new TileCoord(1, 1)], new BuildingTypeId(5)).ShouldBeSameAs(RecordingRoadPlacement.Answer);

        buildings.Seen.ShouldBe((new BuildingTypeId(2), 3, 4, Rotation.Deg180));
        roads.Seen.Path.ShouldBe([new TileCoord(1, 1)]);
        roads.Seen.RoadType.ShouldBe(new BuildingTypeId(5));
    }

    private sealed class CapturingListener(List<SimContext> seen) : IBuildingLifecycleListener
    {
        public void OnActivated(SimContext ctx, BuildingInfo building) => seen.Add(ctx);

        public void OnRemoved(SimContext ctx, BuildingInfo building) => seen.Add(ctx);

        public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType) => seen.Add(ctx);
    }

    private sealed class RecordingBuildingPlacement : IBuildingPlacement
    {
        public static PlacementResult Answer { get; } = new([], []);

        public (BuildingTypeId Type, int X, int Y, Rotation Rotation) Seen { get; private set; }

        public PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation)
        {
            Seen = (type, x, y, rotation);
            return Answer;
        }
    }

    private sealed class RecordingRoadPlacement : IRoadPlacement
    {
        public static PlacementResult Answer { get; } = new([], []);

        public (TileCoord[] Path, BuildingTypeId RoadType) Seen { get; private set; }

        public PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType)
        {
            Seen = (path.ToArray(), roadType);
            return Answer;
        }
    }
}
