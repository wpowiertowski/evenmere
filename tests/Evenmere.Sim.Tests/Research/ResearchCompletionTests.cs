using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Research;

public sealed class ResearchCompletionTests
{
    [Fact]
    public void Completion_AppliesEffectsOnce()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.CreditKnowledge(60);
        h.Run();
        h.Queries.IsDone(theory).ShouldBeTrue();

        // Later days must not apply the tech's effects again.
        h.Run();
        h.Run();

        FakeProgressionService service = h.Sim.Services.ProgressionService;
        service.AppliedSources.ShouldBe([EffectSource.ForTech(theory)]);
        service.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public void Completion_ReportsTheEffectsOfTheTech()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.CreditKnowledge(60);
        h.Run();

        FakeProgressionService service = h.Sim.Services.ProgressionService;
        service.Calls.ShouldHaveSingleItem().Effects.ShouldBe(h.Content.Get(theory).Effects);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        static ulong RunWorld()
        {
            var h = ResearchHarness.Create();
            h.Send(new SetResearchTarget(h.Tech("test_theory")));
            h.Send(new SetResearchQueue([h.Tech("test_practice")]));
            h.Add("founders_hall", 0, 0);
            for (int day = 0; day < 12; day++)
            {
                h.CreditKnowledge(6);
                h.Run();
            }

            return h.Hash();
        }

        RunWorld().ShouldBe(RunWorld());
    }

    [Fact]
    public void State_SaveRoundTrip_PreservesProgress()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice]));
        h.SetInspiration(theory, inspired: true);
        h.CreditKnowledge(30);
        h.Run();

        ulong before = h.Hash();
        var reloaded = ResearchHarness.Reload(h);

        reloaded.Hash().ShouldBe(before);
        reloaded.Queries.Target.ShouldBe(theory);
        reloaded.Queries.Queue.ShouldBe([practice]);
        reloaded.Queries.Progress(theory).ShouldBe(h.Queries.Progress(theory));
        reloaded.Queries.IsInspired(theory).ShouldBeTrue();
        reloaded.Queries.PointsPerDay.ShouldBe(h.Queries.PointsPerDay);
        reloaded.State.Banked.ShouldBe(h.State.Banked);
    }
}
