using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Logistics;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsRoundTripTests
{
    [Fact]
    public void Pickup_TenDirtTilesAway_CompletesInExactlyTwoLegsPlusHandling()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.RunTicks(1);
        TransportJobView first = h.Jobs().ShouldHaveSingleItem();
        long assignedTick = h.Sim.World.Clock.Tick;
        int leg = LogisticsSystem.TravelTicks(10000, h.Content.Rules.CartTicksPerTile, 0);
        leg.ShouldBe(20);
        first.Phase.ShouldBe(TransportPhase.TravelOut);
        first.PhaseStartTick.ShouldBe(assignedTick);
        first.PhaseTicks.ShouldBe(leg);

        // Follow the first job: a new one starts when it completes, because the fake's request stays listed.
        var phases = new List<TransportPhase> { first.Phase };
        do
        {
            h.RunTicks(1);
            TransportJobView[] current = h.Jobs();
            if (current.All(j => j.Id != first.Id))
            {
                break;
            }

            phases.Add(current.Single(j => j.Id == first.Id).Phase);
        }
        while (h.Sim.World.Clock.Tick - assignedTick < 1000);

        // 20 out, 4 load, 20 back, 4 unload: the cart is home 48 ticks after assignment.
        long completionTick = h.Sim.World.Clock.Tick;
        (completionTick - assignedTick).ShouldBe(2 * leg + 2 * LogisticsSystem.HandlingTicks);
        phases.ShouldBe(
            [.. Enumerable.Repeat(TransportPhase.TravelOut, leg),
             .. Enumerable.Repeat(TransportPhase.Load, LogisticsSystem.HandlingTicks),
             .. Enumerable.Repeat(TransportPhase.TravelBack, leg),
             .. Enumerable.Repeat(TransportPhase.Unload, LogisticsSystem.HandlingTicks)]);
    }

    [Fact]
    public void Pickup_RoundTrip_MovesOneCartLoadFromBufferToPool()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");
        h.Events();

        h.RunTicks(1);
        TransportJobView first = h.Jobs().ShouldHaveSingleItem();

        h.RunTicks(48);

        // The first job is done and a follow-up started, because the fake's request stays listed.
        h.Jobs().ShouldHaveSingleItem().Id.ShouldNotBe(first.Id);
        h.Sim.Services.ProductionBuffers.OutputOf(hut, h.Good("wood")).ShouldBe(Amount.Zero);
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(h.CartLoad);

        IReadOnlyList<SimEvent> events = h.Events();
        events.Count.ShouldBe(3);
        TransportJobStarted started = events[0].ShouldBeOfType<TransportJobStarted>();
        started.Storehouse.ShouldBe(storehouse);
        started.Building.ShouldBe(hut);
        started.Good.ShouldBe(h.Good("wood"));
        started.Amount.ShouldBe(h.CartLoad);
        started.Direction.ShouldBe(TransportDirection.Pickup);
        TransportJobCompleted completed = events[1].ShouldBeOfType<TransportJobCompleted>();
        completed.Job.ShouldBe(started.Job);
        completed.Storehouse.ShouldBe(storehouse);
        completed.Building.ShouldBe(hut);
        completed.Amount.ShouldBe(h.CartLoad);
        completed.Direction.ShouldBe(TransportDirection.Pickup);
    }

    [Fact]
    public void TravelTicks_RoundsUpAndNeverDropsBelowOne()
    {
        // Ten dirt tiles at two ticks a tile is a 20-tick leg with no bonus.
        LogisticsSystem.TravelTicks(10000, 2, 0).ShouldBe(20);
        LogisticsSystem.TravelTicks(1000, 2, 0).ShouldBe(2);
        LogisticsSystem.TravelTicks(1, 2, 0).ShouldBe(1);
        LogisticsSystem.TravelTicks(0, 2, 0).ShouldBe(1);

        // The bonus is a percentage: +20% of 20 ticks is 16, +50% halves the leg.
        LogisticsSystem.TravelTicks(10000, 2, 20).ShouldBe(16);
        LogisticsSystem.TravelTicks(10000, 2, 50).ShouldBe(10);

        // A very large bonus clamps to the 100 per-mille floor instead of going instant or negative.
        LogisticsSystem.TravelTicks(10000, 2, 95).ShouldBe(2);
        LogisticsSystem.TravelTicks(10000, 2, 500).ShouldBe(2);

        // A penalty slows the cart down.
        LogisticsSystem.TravelTicks(10000, 2, -25).ShouldBe(25);
    }
}
