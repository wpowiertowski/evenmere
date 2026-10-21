using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsQueryTests
{
    [Fact]
    public void FreeCarts_TracksAssignmentsAndCompletions()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 2000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");

        h.System.Queries.FreeCarts(storehouse).ShouldBe(2);

        h.RunTicks(1);
        h.System.Queries.FreeCarts(storehouse).ShouldBe(1);

        // The producer vanishes: the job finishes its phase, the cart comes home, and nothing re-serves the stale request.
        h.Sim.Services.BuildingRegistry.Remove(hut);
        h.RunTicks(30);
        h.Jobs().ShouldBeEmpty();
        h.System.Queries.FreeCarts(storehouse).ShouldBe(2);
    }

    [Fact]
    public void FreeCarts_NonStorehouseSiteAndUnknownId_HaveNoCarts()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        EntityId site = h.Add("storehouse", 20, 20, BuildingState.Site);

        h.System.Queries.FreeCarts(storehouse).ShouldBe(2);
        h.System.Queries.FreeCarts(hut).ShouldBe(0);
        h.System.Queries.FreeCarts(site).ShouldBe(0);
        h.System.Queries.FreeCarts(new EntityId(9999)).ShouldBe(0);
    }

    [Fact]
    public void ActiveJobs_ListsJobsInJobIdOrder()
    {
        var h = LogisticsHarness.Create();
        EntityId hall = h.Add("founders_hall", 0, 0);
        EntityId first = h.Add("woodcutters_hut", 2, 6);
        EntityId second = h.Add("woodcutters_hut", 8, 12);
        h.SetReach(hall, first, 2000);
        h.SetReach(hall, second, 2000);
        h.SetOutput(first, "wood", h.CartLoad);
        h.SetOutput(second, "wood", h.CartLoad);
        h.OpenPickup(first, "wood");
        h.OpenPickup(second, "wood");

        h.RunTicks(1);

        TransportJobView[] jobs = h.Jobs();
        jobs.Length.ShouldBe(2);
        jobs[0].Id.Value.ShouldBeLessThan(jobs[1].Id.Value);
        jobs[0].Storehouse.ShouldBe(hall);
        jobs[1].Storehouse.ShouldBe(hall);
    }

    [Fact]
    public void ActiveJobs_EmptyWorld_ReturnsAnEmptySpan()
    {
        var h = LogisticsHarness.Create();

        h.System.Queries.ActiveJobs().Length.ShouldBe(0);
    }

    [Fact]
    public void Queries_CartSpeedBonus_ShortensTheLeg()
    {
        var h = LogisticsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        EntityId hut = h.Add("woodcutters_hut", 10, 10);
        h.SetReach(storehouse, hut, 10000);
        h.SetOutput(hut, "wood", h.CartLoad);
        h.OpenPickup(hut, "wood");
        // A +50% cart-speed modifier (BonusPercent returns a percentage) halves the 20-tick leg.
        h.Sim.Queries.Progression.AddModifier(
            Stat.CartSpeed,
            new ModifierContribution(EffectSource.ForTech(h.Content.IdOf<TechId>("net_weaving")), 50, true));

        h.RunTicks(1);

        TransportJobView job = h.Jobs().ShouldHaveSingleItem();
        job.PhaseTicks.ShouldBe(10);
    }
}
