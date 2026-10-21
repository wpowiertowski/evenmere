using Evenmere.Core.Ids;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Workforce;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Tests.Workforce;

public sealed class WorkforceLifecycleTests
{
    [Fact]
    public void OnRemoved_DropsTheRecordAndItsPriorityFlag()
    {
        var h = WorkforceHarness.Create();
        EntityId workshop = h.Add("test_workshop", 0, 0);
        h.Send(new SetWorkforcePriority(workshop, true));
        h.SetSupply("settlers", 5);
        h.Allocate();
        h.Allocated(workshop).ShouldBe(5);

        h.Sim.Services.BuildingRegistry.Remove(workshop);

        h.State.Workplaces.Contains(workshop).ShouldBeFalse();
        h.Queries.IsHighPriority(workshop).ShouldBeFalse();
        h.Queries.FillOf(workshop).PerMille.ShouldBe(0);

        // The allocation still runs cleanly with the building gone.
        h.Allocate();
    }

    [Fact]
    public void State_SaveRoundTrip_PreservesSupplyPriorityAndAllocation()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);
        h.Send(new SetWorkforcePriority(second, true));
        h.SetSupply("settlers", 7);
        h.Allocate();

        byte[] bytes = StateSerializer.Write(h.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, h.Content);
        WorkforceState state = restored.Workforce;

        state.RawSupply[h.Tier("settlers").Value].ShouldBe(7);
        state.Workplaces.TryGet(first, out WorkplaceRecord? firstRecord).ShouldBeTrue();
        firstRecord.ShouldNotBeNull();
        firstRecord.Allocated.ShouldBe(2);
        firstRecord.HighPriority.ShouldBeFalse();
        state.Workplaces.TryGet(second, out WorkplaceRecord? secondRecord).ShouldBeTrue();
        secondRecord.ShouldNotBeNull();
        secondRecord.Allocated.ShouldBe(5);
        secondRecord.HighPriority.ShouldBeTrue();
        StateSerializer.Write(restored).ShouldBe(bytes);
    }
}
