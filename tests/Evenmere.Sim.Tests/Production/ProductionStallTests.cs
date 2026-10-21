using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Production;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionStallTests
{
    [Fact]
    public void Stall_MissingInput_StallsWithThatReasonAndMakesNoProgress()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);

        h.RunTicks(200);

        ProductionInfo info = h.Info(mill);
        info.Stall.ShouldBe(StallReason.MissingInput);
        info.InputsReady.ShouldBeFalse();
        info.Productivity.PerMille.ShouldBe(0);
        info.CycleProgress.PerMille.ShouldBe(0);
        h.Output(mill, "flour").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Stall_ReasonChanges_EmitOneEventPerChange()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);

        h.RunTicks(10);
        h.Events().ShouldBe([new ProductionStalled(1, mill, StallReason.MissingInput)]);

        // Nothing changed, so nothing is said.
        h.RunTicks(10);
        h.Events().ShouldBeEmpty();

        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(1));
        h.RunTicks(1);
        h.Events().ShouldBe([new ProductionResumed(21, mill)]);

        // The one grain is used up when the cycle completes, 120 ticks after the first working tick.
        h.RunTicks(120);
        h.Events().ShouldBe([new ProductionStalled(141, mill, StallReason.MissingInput)]);
    }

    [Fact]
    public void Stall_ProducerThatNeverStalls_EmitsNothing()
    {
        var h = ProductionHarness.Create();
        h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(100);

        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void Stall_NoWorkforce_StallsAndProducesNothing()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.SetFill(farm, 0);

        h.RunTicks(300);

        h.Info(farm).Stall.ShouldBe(StallReason.NoWorkforce);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Stall_OutsideStorehouseReach_StallsEvenWhenStaffed()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.Add("sheep_farm", 2, 2);

        h.RunTicks(300);

        h.Info(farm).Stall.ShouldBe(StallReason.NoStorehouseReach);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Stall_OutputBufferFull_StallsUntilACartTakesGoods()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        // Four units is the output buffer, reached on tick 480; the fifth cycle cannot finish into it.
        h.RunTicks(600);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(4));
        h.Info(farm).Stall.ShouldBe(StallReason.OutputFull);

        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("wool"), Amount.FromUnits(2)).ShouldBeTrue();
        h.RunTicks(1);
        h.Info(farm).Stall.ShouldBe(StallReason.None);

        h.RunTicks(120);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(3));
    }

    [Fact]
    public void Stall_SeveralReasonsAtOnce_ReportsTheHighestPriorityOne()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.Add("windmill", 2, 2);

        // Everything is wrong at once: paused, out of reach, no workers, no grain, and a full output buffer.
        h.SetFill(mill, 0);
        h.State.Producers.TryGet(mill, out ProducerRecord? record).ShouldBeTrue();
        record.ShouldNotBeNull();
        record.Outputs[0] = Amount.FromUnits(4);
        h.Sim.Services.BuildingRegistry.SetState(mill, BuildingState.Paused);

        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.Paused);

        h.Sim.Services.BuildingRegistry.SetState(mill, BuildingState.Active);
        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.NoStorehouseReach);

        h.GrantReach(mill);
        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.NoWorkforce);

        h.SetFill(mill, 1000);
        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.MissingInput);

        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(1));
        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.OutputFull);

        record.Outputs[0] = Amount.Zero;
        h.RunTicks(1);
        h.Info(mill).Stall.ShouldBe(StallReason.None);
    }

    [Fact]
    public void Stall_AStalledBuilding_KeepsItsCycleProgress()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(60);
        h.SetFill(farm, 0);
        h.RunTicks(100);
        h.Info(farm).CycleProgress.PerMille.ShouldBe(500);

        h.SetFill(farm, 1000);
        h.RunTicks(59);
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
        h.RunTicks(1);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(1));
    }
}
