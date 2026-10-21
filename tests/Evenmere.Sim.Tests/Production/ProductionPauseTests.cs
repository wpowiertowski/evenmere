using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Production.Commands;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionPauseTests
{
    private static BuildingState StateOf(ProductionHarness h, EntityId building)
    {
        h.Map.TryGetBuilding(building, out BuildingInfo info).ShouldBeTrue();
        return info.State;
    }

    [Fact]
    public void SetBuildingPaused_AProducer_PausesItThroughTheRegistry()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(50);

        h.Send(new SetBuildingPaused(farm, true));

        StateOf(h, farm).ShouldBe(BuildingState.Paused);
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void SetBuildingPaused_APausedProducer_StallsKeepsItsProgressAndResumes()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(50);
        h.Send(new SetBuildingPaused(farm, true));

        h.RunTicks(100);

        ProductionInfo paused = h.Info(farm);
        paused.Stall.ShouldBe(StallReason.Paused);
        paused.Productivity.PerMille.ShouldBe(0);
        paused.CycleProgress.PerMille.ShouldBe(50 * 1000 / 120);
        h.Events().ShouldBe([new ProductionStalled(51, farm, StallReason.Paused)]);

        h.Send(new SetBuildingPaused(farm, false));
        StateOf(h, farm).ShouldBe(BuildingState.Active);

        // 70 ticks are left of the 120.
        h.RunTicks(69);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));
        h.Events().ShouldBe([new ProductionResumed(151, farm)]);
    }

    [Fact]
    public void SetBuildingPaused_APausedBuilding_IsPausedInTheRegistryAndNowhereElse()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.Send(new SetBuildingPaused(farm, true));

        // The production state has no flag of its own: the registry is the one source of truth (ADR-0011), so a pause set
        // directly in the registry stalls the building in just the same way.
        h.Sim.Services.BuildingRegistry.SetState(farm, BuildingState.Active);
        h.RunTicks(1);
        h.Info(farm).Stall.ShouldBe(StallReason.None);

        h.Sim.Services.BuildingRegistry.SetState(farm, BuildingState.Paused);
        h.RunTicks(1);
        h.Info(farm).Stall.ShouldBe(StallReason.Paused);
    }

    [Fact]
    public void SetBuildingPaused_PausingTwice_IsAcceptedAndChangesNothing()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.Send(new SetBuildingPaused(farm, true));
        h.Send(new SetBuildingPaused(farm, true));

        StateOf(h, farm).ShouldBe(BuildingState.Paused);
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void SetBuildingPaused_AnUnknownBuilding_IsRefusedAsAnUnknownTarget()
    {
        var h = ProductionHarness.Create();
        var command = new SetBuildingPaused(new EntityId(999), true);

        h.Send(command);

        h.Events().ShouldBe([new CommandRejected(0, command, RejectReason.UnknownTarget, "EntityId(999) is not a building.")]);
    }

    [Theory]
    [InlineData("settler_house")]
    [InlineData("storehouse")]
    public void SetBuildingPaused_ABuildingThatIsNoProducer_IsRefusedAsNotApplicable(string type)
    {
        var h = ProductionHarness.Create();
        EntityId building = h.Add(type, 2, 2);

        h.Send(new SetBuildingPaused(building, true));

        h.Events().Single().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NotApplicable);
        StateOf(h, building).ShouldBe(BuildingState.Active);
    }

    [Fact]
    public void SetBuildingPaused_AProducerStillUnderConstruction_IsRefusedAsNotApplicable()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.Add("sheep_farm", 2, 2, BuildingState.Site);

        h.Send(new SetBuildingPaused(farm, true));

        h.Events().Single().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NotApplicable);
        StateOf(h, farm).ShouldBe(BuildingState.Site);
    }
}
