using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>The daily roll: eligible happenings become pending in ascending ID order, and nothing rolls otherwise.</summary>
public sealed class HappeningRollTests
{
    [Fact]
    public void Run_EligibleHappening_RaisesItPendingAndEmitsRaised()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");

        h.Run();

        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningPending(0, h.Happening("test_feast")));
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
        h.Queries.PendingHappening().ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
        h.Queries.IsOnCooldown(h.Happening("test_feast")).ShouldBeFalse();
    }

    [Fact]
    public void Queries_PendingView_MirrorsPending()
    {
        HappeningsHarness empty = HappeningsHarness.Create().EligibleOnly();
        empty.Queries.PendingView().ShouldBeNull();

        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();

        h.Queries.PendingView().ShouldBe(new PendingHappeningView(h.Happening("test_feast"), 0));
    }

    [Fact]
    public void Run_TriggerFalse_RollsNothing()
    {
        // Nothing is eligible: the harness only rolls the happenings EligibleOnly allows.
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly();

        h.Run();

        h.Events().ShouldBeEmpty();
        h.State.Pending.ShouldBeNull();
        h.Queries.PendingHappening().ShouldBeNull();
    }

    [Fact]
    public void Run_OnlyWinterTaleEligible_RollsItWhenItsTriggerHolds()
    {
        // The harness stands in for the trigger: allowing winter_tale makes it roll regardless of season.
        // The real season evaluation is covered by the full-simulation integration tests.
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_winter_tale");

        h.Run();

        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningPending(0, h.Happening("test_winter_tale")));
    }

    [Fact]
    public void Run_PendingAlready_WaitsInsteadOfRolling()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast", "test_storm");
        h.Run();
        h.Events();

        h.Run();

        h.Events().ShouldBeEmpty();
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }

    [Fact]
    public void Run_TwoEligible_RaisesOnlyTheFirstInIdOrder()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast", "test_storm");

        h.Run();

        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningPending(0, h.Happening("test_feast")));
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }

    [Fact]
    public void Run_OnCooldown_SkipsTheHappening()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();
        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 0));
        h.Events();

        // Day 1: the 30-day cooldown still covers test_feast, so nothing rolls.
        h.SetTick(120).Run();

        h.Events().ShouldBeEmpty();
        h.State.Pending.ShouldBeNull();
        h.Queries.IsOnCooldown(h.Happening("test_feast")).ShouldBeTrue();
        h.Queries.IsOnCooldown(h.Happening("test_storm")).ShouldBeFalse();
    }

    [Fact]
    public void Queries_UnknownHappening_IsNeverPendingOrCoolingDown()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        var unknown = HappeningId.From(checked((ushort)h.Content.Happenings.Count));
        h.Run();

        h.Queries.IsOnCooldown(unknown).ShouldBeFalse();
        h.Queries.PendingHappening().ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }
}
