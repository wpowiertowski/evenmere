using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Construction.Monuments;

/// <summary>
/// Multi-stage monument construction (M2-10): the test monument hall builds in two stages (3 timber, then 2 wood)
/// over 2 build days. A stage's bill draws in ten steps, the site moves on only when the stage is drawn and worked,
/// the effects apply exactly once on the last stage, and demolition refunds what was drawn and removes the effects.
/// </summary>
public sealed class MonumentTests
{
    private static MonumentId Monument() => ConstructionWorld.Content.IdOf<MonumentId>("test_monument");

    private static EntityId PlaceMonument(ConstructionWorld world)
    {
        EntityId id = world.Place("test_monument_hall", 5, 5);
        world.GrantReach(id);
        return id;
    }

    private static void Demolish(ConstructionWorld world, EntityId building)
    {
        world.Enqueue(new Demolish(building));
        world.Tick();
    }

    [Fact]
    public void MonumentSite_BuildsOneStageAtATime_StallingWhenTheNextBillIsShort()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = PlaceMonument(world);

        // Stage 1 takes 120 ticks (half of the 2 build days). After 119 of them the timber bill is fully drawn.
        world.Tick(119);
        SiteInfo first = world.Site(id);
        (first.MonumentStage, first.MonumentStageCount).ShouldBe((0, 2));
        (first.Stage, first.Steps, first.Stall).ShouldBe((9, 10, StallReason.None));
        world.Stocked("timber").ShouldBe(Amount.FromUnits(7));

        world.Tick();
        SiteInfo second = world.Site(id);
        world.Sim.DrainEvents().OfType<MonumentStageCompleted>().ShouldHaveSingleItem()
            .ShouldBe(new MonumentStageCompleted(121, id, Monument(), 0));
        (second.MonumentStage, second.MonumentStageCount).ShouldBe((1, 2));
        (second.Stage, second.Stall, second.WaitingFor).ShouldBe((0, StallReason.MissingMaterial, ConstructionWorld.Good("wood")));
        second.Progress.PerMille.ShouldBe(500);

        // No monument effects until the last stage completes.
        world.Sim.Services.ProgressionService.AppliedSources.ShouldBeEmpty();

