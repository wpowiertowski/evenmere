using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>Determinism across a save round trip while a happening is pending: the reload continues identically.</summary>
public sealed class HappeningDeterminismTests
{
    [Fact]
    public void Pending_RoundTrip_ContinuesIdentically()
    {
        HappeningsHarness h = HappeningsHarness.Create().EligibleOnly("test_feast");
        h.Run();
        h.Events();
        ulong before = h.Hash();

        var reloaded = HappeningsHarness.Reload(h);

        reloaded.Hash().ShouldBe(before);
        reloaded.State.Pending.ShouldBe(h.State.Pending);

        h.Send(new ChooseHappeningOption(h.Happening("test_feast"), 1));
        reloaded.Send(new ChooseHappeningOption(reloaded.Happening("test_feast"), 1));

        reloaded.Events().ShouldBe(h.Events());
        reloaded.Hash().ShouldBe(h.Hash());
    }
}
