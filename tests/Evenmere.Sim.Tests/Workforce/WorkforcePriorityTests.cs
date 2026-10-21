using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Tests.Workforce;

public sealed class WorkforcePriorityTests
{
    [Fact]
    public void SetWorkforcePriority_ValidBuilding_SetsAndClearsTheFlag()
    {
        var h = WorkforceHarness.Create();
        EntityId workshop = h.Add("test_workshop", 0, 0);

        h.Queries.IsHighPriority(workshop).ShouldBeFalse();

        h.Send(new SetWorkforcePriority(workshop, true));

        h.Queries.IsHighPriority(workshop).ShouldBeTrue();

        h.Send(new SetWorkforcePriority(workshop, false));

        h.Queries.IsHighPriority(workshop).ShouldBeFalse();
    }

    [Fact]
    public void SetWorkforcePriority_BuildingWithNoWorkforceSpec_IsRejectedAsNotApplicable()
    {
        var h = WorkforceHarness.Create();
        EntityId house = h.Add("settler_house", 0, 0);

        h.Send(new SetWorkforcePriority(house, true));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.NotApplicable);
        h.Queries.IsHighPriority(house).ShouldBeFalse();
    }

    [Fact]
    public void SetWorkforcePriority_UnknownBuilding_IsRejectedAsUnknownTarget()
    {
        var h = WorkforceHarness.Create();

        h.Send(new SetWorkforcePriority(new EntityId(999), true));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.UnknownTarget);
    }
}