        // The wood bill draws once stocked, and the last stage completes 120 ticks after it started.
        world.Stock("wood", "10");
        world.Tick(120);
        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("wood").ShouldBe(Amount.FromUnits(8));
        IReadOnlyList<SimEvent> done = world.Sim.DrainEvents();
        done.OfType<MonumentStageCompleted>().ShouldHaveSingleItem()
            .ShouldBe(new MonumentStageCompleted(241, id, Monument(), 1));
        done.OfType<ConstructionCompleted>().ShouldHaveSingleItem()
            .ShouldBe(new ConstructionCompleted(241, id, ConstructionWorld.Type("test_monument_hall")));
    }

    [Fact]
    public void MonumentSite_ReportsZeroStages_ForAnOrdinaryBuilding()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);
        world.Tick(10);

        SiteInfo site = world.Site(id);
        (site.MonumentStage, site.MonumentStageCount).ShouldBe((0, 0));
    }

    [Fact]
    public void MonumentSite_AppliesItsEffectsExactlyOnce_OnTheLastStage()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "10");
        EntityId id = PlaceMonument(world);
        world.Tick(241);

        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Sim.Services.ProgressionService.AppliedSources.ShouldHaveSingleItem()
            .ShouldBe(EffectSource.ForMonument(Monument()));

        world.Tick(50);
        world.Sim.Services.ProgressionService.AppliedSources.ShouldHaveSingleItem();
    }

    [Fact]
    public void MonumentSite_MaterialCostBonus_ScalesEveryStageBill()
    {
        var world = ConstructionWorld.Create();
        world.Sim.Queries.Progression.AddModifier(
            Stat.MaterialCost, new ModifierContribution(EffectSource.ForTech(new TechId(0)), -20, true));
        world.Stock("timber", "10");
        world.Stock("wood", "10");
        EntityId id = PlaceMonument(world);
        world.Tick(241);

        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("timber").ShouldBe(Amount.Parse("7.6"));
        world.Stocked("wood").ShouldBe(Amount.Parse("8.4"));
    }

    [Fact]
    public void Demolish_FinishedMonument_RemovesItsEffectsAndRefundsHalfTheFinalStage()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "10");
        EntityId id = PlaceMonument(world);
        world.Tick(241);
        world.Sim.DrainEvents();

        Demolish(world, id);

        // The final stage was 2 wood, so half (1 wood) comes back; the timber stages do not.
        world.Stocked("timber").ShouldBe(Amount.FromUnits(7));
        world.Stocked("wood").ShouldBe(Amount.FromUnits(9));
        world.Sim.Queries.Map.TryGetBuilding(id, out _).ShouldBeFalse();
        world.Sim.Services.ProgressionService.RemovedSources.ShouldHaveSingleItem()
            .ShouldBe(EffectSource.ForMonument(Monument()));
    }

    [Fact]
    public void Demolish_MonumentSiteMidBuild_RefundsTheFinishedStageAndWhatTheCurrentStageDrew()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "10");
        EntityId id = PlaceMonument(world);
        world.Tick(121);
        world.Tick(30);
        world.Stocked("wood").ShouldBe(Amount.Parse("9.4"));

        Demolish(world, id);

        world.Stocked("timber").ShouldBe(Amount.FromUnits(10));
        world.Stocked("wood").ShouldBe(Amount.FromUnits(10));
        world.State.Sites.Count.ShouldBe(0);
        world.Sim.Services.ProgressionService.AppliedSources.ShouldBeEmpty();
        world.Sim.Services.ProgressionService.RemovedSources.ShouldBeEmpty();
    }

    [Fact]
    public void Place_SecondMonumentOfTheSameType_IsRejectedAsAlreadyBuilt()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "10");
        PlaceMonument(world);

        world.Enqueue(new PlaceBuilding(ConstructionWorld.Type("test_monument_hall"), 10, 10, Rotation.Deg0));
        world.Tick();

        CommandRejected? rejection = world.Rejection();
        rejection.ShouldNotBeNull().Code.ShouldBe(RejectReason.PlacementInvalid);
        rejection.Detail.ShouldContain("UniqueAlreadyBuilt");
        world.Sim.Queries.Map.BuildingAt(10, 10).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void MonumentSite_SaveRoundTripMidSecondStage_ResumesInLockstep()
    {
        var original = ConstructionWorld.Create();
        original.Stock("timber", "10");
        original.Stock("wood", "10");
        EntityId id = PlaceMonument(original);
        original.Tick(150);
        original.Site(id).MonumentStage.ShouldBe(1);
        original.Sim.DrainEvents();

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, ConstructionWorld.Content);
        StateSerializer.ComputeHash(restored).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        restored.Construction.Sites.TryGet(id, out SiteRecord record).ShouldBeTrue();
        record.StageIndex.ShouldBe(1);

        var resumed = ConstructionWorld.Create(restored.Construction);
        resumed.Sim.SetTick(original.Now);
        resumed.Build("test_monument_hall", 5, 5, BuildingState.Site);
        resumed.Sim.Services.BuildingRegistry.SetStage(id, original.Info(id).Stage);
        resumed.GrantReach(id);
        resumed.Sim.Services.Storage.Set(ConstructionWorld.Good("timber"), original.Stocked("timber"));
        resumed.Sim.Services.Storage.Set(ConstructionWorld.Good("wood"), original.Stocked("wood"));
        original.Sim.DrainEvents();

        for (int tick = 0; tick < 80; tick++)
        {
            original.Tick();
            resumed.Tick();
            original.System.Queries.TryGetSite(id, out SiteInfo expected).ShouldBe(resumed.System.Queries.TryGetSite(id, out SiteInfo actual));
            actual.ShouldBe(expected, $"{id} at tick {original.Now}");
        }

        StateSerializer.ComputeHash(resumed.Sim.World).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        original.Info(id).State.ShouldBe(BuildingState.Site);
    }
}
