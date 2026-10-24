using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Hud;

namespace Evenmere.Game.Tests;

/// <summary>M1-31 and M1-37: the HUD shows each unlocked tier's workforce as "workers free / jobs", with a calm
/// warning tint only for a real shortage after stepping down. Covered jobs name the fill from above, and tiers
/// with workers in lower-tier jobs say so.</summary>
public sealed class HudWorkforceTests
{
    [Fact]
    public void FreeWorkers_ShowFreeLineWithoutWarning()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 24, Supply = 30, Demand = 24 });
        HudStat stat = model.Residents[0];
        stat.Label.ShouldBe("Settlers");
        stat.Value.ShouldBe("24 · 6 free / 24 jobs");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("30 Settlers can work; buildings ask for 24");
        stat.Tooltip.ShouldContain("Houses of the Settlers wait to advance while their workers are needed, counting jobs at construction sites and a reserve.");
        stat.Tooltip.ShouldNotContain("Build more houses");
    }

    [Fact]
    public void ShortWorkers_ShowShortLineWithWarning()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 12, Supply = 12, Demand = 18 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("12 · short 6 / 18 jobs");
        stat.IsWarning.ShouldBeTrue();
        stat.Tooltip.ShouldContain("12 Settlers can work; buildings ask for 18");
        stat.Tooltip.ShouldNotContain("filled by idle workers from higher tiers");
        stat.Tooltip.ShouldContain("Build more houses for Settlers to fill the jobs.");
    }

    [Fact]
    public void EvenWorkers_ShowZeroFreeWithoutWarning()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 18, Supply = 18, Demand = 18 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("18 · 0 free / 18 jobs");
        stat.IsWarning.ShouldBeFalse();
    }

    [Fact]
    public void NoJobsAndNoResidents_HideWorkforceLine()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 0, Supply = 0, Demand = 0 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("0");
        stat.Value.ShouldNotContain("jobs");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("Residents (Settlers): 0");
    }

    [Fact]
    public void ResidentsWithoutJobs_ShowFreeWithoutJobsPart()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 24, Supply = 24, Demand = 0 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("24 · 24 free");
        stat.Value.ShouldNotContain("jobs");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldNotContain("wait to advance");
    }

    [Fact]
    public void CoveredJobs_ShowCoveredLineWithoutWarning()
    {
        // The M1-36 playtest: 45 Settlers against 51 jobs, with 6 filled from above.
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 45, Supply = 45, Demand = 51, Covered = 6 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("45 · 51 jobs, 6 filled from above");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("45 Settlers can work; buildings ask for 51.");
        stat.Tooltip.ShouldContain("6 of these jobs are filled by idle workers from higher tiers.");
        stat.Tooltip.ShouldContain("wait to advance while their workers are needed");
        stat.Tooltip.ShouldContain("Build more houses for Settlers so the higher-tier workers can go back to their own work.");
        stat.Tooltip.ShouldNotContain("to fill the jobs.");
    }

    [Fact]
    public void WorkingBelow_ShowBelowSuffixWithoutWarning()
    {
        // The higher tier of the same playtest: 303 Villagers, 23 of their own jobs, 6 in lower-tier jobs.
        HudViewModel model = ModelForBoth(
            settlers: new TierCase(45, 45, 51),
            villagers: new TierCase(303, 303, 23) with { Below = 6 });
        HudStat stat = model.Residents[1];
        stat.Label.ShouldBe("Villagers");
        stat.Value.ShouldBe("303 · 274 free / 23 jobs · 6 in lower-tier jobs");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("6 Villagers are working in lower-tier jobs. Their houses can't advance until there are enough workers below them.");
    }

    [Fact]
    public void PartialCover_RealShortageWarns()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 12, Supply = 12, Demand = 20, Covered = 2 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("12 · short 6 / 20 jobs");
        stat.IsWarning.ShouldBeTrue();
        stat.Tooltip.ShouldContain("2 of these jobs are filled by idle workers from higher tiers.");
        stat.Tooltip.ShouldContain("Build more houses for Settlers to fill the jobs.");
        stat.Tooltip.ShouldNotContain("go back to their own work");
    }

    [Fact]
    public void SingleWorker_SingularForms()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 1, Supply = 1, Demand = 2 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("1 · short 1 / 2 jobs");
        stat.IsWarning.ShouldBeTrue();
        stat.Tooltip.ShouldContain("1 worker of the Settlers can work; buildings ask for 2.");
        stat.Tooltip.ShouldContain("Build more houses for Settlers to fill the jobs.");
    }

    [Fact]
    public void SingleCoveredJob_SingularCoveredSentence()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 5, Supply = 5, Demand = 6, Covered = 1 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("5 · 6 jobs, 1 filled from above");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("1 of these jobs is filled by an idle worker from a higher tier.");
    }

    [Fact]
    public void SingleWorkerBelow_SingularBelowSentence()
    {
        HudViewModel model = ModelForBoth(
            settlers: new TierCase(45, 45, 51) with { Covered = 6 },
            villagers: new TierCase(10, 10, 2) with { Below = 1 });
        HudStat stat = model.Residents[1];
        stat.Value.ShouldBe("10 · 7 free / 2 jobs · 1 in lower-tier jobs");
        stat.IsWarning.ShouldBeFalse();
        stat.Tooltip.ShouldContain("1 worker of the Villagers is working in a lower-tier job. Their house can't advance until there are enough workers below them.");
    }

    [Fact]
    public void IdleWithBelow_ShowFreeAndBelow()
    {
        HudViewModel model = ModelForBoth(
            settlers: new TierCase(45, 45, 51) with { Covered = 6 },
            villagers: new TierCase(303, 303, 0) with { Below = 6 });
        HudStat stat = model.Residents[1];
        stat.Value.ShouldBe("303 · 297 free · 6 in lower-tier jobs");
        stat.IsWarning.ShouldBeFalse();
    }

    [Fact]
    public void TierNames_ComeFromLocale()
    {
        var strings = new UiStrings(TestLocales.Table());
        HudViewModel model = ModelFor(settlers => settlers, strings);
        model.Residents[0].Label.ShouldBe(strings.TierName(TestContent.Db.NameOf(TestContent.Db.TiersByOrder[0])));
        model.Residents[1].Label.ShouldBe(strings.TierName(TestContent.Db.NameOf(TestContent.Db.TiersByOrder[1])));
    }

    private static HudViewModel ModelFor(Func<TierCase, TierCase> adjust, UiStrings? strings = null)
    {
        ContentDb content = TestContent.Db;
        TierCase applied = adjust(new TierCase(0, 0, 0));
        return ModelForBoth(applied, new TierCase(0, 0, 0), strings);
    }

    private static HudViewModel ModelForBoth(TierCase settlers, TierCase villagers, UiStrings? strings = null)
    {
        ContentDb content = TestContent.Db;
        TierId settlersId = content.TiersByOrder[0];
        TierId villagersId = content.TiersByOrder[1];

        var source = new FakeHudSource();
        source.Unlocked.Add(settlersId);
        source.Unlocked.Add(villagersId);
        source.CaseByTier[settlersId] = settlers;
        source.CaseByTier[villagersId] = villagers;

        var model = new HudViewModel(source, content, strings ?? new UiStrings(TestLocales.Table()));
        model.Advance(10.0);
        return model;
    }

    private sealed record TierCase(long Residents, int Supply, int Demand, int Below = 0, int Covered = 0);

    private sealed class FakeHudSource : IHudSource
    {
        public readonly Dictionary<TierId, TierCase> CaseByTier = new();
        public readonly HashSet<TierId> Unlocked = [];

        public bool IsRunning => true;

        public GameClock Clock => new(0);

        public SimSpeed Speed => SimSpeed.X1;

        public Amount Stock(GoodId good) => Amount.Zero;

        public long? Residents(TierId tier) => CaseByTier.TryGetValue(tier, out TierCase? kase) ? kase.Residents : 0;

        public bool IsTierUnlocked(TierId tier) => Unlocked.Contains(tier);

        public int WorkforceSupply(TierId tier) => CaseByTier.TryGetValue(tier, out TierCase? kase) ? kase.Supply : 0;

        public int WorkforceDemand(TierId tier) => CaseByTier.TryGetValue(tier, out TierCase? kase) ? kase.Demand : 0;

        public int WorkingBelowTier(TierId tier) => CaseByTier.TryGetValue(tier, out TierCase? kase) ? kase.Below : 0;

        public int FilledFromAbove(TierId tier) => CaseByTier.TryGetValue(tier, out TierCase? kase) ? kase.Covered : 0;

        public Amount? KnowledgePerDay() => Amount.Zero;

        public Amount? CulturePerDay() => Amount.Zero;
    }
}
