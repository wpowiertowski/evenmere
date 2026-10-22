using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Hud;

namespace Evenmere.Game.Tests;

/// <summary>M1-31: the HUD shows each unlocked tier's workforce as "workers free / jobs", with a calm warning tint only when demand outstrips supply.</summary>
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
        stat.Tooltip.ShouldContain("houses at the Settlers tier");
        stat.Tooltip.ShouldContain("wait to advance while their workers are needed");
    }

    [Fact]
    public void ShortWorkers_ShowShortLineWithWarning()
    {
        HudViewModel model = ModelFor(settlers => settlers with { Residents = 12, Supply = 12, Demand = 18 });
        HudStat stat = model.Residents[0];
        stat.Value.ShouldBe("12 · short 6 / 18 jobs");
        stat.IsWarning.ShouldBeTrue();
        stat.Tooltip.ShouldContain("12 Settlers can work; buildings ask for 18");
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
        TierId settlers = content.TiersByOrder[0];
        TierId villagers = content.TiersByOrder[1];
        TierCase applied = adjust(new TierCase(0, 0, 0));

        var source = new FakeHudSource();
        source.Unlocked.Add(settlers);
        source.Unlocked.Add(villagers);
        source.ResidentsByTier[settlers] = applied.Residents;
        source.SupplyByTier[settlers] = applied.Supply;
        source.DemandByTier[settlers] = applied.Demand;

        var model = new HudViewModel(source, content, strings ?? new UiStrings(TestLocales.Table()));
        model.Advance(10.0);
        return model;
    }

    private sealed record TierCase(long Residents, int Supply, int Demand);

    private sealed class FakeHudSource : IHudSource
    {
        public readonly Dictionary<TierId, long> ResidentsByTier = new();
        public readonly Dictionary<TierId, int> SupplyByTier = new();
        public readonly Dictionary<TierId, int> DemandByTier = new();
        public readonly HashSet<TierId> Unlocked = [];

        public bool IsRunning => true;

        public GameClock Clock => new(0);

        public SimSpeed Speed => SimSpeed.X1;

        public Amount Stock(GoodId good) => Amount.Zero;

        public long? Residents(TierId tier) => ResidentsByTier.TryGetValue(tier, out long count) ? count : 0;

        public bool IsTierUnlocked(TierId tier) => Unlocked.Contains(tier);

        public int WorkforceSupply(TierId tier) => SupplyByTier.TryGetValue(tier, out int count) ? count : 0;

        public int WorkforceDemand(TierId tier) => DemandByTier.TryGetValue(tier, out int count) ? count : 0;

        public Amount? KnowledgePerDay() => Amount.Zero;

        public Amount? CulturePerDay() => Amount.Zero;
    }
}
