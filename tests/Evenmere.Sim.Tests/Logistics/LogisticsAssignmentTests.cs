using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Logistics;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsAssignmentTests
{
    [Fact]
    public void Assignment_TwoStorehouses_ServesEachRequestFromTheNearerOne()
    {
        var h = LogisticsHarness.Create();
        EntityId near = h.Add("storehouse", 0, 0);
        EntityId far = h.Add("storehouse", 20, 20);
        EntityId first = h.Add("woodcutters_hut", 2, 4);
        EntityId second = h.Add("sawpit", 22, 24);
        h.SetReach(near, first, 2000);
        h.SetReach(far, first, 15000);
        h.SetReach(near, second, 15000);
        h.SetReach(far, second, 2000);
        h.SetOutput(first, "wood", h.CartLoad);
        h.SetOutput(second, "timber", h.CartLoad);
        h.OpenPickup(first, "wood");
        h.OpenPickup(second, "timber");

        h.RunTicks(1);

        TransportJobView[] jobs = h.Jobs();
        jobs.Length.ShouldBe(2);
        jobs.Single(j => j.Building == first).Storehouse.ShouldBe(near);
        jobs.Single(j => j.Building == second).Storehouse.ShouldBe(far);
    }

    [Fact]
    public void Assignment_EqualCosts_BreaksTheTieByLowerStorehouseId()
    {
        var h = LogisticsHarness.Create();
        EntityId low = h.Add("storehouse", 0, 0);
        EntityId high = h.Add("storehouse", 20, 20);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(low, hut, 5000);
        h.SetReach(high, hut, 5000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.RunTicks(1);

        h.Jobs().ShouldHaveSingleItem().Storehouse.ShouldBe(low);
    }

    [Fact]
    public void Assignment_NoFreeCart_LeavesTheRequestWaiting()
    {
        var h = LogisticsHarness.Create();

        // The storehouse owns two carts; both are sent out first.
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId first = h.Add("woodcutters_hut", 2, 4);
        EntityId second = h.Add("woodcutters_hut", 6, 8);
        EntityId waiting = h.Add("woodcutters_hut", 12, 12);
        foreach (EntityId hut in new[] { first, second, waiting })
        {
            h.SetReach(storehouse, hut, 2000);
            h.SetOutput(hut, "wood", h.CartLoad);
            h.OpenPickup(hut, "wood");
        }

        h.RunTicks(1);

        TransportJobView[] jobs = h.Jobs();
        jobs.Length.ShouldBe(2);
        jobs.ShouldNotContain(j => j.Building == waiting);
        h.System.Queries.FreeCarts(storehouse).ShouldBe(0);
    }

    [Fact]
    public void Assignment_NoStorehouseInReach_LeavesTheRequestWaiting()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.RunTicks(10);

        h.Jobs().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
        h.System.Queries.FreeCarts(storehouse).ShouldBe(2);
    }

    [Fact]
    public void Assignment_RequestAlreadyServed_NeverStartsASecondJob()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        // The request stays listed the whole round trip; only one job may serve it.
        for (int i = 0; i < 48; i++)
        {
            h.RunTicks(1);
            h.Jobs().Length.ShouldBeLessThanOrEqualTo(1);
        }

        h.Events().OfType<TransportJobStarted>().Count().ShouldBe(1);
    }

    [Fact]
    public void Assignment_ServesRequestsInRequestTickThenBuildingOrder()
    {
        var h = LogisticsHarness.Create();

        // One cart between two storehouses would do; a founders' hall owns three carts, so use one hall for both.
        EntityId hall = h.Add("founders_hall", 0, 0);
        EntityId later = h.Add("woodcutters_hut", 2, 6);
        EntityId earlier = h.Add("woodcutters_hut", 8, 12);
        h.SetReach(hall, later, 2000);
        h.SetReach(hall, earlier, 2000);
        h.SetOutput(later, "wood", h.CartLoad);
        h.SetOutput(earlier, "wood", h.CartLoad);

        // The later building's request is older.
        h.OpenPickup(later, "wood");
        h.Sim.SetTick(5);
        h.OpenPickup(earlier, "wood");

        h.RunTicks(1);

        TransportJobView[] jobs = h.Jobs();
        jobs.Length.ShouldBe(2);
        jobs[0].Building.ShouldBe(later);
        jobs[1].Building.ShouldBe(earlier);
    }
}
