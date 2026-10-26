using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Events;
using Evenmere.Game.Tests;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;

namespace Evenmere.Game.Tests;

/// <summary>The festival panel rules: the block order follows the sim's HoldFestival checks, and every blocked row names its calm reason.</summary>
public sealed class FestivalPanelModelTests
{
    private static UiStrings Strings => new(TestLocales.Table());

    [Fact]
    public void Rows_EmptyWhenNoFestivals()
    {
        var model = new FestivalPanelModel(new FakeFestivals([]), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.Rows.ShouldBeEmpty();
        model.RunningLine.ShouldBeNull();
    }

    [Fact]
    public void HoldCommand_ReadyRow_EnqueuesHold()
    {
        var ground = new EntityId(7);
        var model = new FestivalPanelModel(new FakeFestivals([Ready()]), Strings) { Ground = ground };

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.CanHold.ShouldBeTrue();
        row.StatusText.ShouldBe(string.Empty);
        model.HoldCommand(row.Id).ShouldBe(new HoldFestival(ground, row.Id));
    }

    [Fact]
    public void HoldCommand_NoGround_IsNull()
    {
        var model = new FestivalPanelModel(new FakeFestivals([Ready()]), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.HoldCommand(model.Rows.ShouldHaveSingleItem().Id).ShouldBeNull();
    }

    [Fact]
    public void Block_LockedBeatsEverything()
    {
        FestivalFacts facts = Ready() with { Unlocked = false, HeldThisYear = true };
        var model = new FestivalPanelModel(
            new FakeFestivals([facts], IsRunning: true, season: Season.Winter),
            Strings);

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.Block.ShouldBe(FestivalBlock.Locked);
        row.CanHold.ShouldBeFalse();
        row.StatusText.ShouldBe(Strings.Get("ui.events.festival.block.locked"));
        model.HoldCommand(row.Id).ShouldBeNull();
    }

    [Fact]
    public void Block_RunningBeatsHeldSeasonAndGoods()
    {
        FestivalFacts facts = Ready() with
        {
            HeldThisYear = true,
            Seasons = [Season.Winter],
            Cost = [new FestivalCostFact("Bread", Amount.FromUnits(12), Amount.FromUnits(2))],
        };
        var model = new FestivalPanelModel(
            new FakeFestivals([facts], IsRunning: true, season: Season.Summer),
            Strings);

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.Block.ShouldBe(FestivalBlock.AlreadyRunning);
        row.StatusText.ShouldBe(Strings.Get("ui.events.festival.block.running"));
    }

    [Fact]
    public void Block_HeldBeatsSeasonAndGoods()
    {
        FestivalFacts facts = Ready() with
        {
            HeldThisYear = true,
            Seasons = [Season.Winter],
            Cost = [new FestivalCostFact("Bread", Amount.FromUnits(12), Amount.FromUnits(2))],
        };
        var model = new FestivalPanelModel(new FakeFestivals([facts], season: Season.Summer), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.Rows.ShouldHaveSingleItem().Block.ShouldBe(FestivalBlock.HeldThisYear);
        model.Rows.ShouldHaveSingleItem().StatusText.ShouldBe(Strings.Get("ui.events.festival.block.held"));
    }

    [Fact]
    public void Block_WrongSeasonNamesTheSeason()
    {
        FestivalFacts facts = Ready() with { Seasons = [Season.Winter] };
        var model = new FestivalPanelModel(new FakeFestivals([facts], season: Season.Summer), Strings);

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.Block.ShouldBe(FestivalBlock.WrongSeason);
        row.StatusText.ShouldBe(Strings.Format("ui.events.festival.block.season", ("season", "Winter")));
    }

    [Fact]
    public void Block_GoodsShortNamesTheShortfall()
    {
        FestivalFacts facts = Ready() with
        {
            Cost = [new FestivalCostFact("Bread", Amount.FromUnits(12), Amount.FromUnits(2))],
        };
        var model = new FestivalPanelModel(new FakeFestivals([facts]), Strings);

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.Block.ShouldBe(FestivalBlock.GoodsShort);
        row.StatusText.ShouldBe(Strings.Format(
            "ui.events.festival.block.goods",
            ("amount", "10"),
            ("good", "Bread")));
    }

    [Fact]
    public void Row_NamesDaysCultureAndBonus()
    {
        FestivalFacts facts = Ready() with
        {
            Days = 1,
            CulturePerDay = Amount.FromUnits(12),
            Bonus = new Evenmere.Content.Definitions.SeasonBonus(Season.Autumn, 50),
            Cost = [
                new FestivalCostFact("Bread", Amount.FromUnits(10), Amount.FromUnits(10)),
                new FestivalCostFact("Cider", Amount.FromUnits(4), Amount.FromUnits(9)),
            ],
        };
        var model = new FestivalPanelModel(new FakeFestivals([facts]), Strings);

        model.Advance(0.0).ShouldBeTrue();

        FestivalRow row = model.Rows.ShouldHaveSingleItem();
        row.DaysText.ShouldBe(Strings.Get("ui.events.festival.days.one"));
        row.CultureText.ShouldContain("+12");
        row.CultureText.ShouldContain("50");
        row.CultureText.ShouldContain("Autumn");
        row.CostText.ShouldContain("10");
        row.CostText.ShouldContain("Bread");
    }

    [Fact]
    public void RunningLine_NamesFestivalAndDaysLeft()
    {
        var model = new FestivalPanelModel(
            new FakeFestivals([Ready()], IsRunning: true, runningName: "Harvest Fair", runningDaysLeft: 2),
            Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.RunningLine.ShouldBe(Strings.Format(
            "ui.events.festival.running",
            ("name", "Harvest Fair"),
            ("left", Strings.Format("ui.events.festival.days", ("count", "2")))));
    }

    private static FestivalFacts Ready() => new(
        FestivalId.From(0),
        "Harvest Fair",
        [],
        [],
        3,
        Amount.FromUnits(8),
        null,
        "Music and shared tables.",
        Unlocked: true,
        HeldThisYear: false);

    private sealed class FakeFestivals(IReadOnlyList<FestivalFacts> festivals) : IFestivalData
    {
        private readonly bool _running;
        private readonly Season _season;
        private readonly string? _runningName;
        private readonly int _runningDaysLeft;

        public FakeFestivals(
            IReadOnlyList<FestivalFacts> festivals,
            bool IsRunning = false,
            Season season = Season.Autumn,
            string? runningName = null,
            int runningDaysLeft = 0)
            : this(festivals)
        {
            _running = IsRunning;
            _season = season;
            _runningName = runningName;
            _runningDaysLeft = runningDaysLeft;
        }

        public bool IsRunning => _running;

        public Season SeasonNow => _season;

        public string? RunningName => _runningName;

        public int RunningDaysLeft => _runningDaysLeft;

        public IReadOnlyList<FestivalFacts> Festivals => festivals;
    }
}
