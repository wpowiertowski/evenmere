using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Tests.Workforce;

public sealed class WorkforceAllocationTests
{
    [Fact]
    public void Allocate_SupplyTenAgainstDemandFifteenAcrossThreeBuildings_FillsSumToExactlyTenWorkers()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);
        EntityId third = h.Add("test_workshop", 8, 0);

        h.SetSupply("settlers", 10);
        h.Allocate();

        // Each workshop asks for 5 of 15: the equal per-mille fill is floor(10 × 1000 / 15) = 666, so each gets
        // floor(5 × 666 / 1000) = 3, and the one leftover worker goes to the lowest ID.
        h.Allocated(first).ShouldBe(4);
        h.Allocated(second).ShouldBe(3);
        h.Allocated(third).ShouldBe(3);
        (h.Allocated(first) + h.Allocated(second) + h.Allocated(third)).ShouldBe(10);
        h.Queries.FillOf(first).PerMille.ShouldBe(800);
        h.Queries.FillOf(second).PerMille.ShouldBe(600);
    }

    [Fact]
    public void Allocate_PriorityBuilding_IsFilledFirst()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);
        EntityId third = h.Add("test_workshop", 8, 0);
        h.Send(new SetWorkforcePriority(second, true));

        h.SetSupply("settlers", 10);
        h.Allocate();

        // The prioritised workshop takes its full 5 first; the remaining 5 split 3 and 2 across the other two.
        h.Allocated(second).ShouldBe(5);
        h.Allocated(first).ShouldBe(3);
        h.Allocated(third).ShouldBe(2);
        h.Queries.FillOf(second).PerMille.ShouldBe(1000);
    }

    [Fact]
    public void Allocate_ZeroSupply_GivesZeroFill()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);

        h.SetSupply("settlers", 0);
        h.Allocate();

        h.Allocated(first).ShouldBe(0);
        h.Allocated(second).ShouldBe(0);
        h.Queries.FillOf(first).PerMille.ShouldBe(0);
        h.Queries.FillOf(second).PerMille.ShouldBe(0);
    }

    [Fact]
    public void Allocate_SupplyCoversDemand_FillsEveryBuilding()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);

        h.SetSupply("settlers", 10);
        h.Allocate();

        h.Allocated(first).ShouldBe(5);
        h.Allocated(second).ShouldBe(5);
        h.Queries.FillOf(first).PerMille.ShouldBe(1000);
    }

    [Fact]
    public void Allocate_PausedBuilding_DemandsNothingAndKeepsNoWorkers()
    {
        var h = WorkforceHarness.Create();
        h.Add("test_workshop", 0, 0, BuildingState.Paused);
        EntityId working = h.Add("test_workshop", 4, 0);

        h.Queries.Demand(h.Tier("settlers")).ShouldBe(5);

        h.SetSupply("settlers", 10);
        h.Allocate();

        h.Allocated(working).ShouldBe(5);
    }

    [Fact]
    public void Allocate_SiteAndRuin_DemandNothing()
    {
        var h = WorkforceHarness.Create();
        h.Add("test_workshop", 0, 0, BuildingState.Site);
        h.Add("test_workshop", 4, 0, BuildingState.Ruin);
        h.Add("test_workshop", 8, 0);

        h.Queries.Demand(h.Tier("settlers")).ShouldBe(5);
    }

    [Fact]
    public void Run_TwiceFromTheSameArrangement_GivesTheSameHash()
    {
        static ulong RunOnce()
        {
            var h = WorkforceHarness.Create();
            h.Add("test_workshop", 0, 0);
            EntityId second = h.Add("test_workshop", 4, 0);
            h.Add("test_workshop", 8, 0);
            h.Send(new SetWorkforcePriority(second, true));
            h.SetSupply("settlers", 10);
            h.Allocate();
            return h.Hash();
        }

        RunOnce().ShouldBe(RunOnce());
    }
}
