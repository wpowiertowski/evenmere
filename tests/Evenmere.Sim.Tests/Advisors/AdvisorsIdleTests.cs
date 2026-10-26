using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>Idle advisors only speak when there is a choice: points accrue, nothing is targeted, and something can be chosen.</summary>
public sealed class AdvisorsIdleTests
{
    [Fact]
    public void ResearchIdle_AllTechsDone_StaysSilentForASeason()
    {
        var h = AdvisorsHarness.Create();
        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(4);
        h.Sim.Queries.Research.SetTech(h.Content.IdOf<TechId>("net_weaving"), Amount.FromUnits(50), done: true);
        h.Sim.Queries.Research.SetTech(h.Content.IdOf<TechId>("town_charter"), Amount.FromUnits(160), done: true);

        h.Run();
        h.AdvanceDays(10);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void ResearchIdle_AvailabilityEnds_ClearsLiveNote()
    {
        var h = AdvisorsHarness.Create();
        TechId net = h.Content.IdOf<TechId>("net_weaving");
        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(4);
        h.Sim.Queries.Research.SetTech(net, Amount.FromUnits(50), available: true);

        h.Run();
        AdviceView raised = h.Active().ShouldHaveSingleItem();
        raised.Kind.ShouldBe(AdviceKind.ResearchIdle);
        h.Events();

        // The last available tech finishes: the note clears the way other notes clear when their condition ends.
        h.Sim.Queries.Research.SetTech(net, Amount.FromUnits(50), done: true);
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, raised.Id, AdviceKind.ResearchIdle)]);
    }

    [Fact]
    public void ResearchIdle_DismissedThenAllDone_NeverReturns()
    {
        var h = AdvisorsHarness.Create();
        TechId net = h.Content.IdOf<TechId>("net_weaving");
        TechId charter = h.Content.IdOf<TechId>("town_charter");
        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(4);
        h.Sim.Queries.Research.SetTech(net, Amount.FromUnits(50), available: true);

        h.Run();
        int id = h.Active().ShouldHaveSingleItem().Id;
        h.Events();

        h.Send(new DismissAdvice(id));
        h.Events();

        // Everything finishes while the dismissal suppresses the note: after the season ends, nothing comes back.
        h.Sim.Queries.Research.SetTech(net, Amount.FromUnits(50), done: true);
        h.Sim.Queries.Research.SetTech(charter, Amount.FromUnits(160), done: true);
        h.AdvanceDays(11);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void CivicIdle_AllCivicsDone_StaysSilentForASeason()
    {
        var h = AdvisorsHarness.Create();
        h.Sim.Queries.Culture.PointsPerDay = Amount.FromUnits(2);
        h.Sim.Queries.Culture.SetCivic(h.Content.IdOf<CivicId>("village_moot"), Amount.FromUnits(140), done: true);

        h.Run();
        h.AdvanceDays(10);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void CivicIdle_AvailabilityEnds_ClearsLiveNote()
    {
        var h = AdvisorsHarness.Create();
        CivicId moot = h.Content.IdOf<CivicId>("village_moot");
        h.Sim.Queries.Culture.PointsPerDay = Amount.FromUnits(2);
        h.Sim.Queries.Culture.SetCivic(moot, Amount.FromUnits(140), available: true);

        h.Run();
        AdviceView raised = h.Active().ShouldHaveSingleItem();
        raised.Kind.ShouldBe(AdviceKind.CivicIdle);
        h.Events();

        // The last available civic is adopted: the note clears the way other notes clear when their condition ends.
        h.Sim.Queries.Culture.SetCivic(moot, Amount.FromUnits(140), done: true);
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, raised.Id, AdviceKind.CivicIdle)]);
    }

    [Fact]
    public void SliceEndState_AllTechsAndCivicDone_NoIdleNotesOverTwoSeasons()
    {
        ContentDb content = ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow();
        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = 1 });

        // Complete the whole tree in dependency order, so the idle notes have nothing to fire on.
        CompleteAllTechs(sim, content);
        CompleteAllCivics(sim, content);
        sim.DrainEvents();

        // Points keep accruing with nothing left to choose, for two full seasons.
        for (int day = 0; day < 2 * GameTime.DaysPerSeason; day++)
        {
            sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(5));
            sim.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(5));
            for (int i = 0; i < GameTime.TicksPerDay; i++)
            {
                sim.Tick();
            }
        }

        // The points are still accruing, so the old rule would have fired every day; with the tree fully
        // developed, neither idle note appears.
        (sim.Query.Research.PointsPerDay > Amount.Zero).ShouldBeTrue();
        (sim.Query.Culture.PointsPerDay > Amount.Zero).ShouldBeTrue();
        sim.Query.Advisors.Active().ToArray().ShouldNotContain(static a => a.Kind == AdviceKind.ResearchIdle || a.Kind == AdviceKind.CivicIdle);
    }

    private static void CompleteAllTechs(Simulation sim, ContentDb content)
    {
        var done = new HashSet<TechId>();
        while (done.Count < content.Techs.Count)
        {
            int before = done.Count;
            foreach (TechDef def in content.Techs.Items)
            {
                if (done.Contains(def.Id))
                {
                    continue;
                }

                bool ready = true;
                foreach (TechId required in def.Requires)
                {
                    if (!done.Contains(required))
                    {
                        ready = false;
                        break;
                    }
                }

                if (ready)
                {
                    CompleteTech(sim, def.Id);
                    done.Add(def.Id);
                }
            }

            Assert.True(done.Count > before, "Tech tree has a cycle or an unknown requirement.");
        }
    }

    private static void CompleteAllCivics(Simulation sim, ContentDb content)
    {
        var done = new HashSet<CivicId>();
        while (done.Count < content.Civics.Count)
        {
            int before = done.Count;
            foreach (CivicDef def in content.Civics.Items)
            {
                if (done.Contains(def.Id))
                {
                    continue;
                }

                bool ready = true;
                foreach (CivicId required in def.Requires)
                {
                    if (!done.Contains(required))
                    {
                        ready = false;
                        break;
                    }
                }

                if (ready)
                {
                    CompleteCivic(sim, def.Id);
                    done.Add(def.Id);
                }
            }

            Assert.True(done.Count > before, "Civic tree has a cycle or an unknown requirement.");
        }
    }

    private static void CompleteTech(Simulation sim, TechId tech)
    {
        sim.Enqueue(new SetResearchTarget(tech));
        sim.Tick();
        sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(500));
        for (int i = 0; i < GameTime.TicksPerDay; i++)
        {
            sim.Tick();
        }

        sim.Query.Research.IsDone(tech).ShouldBeTrue();
    }

    private static void CompleteCivic(Simulation sim, CivicId civic)
    {
        sim.Enqueue(new SetCivicTarget(civic));
        sim.Tick();
        sim.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(500));
        for (int i = 0; i < GameTime.TicksPerDay; i++)
        {
            sim.Tick();
        }

        sim.Query.Culture.IsDone(civic).ShouldBeTrue();
    }
}
