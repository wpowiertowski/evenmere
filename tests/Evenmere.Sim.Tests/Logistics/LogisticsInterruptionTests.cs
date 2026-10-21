using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Logistics;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsInterruptionTests
{
    [Fact]
    public void Interruption_StorehouseRemovedMidPickup_ReturnsTheLoadToThePool()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        // Drive the cart past its load, so it carries a full cart home.
        h.RunTicks(25);
        h.Jobs().ShouldHaveSingleItem().Phase.ShouldBe(TransportPhase.TravelBack);

        h.Sim.Services.BuildingRegistry.Remove(storehouse);
        h.RunTicks(20);

        // The job ends when its phase ends; the load reaches the pool, not the missing hall.
        h.Jobs().ShouldBeEmpty();
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(h.CartLoad);
        h.Events().OfType<TransportJobCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Interruption_StorehouseRemovedMidDelivery_ReturnsTheReservationToThePool()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId bakery = h.Add("sawpit", 10, 10);
        h.SetReach(storehouse, bakery, 10000);
        h.Sim.Services.Storage.Set(h.Good("wood"), h.CartLoad);
        h.OpenDelivery(bakery, "wood");

        h.RunTicks(5);
        h.Jobs().ShouldHaveSingleItem().Phase.ShouldBe(TransportPhase.TravelOut);
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.Zero);

        h.Sim.Services.BuildingRegistry.Remove(storehouse);
        h.RunTicks(16);

        h.Jobs().ShouldBeEmpty();
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(h.CartLoad);
        h.Sim.Services.ProductionBuffers.InputOf(bakery, h.Good("wood")).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Interruption_TargetRemovedMidJob_LosesNoGoodsAndFreesTheCart()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.RunTicks(5);
        h.Sim.Services.BuildingRegistry.Remove(hut);

        // The cart finishes its leg, finds no producer and comes home empty.
        h.RunTicks(16);
        h.Jobs().ShouldBeEmpty();
        h.System.Queries.FreeCarts(storehouse).ShouldBe(2);
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.Zero);

        IReadOnlyList<SimEvent> events = h.Events();
        events.OfType<TransportJobStarted>().ShouldHaveSingleItem();
        events.OfType<TransportJobCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Interruption_RemovalBeforeLoading_LosesNoGoods()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.RunTicks(25);
        h.Jobs().ShouldHaveSingleItem().Phase.ShouldBe(TransportPhase.TravelBack);

        // The producer vanishes on the way home; the cart's load still reaches the pool.
        h.Sim.Services.BuildingRegistry.Remove(hut);
        h.RunTicks(20);

        h.Jobs().ShouldBeEmpty();
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(h.CartLoad);
    }
}
