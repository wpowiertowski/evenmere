using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Tests.Culture;

public sealed class CultureCommandTests
{
    [Fact]
    public void SetTarget_LockedCivic_IsRejected()
    {
        var h = CultureHarness.Create();

        h.Send(new SetCivicTarget(h.Civic("test_custom2")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
        h.Queries.Target.ShouldBeNull();
    }

    [Fact]
    public void SetTarget_EraLockedCivic_IsRejected()
    {
        var h = CultureHarness.Create();

        h.Send(new SetCivicTarget(h.Civic("test_future_civic")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
        h.Queries.Target.ShouldBeNull();
    }

    [Fact]
    public void SetTarget_AdoptedCivic_IsRejected()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.CreditCulture(40);
        h.Run();
        h.Queries.IsDone(custom).ShouldBeTrue();
        h.Events();

        h.Send(new SetCivicTarget(custom));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);
    }

    [Fact]
    public void SetTarget_QueuedCivic_PromotesItOutOfTheQueue()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicQueue([custom]));
        h.Events();

        h.Send(new SetCivicTarget(custom));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(custom);
        h.Queries.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void SetQueue_UnreachableAndDuplicateEntries_AreRejected()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        CivicId custom3 = h.Civic("test_custom3");
        h.Send(new SetCivicTarget(custom));

        // custom3 needs custom2, which is neither adopted nor earlier in the queue nor the target.
        h.Send(new SetCivicQueue([custom3]));
        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);

        // The target itself cannot be queued, and neither can a civic twice.
        h.Send(new SetCivicQueue([custom]));
        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);

        h.Send(new SetCivicQueue([custom2, custom2]));
        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.NodeNotAvailable);

        h.Queries.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void SetQueue_ChainThroughTheTarget_IsAccepted()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        CivicId custom3 = h.Civic("test_custom3");
        h.Send(new SetCivicTarget(custom));

        h.Send(new SetCivicQueue([custom2, custom3]));

        h.Events().ShouldBeEmpty();
        h.Queries.Queue.ShouldBe([custom2, custom3]);
    }

    [Fact]
    public void SetTarget_Retarget_PruneOrphanedQueue()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        CivicId moot = h.Civic("village_moot");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2]));
        h.Events();

        // Custom2 needs custom, which is neither adopted nor the new target nor earlier in the queue.
        h.Send(new SetCivicTarget(moot));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(moot);
        h.Queries.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void SetTarget_Retarget_KeepsReachableEntries()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        CivicId moot = h.Civic("village_moot");
        CivicId tales = h.Civic("test_tales");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2, tales]));
        h.Events();

        // Custom2 loses custom and drops, taking nothing with it; tales needs nothing and stays.
        h.Send(new SetCivicTarget(moot));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(moot);
        h.Queries.Queue.ShouldBe([tales]);
    }

    [Fact]
    public void SetTarget_SameTarget_KeepsTheQueue()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2]));
        h.Events();

        h.Send(new SetCivicTarget(custom));

        h.Events().ShouldBeEmpty();
        h.Queries.Target.ShouldBe(custom);
        h.Queries.Queue.ShouldBe([custom2]);
    }

    [Fact]
    public void Retarget_PrunedCivic_NeverCompletes()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        CivicId moot = h.Civic("village_moot");
        CivicId tales = h.Civic("test_tales");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2, tales]));
        h.Send(new SetCivicTarget(moot));
        h.Events();

        // The moot costs 140; adopting it pops tales, never the pruned custom2.
        h.CreditCulture(140);
        h.Run();

        h.Queries.IsDone(moot).ShouldBeTrue();
        h.Queries.IsDone(custom2).ShouldBeFalse();
        h.Queries.Target.ShouldBe(tales);
        h.Events().ShouldBe([new CivicAdopted(h.Sim.Context.Tick, moot)]);
    }
}
