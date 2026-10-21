using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Logistics;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsDeliveryTests
{
    [Fact]
    public void Delivery_ReservesAtAssignmentAndUnloadsAfterOneRoundTrip()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId bakery = h.Add("sawpit", 10, 10);
        h.SetReach(storehouse, bakery, 5000);
        h.Sim.Services.Storage.Set(h.Good("wood"), Amount.FromUnits(10));
        h.OpenDelivery(bakery, "wood");

        h.RunTicks(1);
        long assignedTick = h.Sim.World.Clock.Tick;
        TransportJobView first = h.Jobs().ShouldHaveSingleItem();
        first.Direction.ShouldBe(TransportDirection.Delivery);
        first.Phase.ShouldBe(TransportPhase.TravelOut);

        // The reservation leaves the pool at assignment, before the cart arrives.
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.FromUnits(8));

        // A delivery runs out, unload, back: 10 + 4 + 10 ticks.
        int leg = LogisticsSystem.TravelTicks(5000, h.Content.Rules.CartTicksPerTile, 0);
        leg.ShouldBe(10);
        do
        {
            h.RunTicks(1);
        }
        while (h.Jobs().Any(j => j.Id == first.Id) && h.Sim.World.Clock.Tick - assignedTick < 1000);

        // The first job is done and a follow-up started, because the fake's request stays listed.
        h.Jobs().ShouldHaveSingleItem().Id.ShouldNotBe(first.Id);
        (h.Sim.World.Clock.Tick - assignedTick).ShouldBe(leg + LogisticsSystem.HandlingTicks + leg);
        h.Sim.Services.ProductionBuffers.InputOf(bakery, h.Good("wood")).ShouldBe(h.CartLoad);

        // The first cart delivered its load while the follow-up reserved another: 10 − 2 × cart load.
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.FromUnits(6));

        IReadOnlyList<SimEvent> events = h.Events();
        events.Count.ShouldBe(3);
        events[0].ShouldBeOfType<TransportJobStarted>().Direction.ShouldBe(TransportDirection.Delivery);
        TransportJobCompleted completed = events[1].ShouldBeOfType<TransportJobCompleted>();
        completed.Amount.ShouldBe(h.CartLoad);
    }

    [Fact]
    public void Delivery_ShortPool_WaitsUntilTheNextDayBeforeRetrying()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId bakery = h.Add("sawpit", 10, 10);
        h.SetReach(storehouse, bakery, 2000);
        h.OpenDelivery(bakery, "wood");

        h.RunTicks(5);
        h.Jobs().ShouldBeEmpty();

        // Stock arrives later the same day, but the failed reservation does not retry yet.
        h.Sim.Services.Storage.Set(h.Good("wood"), Amount.FromUnits(5));
        h.RunTicks(5);
        h.Sim.World.Clock.Day.ShouldBe(0);
        h.Jobs().ShouldBeEmpty();

        // The next day the request is served again.
        h.RunTicks(109);
        h.Sim.World.Clock.Day.ShouldBe(0);
        h.Jobs().ShouldBeEmpty();
        h.RunTicks(1);
        h.Sim.World.Clock.Day.ShouldBe(1);
        h.Jobs().ShouldHaveSingleItem();
    }

    [Fact]
    public void Delivery_OnceReserved_RetriesWithoutAnotherReservation()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId bakery = h.Add("sawpit", 10, 10);
        h.SetReach(storehouse, bakery, 2000);
        h.Sim.Services.Storage.Set(h.Good("wood"), h.CartLoad);
        h.OpenDelivery(bakery, "wood");

        h.RunTicks(1);

        // The whole pool rides on the cart; the request no longer competes for it.
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.Zero);
        h.Jobs().ShouldHaveSingleItem();
    }

    [Fact]
    public void Delivery_ReservationsNeverDriveThePoolNegative()
    {
        var random = new Random(1234);
        for (int trial = 0; trial < 20; trial++)
        {
            var h = LogisticsHarness.Create();
            EntityId storehouse = h.Add("storehouse", 0, 0);
            EntityId hut = h.Add("woodcutters_hut", 4, 4);
            EntityId bakery = h.Add("sawpit", 12, 12);
            h.SetReach(storehouse, hut, 1000 + random.Next(9000));
            h.SetReach(storehouse, bakery, 1000 + random.Next(9000));

            var pool = Amount.FromUnits(random.Next(7));
            Amount output = random.Next(2) == 0 ? Amount.Zero : h.CartLoad;
            h.Sim.Services.Storage.Set(h.Good("wood"), pool);
            h.SetOutput(hut, "wood", output);
            h.OpenPickup(hut, "wood");
            h.OpenDelivery(bakery, "wood");

            Amount initialTotal = pool + output;
            for (int i = 0; i < 300; i++)
            {
                h.RunTicks(1);
                h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBeGreaterThanOrEqualTo(Amount.Zero);

                // Goods in flight are invisible, so the visible total never grows.
                Amount visible = h.Sim.Services.Storage.Get(h.Good("wood"))
                    + h.Sim.Services.ProductionBuffers.OutputOf(hut, h.Good("wood"))
                    + h.Sim.Services.ProductionBuffers.InputOf(bakery, h.Good("wood"));
                visible.ShouldBeLessThanOrEqualTo(initialTotal);
            }
        }
    }
}
