using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Production;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionBufferTests
{
    private static List<TransportRequest> Requests(ProductionHarness h)
    {
        var requests = new List<TransportRequest>();
        h.Context.ProductionBuffers.ListRequests(requests);
        return requests;
    }

    [Fact]
    public void Requests_OutputBelowACartLoad_OpensNoPickup()
    {
        var h = ProductionHarness.Create();
        h.AddReady("sheep_farm", 2, 2);

        // One unit is made on tick 120, and a cart load is two.
        h.RunTicks(120);

        Requests(h).ShouldBeEmpty();
    }

    [Fact]
    public void Requests_OutputAtACartLoad_OpensAPickupForTheFirstTickItHeldOne()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(240);

        Requests(h).ShouldBe([new TransportRequest(farm, TransportDirection.Pickup, h.Good("wool"), Amount.FromUnits(2), 240)]);
    }

    [Fact]
    public void Requests_APickupStaysListedWithItsOriginalTickUntilGoodsMove()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(240);
        h.RunTicks(50);

        Requests(h).ShouldBe([new TransportRequest(farm, TransportDirection.Pickup, h.Good("wool"), Amount.FromUnits(2), 240)]);
    }

    [Fact]
    public void Requests_AfterACartTakesAFullLoad_ThePickupIsGone()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(240);

        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("wool"), Amount.FromUnits(2)).ShouldBeTrue();

        Requests(h).ShouldBeEmpty();
        h.Output(farm, "wool").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Requests_AnotherLoadStillWaiting_OpensAFreshPickupFromNow()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(480);
        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(4));

        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("wool"), Amount.FromUnits(2)).ShouldBeTrue();

        Requests(h).ShouldBe([new TransportRequest(farm, TransportDirection.Pickup, h.Good("wool"), Amount.FromUnits(2), 480)]);
    }

    [Fact]
    public void TryTakeOutput_MoreThanTheBufferHolds_TakesNothing()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(240);

        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("wool"), Amount.FromMilli(2001)).ShouldBeFalse();

        h.Output(farm, "wool").ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void TryTakeOutput_ForAnythingElse_ReturnsFalse()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(240);

        // A good the farm does not make, a building that does not exist, and a building that makes nothing.
        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("fish"), Amount.FromUnits(1)).ShouldBeFalse();
        h.Context.ProductionBuffers.TryTakeOutput(new EntityId(999), h.Good("wool"), Amount.FromUnits(1)).ShouldBeFalse();
        h.Context.ProductionBuffers.TryTakeOutput(h.Add("storehouse", 20, 20), h.Good("wool"), Amount.FromUnits(1)).ShouldBeFalse();
    }

    [Fact]
    public void Requests_InputBelowACartLoad_OpensADeliveryForTheFirstTick()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);

        h.RunTicks(30);

        Requests(h).ShouldBe([new TransportRequest(mill, TransportDirection.Delivery, h.Good("grain"), Amount.FromUnits(2), 1)]);
    }

    [Fact]
    public void Requests_AGatherer_NeverAsksForDeliveries()
    {
        var h = ProductionHarness.Create();
        h.AddReady("sheep_farm", 2, 2);

        h.RunTicks(30);

        Requests(h).ShouldBeEmpty();
    }

    [Fact]
    public void AddInput_AFullLoad_ClosesTheDeliveryAndNoNewOneOpens()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        h.RunTicks(10);

        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(2));
        Requests(h).ShouldBeEmpty();

        h.RunTicks(10);
        Requests(h).ShouldBeEmpty();
        h.Input(mill, "grain").ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void AddInput_LessThanALoad_ReopensTheDeliveryOnTheNextTick()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        h.RunTicks(10);

        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(1));
        Requests(h).ShouldBeEmpty();

        h.RunTicks(1);
        Requests(h).ShouldBe([new TransportRequest(mill, TransportDirection.Delivery, h.Good("grain"), Amount.FromUnits(2), 11)]);
    }

    [Fact]
    public void AddInput_CompletingACycle_OpensADeliveryOnceTheBufferDropsBelowALoad()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        h.Context.ProductionBuffers.AddInput(mill, h.Good("grain"), Amount.FromUnits(2));

        h.RunTicks(119);
        Requests(h).ShouldBeEmpty();

        // The first cycle ends on tick 120 and uses one grain, leaving one: below a load of two.
        h.RunTicks(1);
        Requests(h).ShouldBe([new TransportRequest(mill, TransportDirection.Delivery, h.Good("grain"), Amount.FromUnits(2), 120)]);
    }

    [Fact]
    public void AddInput_ForAGoodTheRecipeDoesNotUse_IsIgnored()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);

        h.Context.ProductionBuffers.AddInput(mill, h.Good("fish"), Amount.FromUnits(2));
        h.Context.ProductionBuffers.AddInput(new EntityId(999), h.Good("grain"), Amount.FromUnits(2));

        h.Input(mill, "grain").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Requests_FromSeveralBuildings_AreOrderedByTickThenBuildingID()
    {
        var h = ProductionHarness.Create();

        // Mill A (the lowest ID) starts with grain, so it asks only after its first cycle, on tick 120. Mills B and C ask on tick 1.
        EntityId a = h.AddReady("windmill", 2, 2);
        EntityId b = h.AddReady("windmill", 6, 2);
        EntityId c = h.AddReady("windmill", 10, 2);
        h.Context.ProductionBuffers.AddInput(a, h.Good("grain"), Amount.FromUnits(2));
        h.RunTicks(120);

        var requests = new List<TransportRequest> { new(new EntityId(500), TransportDirection.Pickup, h.Good("wool"), Amount.FromUnits(1), 999) };
        h.Context.ProductionBuffers.ListRequests(requests);

        // What the list already held stays where it was, and the new requests follow in order.
        requests.Select(static r => (r.Building.Value, r.RequestTick)).ShouldBe(
            [(500, 999L), (b.Value, 1L), (c.Value, 1L), (a.Value, 120L)]);
    }

    [Fact]
    public void Requests_BothKindsFromOneBuildingOnTheSameTick_ListPickupsFirst()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);

        // Give the mill a full load of flour and no grain: a pickup and a delivery, both opened on tick 1.
        h.State.Producers.TryGet(mill, out ProducerRecord? record).ShouldBeTrue();
        record.ShouldNotBeNull();
        record.Outputs[0] = Amount.FromUnits(2);
        h.RunTicks(1);

        Requests(h).Select(static r => (r.Direction, r.RequestTick)).ShouldBe(
            [(TransportDirection.Pickup, 1L), (TransportDirection.Delivery, 1L)]);
    }

    [Fact]
    public void Requests_APausedBuilding_StillAsksForPickupsButNotForDeliveries()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        EntityId farm = h.AddReady("sheep_farm", 8, 2);
        h.RunTicks(240);
        Requests(h).Select(static r => r.Direction).ShouldBe([TransportDirection.Delivery, TransportDirection.Pickup]);

        h.Sim.Services.BuildingRegistry.SetState(mill, BuildingState.Paused);
        h.Sim.Services.BuildingRegistry.SetState(farm, BuildingState.Paused);
        h.RunTicks(1);

        Requests(h).ShouldBe([new TransportRequest(farm, TransportDirection.Pickup, h.Good("wool"), Amount.FromUnits(2), 240)]);
    }

    [Fact]
    public void Requests_ABuildingThatIsRemoved_LeavesTheList()
    {
        var h = ProductionHarness.Create();
        EntityId mill = h.AddReady("windmill", 2, 2);
        h.RunTicks(5);
        Requests(h).Count.ShouldBe(1);

        h.Sim.Services.BuildingRegistry.Remove(mill);

        Requests(h).ShouldBeEmpty();
    }
}
