using Evenmere.Content.Effects;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>The pending timeout and the duration-bound modifier expiry: both fire on their exact day.</summary>
public sealed class HappeningTimeoutTests
{
    [Fact]
    public void Pending_BeforeDayTen_StaysPending()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();
        h.Events();

        // Day 9: still waiting, nothing resolved.
        h.SetTick(9 * 120).Run();

        h.Events().ShouldBeEmpty();
        h.State.Pending.ShouldBe(new PendingHappening(h.Happening("test_feast"), 0));
    }

    [Fact]
    public void Pending_OnDayTen_AppliesTheDefaultOption()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();
        h.Events();

        // Day 10: the default option (grant 10 fish) applies exactly on the tenth day. The grant reaching
        // the pool is covered by the full-simulation integration tests; here the recorded service call proves
        // the option's effects applied.
        h.SetTick(10 * 120).Run();

        h.Events().ShouldHaveSingleItem().ShouldBe(new HappeningResolved(10 * 120, h.Happening("test_feast"), 0));
        h.State.Pending.ShouldBeNull();
        var source = EffectSource.ForHappening(h.Happening("test_feast"));
        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Source.ShouldBe(source),
            call => call.Effects.ShouldHaveSingleItem().ShouldBeOfType<GrantGoodsEffect>());
        h.State.CooldownUntil[h.Happening("test_feast").Value].ShouldBe(10 * 120 + 30 * 120L);
    }

    [Fact]
    public void TempModifiers_OnExpiryDay_AreRemovedThroughRemoveSource()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_storm");
        h.Run();
        h.Send(new ChooseHappeningOption(h.Happening("test_storm"), 0));
        h.Events();
        var source = EffectSource.ForHappening(h.Happening("test_storm"));

        // Choosing already removes the source once, so the resolve can re-apply a repeat occurrence.
        h.Sim.Services.ProgressionService.RemovedSources.Count.ShouldBe(1);

        // Day 4: the 5-day modifiers still apply, so nothing more is removed.
        h.SetTick(4 * 120).Run();
        h.Sim.Services.ProgressionService.RemovedSources.Count.ShouldBe(1);
        h.State.TempModifiers.ShouldHaveSingleItem();

        // Day 5: the modifiers expire exactly on the fifth day.
        h.SetTick(5 * 120).Run();
        h.Sim.Services.ProgressionService.RemovedSources.Count.ShouldBe(2);
        h.Sim.Services.ProgressionService.RemovedSources[1].ShouldBe(source);
        h.State.TempModifiers.ShouldBeEmpty();
    }

    [Fact]
    public void TempModifiers_PermanentOption_LeavesNoExpiryRecord()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();

        // Option 1 carries a 10-day modifier, option 0 only grants: the grant-only resolve records nothing.
        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 0));

        h.State.TempModifiers.ShouldBeEmpty();
        h.Sim.Services.ProgressionService.RemovedSources.ShouldContain(EffectSource.ForHappening(h.Happening("test_feast")));
    }
}
