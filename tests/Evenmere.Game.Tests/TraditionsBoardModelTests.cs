using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Traditions;

namespace Evenmere.Game.Tests;

/// <summary>
/// The pure rules behind the traditions board: slot grouping, the drawer, the bonus probes and the after-swap
/// summary in <see cref="TraditionsBoardModel"/>.
/// </summary>
public sealed class TraditionsBoardModelTests
{
    [Fact]
    public void GroupSlots_GroupsByTypeInOrder()
    {
        var strings = new UiStrings(TestLocales.Table());
        var slots = new List<TraditionSlotView>
        {
            new(TraditionSlotType.Hearth, 0, TraditionId.From(0)),
            new(TraditionSlotType.Hearth, 1, null),
            new(TraditionSlotType.Craft, 0, null),
        };

        IReadOnlyList<SlotGroup> groups = TraditionsBoardModel.GroupSlots(strings, slots);

        groups.Count.ShouldBe(2);
        groups[0].Slot.ShouldBe(TraditionSlotType.Hearth);
        groups[0].Title.ShouldBe(strings.Get("ui.traditions.slot.hearth"));
        groups[0].Entries.Count.ShouldBe(2);
        groups[0].Entries[0].Tradition.ShouldBe(TraditionId.From(0));
        groups[0].Entries[1].Tradition.ShouldBeNull();
        groups[1].Slot.ShouldBe(TraditionSlotType.Craft);
        groups[1].Entries.Count.ShouldBe(1);
    }

    [Fact]
    public void BuildDrawer_EmptyWhenNothingUnlocked()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());

        IReadOnlyList<TraditionCardData> drawer = TraditionsBoardModel.BuildDrawer(
            content,
            strings,
            [],
            static _ => null,
            static _ => null);

        drawer.ShouldBeEmpty();
    }

    [Fact]
    public void ProbesFor_IslandTarget()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());

        IReadOnlyList<BonusProbe> probes = TraditionsBoardModel.ProbesFor(
            content,
            new ContentConditionNames(content, strings),
            strings,
            [new ModifierEffect(Stat.Productivity, new IslandTarget(), 20, null)]);

        probes.Count.ShouldBe(1);
        probes[0].Target.ShouldBe(strings.Get("ui.traditions.target.island"));
        probes[0].Season.ShouldBeNull();
    }

    [Fact]
    public void ProbesFor_NamesTagTargetAndSeason()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        var names = new ContentConditionNames(content, strings);

        IReadOnlyList<BonusProbe> probes = TraditionsBoardModel.ProbesFor(
            content,
            names,
            strings,
            [new ModifierEffect(
                Stat.Productivity,
                new BuildingsTarget(new BuildingTagSelector("farm")),
                20,
                new SeasonCondition(Season.Winter))]);

        probes.Count.ShouldBe(1);
        probes[0].Stat.ShouldBe(Stat.Productivity);
        probes[0].Target.ShouldBe(names.Tag("farm"));
        probes[0].Season.ShouldBe(names.Season(Season.Winter));
        probes[0].Context.BuildingType.ShouldNotBeNull();
    }

    [Fact]
    public void ProbesFor_UnknownTagFallsBackToIsland()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        var names = new ContentConditionNames(content, strings);

        IReadOnlyList<BonusProbe> probes = TraditionsBoardModel.ProbesFor(
            content,
            names,
            strings,
            [new ModifierEffect(
                Stat.Productivity,
                new BuildingsTarget(new BuildingTagSelector("no-such-tag")),
                20,
                null)]);

        probes.Count.ShouldBe(1);
        probes[0].Context.ShouldBe(ModifierContext.Island);
        probes[0].Season.ShouldBeNull();
    }

    [Fact]
    public void SummarizeChanges_NamesTargetStatDeltaAndSeason()
    {
        var strings = new UiStrings(TestLocales.Table());
        var probe = new BonusProbe(Stat.Productivity, ModifierContext.Island, "farms", "Winter");

        IReadOnlyList<string> lines = TraditionsBoardModel.SummarizeChanges(
            strings,
            [new ProbedBonus(probe, 10)],
            [new ProbedBonus(probe, 30)]);

        lines.ShouldBe(["Farms productivity +20% (Winter)"]);
    }

    [Fact]
    public void SummarizeChanges_OmitsSeasonWhenAlwaysOn()
    {
        var strings = new UiStrings(TestLocales.Table());
        var probe = new BonusProbe(Stat.Productivity, ModifierContext.Island, "Island-wide", null);

        IReadOnlyList<string> lines = TraditionsBoardModel.SummarizeChanges(
            strings,
            [new ProbedBonus(probe, 0)],
            [new ProbedBonus(probe, 10)]);

        lines.ShouldBe(["Island-wide productivity +10%"]);
    }

    [Fact]
    public void SummarizeChanges_SilentWhenNothingMoved()
    {
        var strings = new UiStrings(TestLocales.Table());
        var probe = new BonusProbe(Stat.Productivity, ModifierContext.Island, "farms", "Winter");

        IReadOnlyList<string> lines = TraditionsBoardModel.SummarizeChanges(
            strings,
            [new ProbedBonus(probe, 20)],
            [new ProbedBonus(probe, 20)]);

        lines.ShouldBeEmpty();
    }

    [Fact]
    public void SnapshotBonuses_MeasuresEachProbe()
    {
        var strings = new UiStrings(TestLocales.Table());
        var progression = new StubProgression();
        var probe = new BonusProbe(Stat.Productivity, ModifierContext.Island, "farms", "Winter");

        IReadOnlyList<ProbedBonus> measured = TraditionsBoardModel.SnapshotBonuses(progression, [probe]);

        measured.Count.ShouldBe(1);
        measured[0].Probe.ShouldBe(probe);
        measured[0].Percent.ShouldBe(20);
    }

    private sealed class StubProgression : IProgressionQueries
    {
        public bool IsUnlocked(BuildingTypeId building) => true;
        public bool IsUnlocked(RecipeId recipe) => true;
        public bool IsUnlocked(TierId tier) => true;
        public bool HasAccess(TerrainAccess access) => true;
        public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null) => true;
        public EraId CurrentEra => throw new NotSupportedException();
        public int BonusPercent(Stat stat, in ModifierContext context) =>
            stat == Stat.Productivity ? 20 : 0;
        public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions) { }
        public long Counter(CounterKey key) => 0;
        public bool Evaluate(Condition condition) => false;
        public bool IsTraditionUnlocked(TraditionId tradition) => false;
        public bool IsFestivalUnlocked(FestivalId festival) => false;
        public int TraditionSlotCount(TraditionSlotType slot) => 0;
        public GatewayStatus GatewayStatus(EraId era) => new(false, false);
    }
}
