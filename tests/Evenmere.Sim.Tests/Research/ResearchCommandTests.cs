using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Tests.Research;

public sealed class ResearchCommandTests
{
    [Fact]
    public void SetTarget_LockedTech_IsRejected()
    {
        var h = ResearchHarness.Create();
        TechId practice = h.Tech("test_practice");

        h.Send(new SetResearchTarget(practice));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
        h.Queries.Target.ShouldBeNull();
    }

    [Fact]
    public void SetTarget_DoneTech_IsRejected()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.CreditKnowledge(60);
        h.Run();
        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Events();

        h.Send(new SetResearchTarget(theory));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
        h.Queries.Target.ShouldBeNull();
    }

    [Fact]
    public void SetTarget_EraLockedTech_IsRejected()
    {
        var h = ResearchHarness.Create();

        h.Send(new SetResearchTarget(h.Tech("test_future")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
        h.Queries.Target.ShouldBeNull();
    }

    [Fact]
    public void SetTarget_UnknownTech_IsRejected()
    {
        var h = ResearchHarness.Create();
        var unknown = TechId.From(999);

        h.Send(new SetResearchTarget(unknown));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
    }

    [Fact]
    public void SetTarget_QueuedTech_PromotesItOutOfTheQueue()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchQueue([theory]));
        h.Events();

        h.Send(new SetResearchTarget(theory));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(theory);
        h.Queries.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void SetQueue_ReplacesTheQueueAndClearsOnEmpty()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        h.Send(new SetResearchTarget(theory));

        h.Send(new SetResearchQueue([practice]));

        h.Queries.Queue.ShouldBe([practice]);

        h.Send(new SetResearchQueue([]));

        h.Queries.Queue.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("done")]
    [InlineData("unreachable")]
    [InlineData("target")]
    public void SetQueue_BadEntries_AreRejected(string kind)
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        TechId mastery = h.Tech("test_mastery");
        h.Send(new SetResearchTarget(theory));

        TechId[] queue = kind switch
        {
            "duplicate" => [practice, practice],
            "done" => [practice, theory],
            "unreachable" => [mastery],
            _ => [theory],
        };

        // For the "done" case the theory must already be researched, which also pops it as the target;
        // re-target it so the queue entry is done while a target stands.
        if (kind == "done")
        {
            h.CreditKnowledge(60);
            h.Run();
            h.Queries.IsDone(theory).ShouldBeTrue();
            h.Send(new SetResearchTarget(practice));
            h.Events();
        }

        h.Send(new SetResearchQueue(queue));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
    }

    [Fact]
    public void SetQueue_ChainThroughTheTarget_IsAccepted()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        TechId mastery = h.Tech("test_mastery");
        h.Send(new SetResearchTarget(theory));

        // Mastery needs practice, which is earlier in the queue; practice needs theory, which is the target.
        h.Send(new SetResearchQueue([practice, mastery]));

        h.Events().ShouldBeEmpty();
        h.Queries.Queue.ShouldBe([practice, mastery]);
    }

    [Fact]
    public void SetTarget_Retarget_PruneOrphanedQueue()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        TechId weaving = h.Tech("net_weaving");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice]));
        h.Events();

        // Practice needs theory, which is neither done nor the new target nor earlier in the queue.
        h.Send(new SetResearchTarget(weaving));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(weaving);
        h.Queries.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void SetTarget_Retarget_KeepsReachableEntries()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        TechId weaving = h.Tech("net_weaving");
        TechId lore = h.Tech("test_lore");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice, lore]));
        h.Events();

        // Practice loses theory and drops, taking nothing with it; lore needs nothing and stays.
        h.Send(new SetResearchTarget(weaving));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(weaving);
        h.Queries.Queue.ShouldBe([lore]);
    }

    [Fact]
    public void SetTarget_SameTarget_KeepsTheQueue()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice]));
        h.Events();

        h.Send(new SetResearchTarget(theory));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(theory);
        h.Queries.Queue.ShouldBe([practice]);
    }

    [Fact]
    public void Retarget_PrunedTech_NeverCompletes()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        TechId weaving = h.Tech("net_weaving");
        TechId lore = h.Tech("test_lore");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice, lore]));
        h.Send(new SetResearchTarget(weaving));
        h.Events();

        // Weaving costs 50; completing it pops lore, never the pruned practice.
        h.CreditKnowledge(50);
        h.Run();

        h.Queries.IsDone(weaving).ShouldBeTrue();
        h.Queries.IsDone(practice).ShouldBeFalse();
        h.Queries.Target.ShouldBe(lore);
        h.Events().ShouldBe([new TechResearched(h.Sim.Context.Tick, weaving)]);
    }
}
