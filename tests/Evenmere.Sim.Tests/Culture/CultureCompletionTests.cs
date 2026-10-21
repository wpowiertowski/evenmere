using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Culture;

public sealed class CultureCompletionTests
{
    [Fact]
    public void Completion_AppliesEffectsOnce()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.CreditCulture(40);
        h.Run();
        h.Queries.IsDone(custom).ShouldBeTrue();

        h.Run();
        h.Run();

        FakeProgressionService service = h.Sim.Services.ProgressionService;
        service.AppliedSources.ShouldBe([EffectSource.ForCivic(custom)]);
        service.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        static ulong RunWorld()
        {
            var h = CultureHarness.Create();
            h.Send(new SetCivicTarget(h.Civic("test_custom")));
            h.Send(new SetCivicQueue([h.Civic("test_custom2")]));
            h.Add("founders_hall", 0, 0);
            for (int day = 0; day < 12; day++)
            {
                h.CreditCulture(4);
                h.Run();
            }

            return h.Hash();
        }

        RunWorld().ShouldBe(RunWorld());
    }

    [Fact]
    public void State_SaveRoundTrip_PreservesProgress()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        CivicId custom2 = h.Civic("test_custom2");
        h.Send(new SetCivicTarget(custom));
        h.Send(new SetCivicQueue([custom2]));
        h.SetInspiration(custom, inspired: true);
        h.CreditCulture(20);
        h.Run();

        ulong before = h.Hash();
        var reloaded = CultureHarness.Reload(h);

        reloaded.Hash().ShouldBe(before);
        reloaded.Queries.Target.ShouldBe(custom);
        reloaded.Queries.Queue.ShouldBe([custom2]);
        reloaded.Queries.Progress(custom).ShouldBe(h.Queries.Progress(custom));
        reloaded.Queries.IsInspired(custom).ShouldBeTrue();
        reloaded.Queries.PointsPerDay.ShouldBe(h.Queries.PointsPerDay);
        reloaded.State.Banked.ShouldBe(h.State.Banked);
    }
}
