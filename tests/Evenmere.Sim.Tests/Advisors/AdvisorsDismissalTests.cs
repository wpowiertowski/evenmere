using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Advisors.Commands;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>Dismissal suppresses a (kind, subject) for exactly one season, and unknown IDs are rejected.</summary>
public sealed class AdvisorsDismissalTests
{
    [Fact]
    public void DismissAdvice_ShowingNote_RemovesItAndSuppressesForOneSeason()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);
        h.AddReachedStorehouse(hut, 8, 8);
        h.Run();
        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        int firstId = active[0].Id;
        h.Events();

        h.Send(new DismissAdvice(firstId));

        // The showing note clears with an event, and the problem raises nothing while suppressed.
        h.Events().ShouldBe([new AdviceCleared(0, firstId, AdviceKind.NoRoadAccess)]);
        h.Active().ShouldBeEmpty();

        // Nine days pass with the problem still present: nothing re-raises.
        h.AdvanceDays(9);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();

        // On day ten the suppression has ended, so the problem raises a new record with a new ID.
        h.AdvanceDays(1);
        AdviceView[] renewed = h.Active();
        renewed.ShouldHaveSingleItem();
        renewed[0].Kind.ShouldBe(AdviceKind.NoRoadAccess);
        renewed[0].Subject.ShouldBe(SubjectLink.ForBuilding(hut));
        renewed[0].Id.ShouldNotBe(firstId);
        renewed[0].FirstSeenTick.ShouldBe(1200);
        h.Events().ShouldBe([new AdviceRaised(1200, renewed[0])]);
    }

    [Fact]
    public void DismissAdvice_QueuedNote_RemovesItSilently()
    {
        var h = AdvisorsHarness.Create();
        for (int i = 0; i < 6; i++)
        {
            EntityId hut = h.Add("woodcutters_hut", i * 4, 0);
            h.AddReachedStorehouse(hut, i * 4, 8);
        }

        h.Run();
        AdviceView[] active = h.Active();
        active.Length.ShouldBe(5);
        h.Events();

        // The queued record is a live record too: find it through the state and dismiss it.
        int queuedId = h.State.Records.Single(static r => !r.Showing).Id;
        h.Send(new DismissAdvice(queuedId));

        // A queued dismissal emits nothing and frees no slot.
        h.Events().ShouldBeEmpty();
        h.Active().Select(static a => a.Id).ShouldBe(active.Select(static a => a.Id));
    }

    [Fact]
    public void DismissAdvice_UnknownId_IsRejected()
    {
        var h = AdvisorsHarness.Create();
        h.Run();
        h.Events();

        h.Send(new DismissAdvice(999));

        IReadOnlyList<SimEvent> events = h.Events();
        CommandRejected rejected = events.OfType<CommandRejected>().ShouldHaveSingleItem();
        rejected.Code.ShouldBe(RejectReason.UnknownTarget);
        rejected.Command.ShouldBe(new DismissAdvice(999));
    }

    [Fact]
    public void DismissAdvice_ClearedId_IsRejected()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);
        h.AddReachedStorehouse(hut, 8, 8);
        h.Run();
        int id = h.Active().Single().Id;

        // Fixing the problem clears the note; its ID stays unknown from then on.
        h.Sim.Queries.Roads.GrantRoadAccess(hut);
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events();

        h.Send(new DismissAdvice(id));

        h.Events().OfType<CommandRejected>().ShouldHaveSingleItem().Code.ShouldBe(RejectReason.UnknownTarget);
    }
}
