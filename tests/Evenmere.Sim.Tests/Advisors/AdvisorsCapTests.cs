using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>The calm cap: at most five notes show, showing notes are never bumped, and clearing one promotes the best queued note.</summary>
public sealed class AdvisorsCapTests
{
    [Fact]
    public void Cap_SevenProblems_FiveShowAndTwoQueue()
    {
        var h = AdvisorsHarness.Create();
        AddRoadlessHuts(h, 7);

        h.Run();

        AdviceView[] active = h.Active();
        active.Length.ShouldBe(5);
        h.State.Records.Length.ShouldBe(7);
        h.State.Records.Count(static r => r.Showing).ShouldBe(5);
        h.State.Records.Count(static r => !r.Showing).ShouldBe(2);

        // The five lowest IDs show: the queue holds the two newest.
        int[] showingIds = active.Select(static a => a.Id).ToArray();
        showingIds.ShouldBe(showingIds.Order().ToArray(), "showing records keep their ID order within a kind");
    }

    [Fact]
    public void Cap_ClearingOne_PromotesTheHighestPriorityQueuedNote()
    {
        var h = AdvisorsHarness.Create();
        EntityId[] huts = AddRoadlessHuts(h, 7);

        h.Run();
        AdviceView[] before = h.Active();
        before.Length.ShouldBe(5);
        h.Events();

        // Fail a good too; after two days it queues behind the showing road notes.
        GoodId fish = h.Good("fish");
        h.Sim.Queries.Needs.SetDemandYesterday(fish, Amount.FromUnits(10));
        h.Sim.Queries.Needs.SetFulfilment(fish, Ratio.FromPerMille(700));
        h.AdvanceDays(2);
        h.Active().Select(static a => a.Id).ShouldBe(before.Select(static a => a.Id), "a higher-priority newcomer queues without bumping");
        h.State.Records.Length.ShouldBe(8);
        h.Events();

        // Clearing the lowest showing road note promotes the queued shortage, which outranks the queued road notes.
        h.Sim.Queries.Roads.GrantRoadAccess(huts[0]);
        h.AdvanceDays(1);

        AdviceView[] active = h.Active();
        active.Length.ShouldBe(5);
        active[0].Kind.ShouldBe(AdviceKind.GoodShortage);
        active[0].Subject.ShouldBe(SubjectLink.ForGood(fish));

        IReadOnlyList<SimEvent> events = h.Events();
        events.Count.ShouldBe(2);
        events[0].ShouldBe(new AdviceCleared(360, before[0].Id, AdviceKind.NoRoadAccess));
        events[1].ShouldBe(new AdviceRaised(360, active[0]));
    }

    [Fact]
    public void Showing_NewerHigherPriorityProblem_DoesNotBump()
    {
        var h = AdvisorsHarness.Create();
        AddRoadlessHuts(h, 5);

        h.Run();
        AdviceView[] before = h.Active();
        before.Length.ShouldBe(5);
        h.Events();

        // A good shortage appears two days later; it is more urgent but stays queued.
        GoodId fish = h.Good("fish");
        h.Sim.Queries.Needs.SetDemandYesterday(fish, Amount.FromUnits(10));
        h.Sim.Queries.Needs.SetFulfilment(fish, Ratio.FromPerMille(700));
        h.AdvanceDays(2);

        AdviceView[] active = h.Active();
        active.Select(static a => a.Id).ShouldBe(before.Select(static a => a.Id));
        active.All(static a => a.Kind == AdviceKind.NoRoadAccess).ShouldBeTrue();

        // The shortage was recorded but never shown, so nothing was emitted and nothing moved.
        h.State.Records.Length.ShouldBe(6);
        h.State.Records.Count(static r => r.Showing).ShouldBe(5);
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void Active_ReturnsPriorityThenAgeOrder()
    {
        var h = AdvisorsHarness.Create();

        // Research idleness shows at once; the reach problem outranks it by kind, not by age.
        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(4);
        EntityId hut = h.Add("woodcutters_hut", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(hut);

        h.Run();

        AdviceView[] active = h.Active();
        active.Length.ShouldBe(2);
        active[0].Kind.ShouldBe(AdviceKind.OutsideStorehouseReach);
        active[1].Kind.ShouldBe(AdviceKind.ResearchIdle);
    }

    [Fact]
    public void QueuedRecord_ClearingWithoutShowing_EmitsNothing()
    {
        var h = AdvisorsHarness.Create();
        EntityId[] huts = AddRoadlessHuts(h, 6);

        h.Run();
        h.Active().Length.ShouldBe(5);
        h.Events();

        // Fix the queued building (the sixth) while five notes show: it clears silently and no slot opens.
        h.Sim.Queries.Roads.GrantRoadAccess(huts[5]);
        h.AdvanceDays(1);

        h.Active().Length.ShouldBe(5);
        h.State.Records.Length.ShouldBe(5);
        h.Events().ShouldBeEmpty();
    }

    private static EntityId[] AddRoadlessHuts(AdvisorsHarness h, int count)
    {
        EntityId hall = h.Add("storehouse", 24, 24);
        h.Sim.Queries.Roads.GrantRoadAccess(hall);
        var huts = new EntityId[count];
        for (int i = 0; i < count; i++)
        {
            huts[i] = h.Add("woodcutters_hut", i * 3, 0);
            h.Sim.Queries.Roads.SetReach(hall, huts[i], 1000);
        }

        return huts;
    }
}
