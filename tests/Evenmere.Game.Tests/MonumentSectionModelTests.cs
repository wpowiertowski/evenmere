using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Monuments;
using Evenmere.Game.Tests;

namespace Evenmere.Game.Tests;

/// <summary>The inspector's monument section: the stage the site reports, the bill for each stage, and the effects when finished.</summary>
public sealed class MonumentSectionModelTests
{
    private static UiStrings Strings => new(TestLocales.Table());

    [Fact]
    public void NoTarget_ShowsEmpty()
    {
        var model = new MonumentSectionModel(new FakeMonuments(null), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.HasMonument.ShouldBeFalse();
        model.Stages.ShouldBeEmpty();
        model.EffectsLine.ShouldBe(Strings.Get("ui.events.monument.empty"));
    }

    [Fact]
    public void UnknownBuilding_ShowsEmpty()
    {
        var model = new MonumentSectionModel(new FakeMonuments(null), Strings) { Target = new EntityId(9) };

        model.Advance(0.0).ShouldBeTrue();

        model.HasMonument.ShouldBeFalse();
    }

    [Fact]
    public void ActiveSite_NamesStageProgressAndStall()
    {
        var facts = new MonumentFacts(
            new EntityId(3),
            "Standing Stones",
            StageIndex: 0,
            StageCount: 3,
            ProgressPerMille: 450,
            StallGoodName: "Stone",
            OutOfReach: false,
            EffectText: "+4 culture",
            IsComplete: false,
            [
                new MonumentStageFacts(0, MonumentStageState.Active, [new MonumentBillLine("Stone", Amount.FromUnits(20))]),
                new MonumentStageFacts(1, MonumentStageState.Later, [new MonumentBillLine("Stone", Amount.FromUnits(30))]),
                new MonumentStageFacts(2, MonumentStageState.Later, [new MonumentBillLine("Timber", Amount.FromUnits(10))]),
            ]);
        var model = new MonumentSectionModel(new FakeMonuments(facts), Strings) { Target = facts.Building };

        model.Advance(0.0).ShouldBeTrue();

        model.HasMonument.ShouldBeTrue();
        model.Title.ShouldBe("Standing Stones");
        model.StageLine.ShouldBe(Strings.Format(
            "ui.events.monument.stage", ("done", "1"), ("count", "3")));
        model.ProgressLine.ShouldBe(Strings.Format("ui.events.monument.progress", ("percent", "45")));
        model.StallLine.ShouldBe(Strings.Format("ui.events.monument.stall", ("good", "Stone")));
        model.EffectsLine.ShouldBe(Strings.Format("ui.events.monument.effects", ("effects", "+4 culture")));
        model.Stages.Count.ShouldBe(3);
        model.Stages[0].State.ShouldBe(MonumentStageState.Active);
        model.Stages[0].Headline.ShouldBe(Strings.Format("ui.events.monument.bill.stage", ("stage", "1")));
        model.Stages[0].BillText.ShouldBe(Strings.Format(
            "ui.events.monument.bill.line", ("amount", "20"), ("good", "Stone")));
        model.Stages[1].State.ShouldBe(MonumentStageState.Later);
        model.Stages[2].BillText.ShouldContain("Timber");
    }

    [Fact]
    public void MiddleStage_MarksEarlierStagesDone()
    {
        var facts = new MonumentFacts(
            new EntityId(3),
            "Standing Stones",
            StageIndex: 1,
            StageCount: 3,
            ProgressPerMille: 100,
            StallGoodName: null,
            OutOfReach: false,
            EffectText: "+4 culture",
            IsComplete: false,
            [
                new MonumentStageFacts(0, MonumentStageState.Done, [new MonumentBillLine("Stone", Amount.FromUnits(20))]),
                new MonumentStageFacts(1, MonumentStageState.Active, [new MonumentBillLine("Stone", Amount.FromUnits(30))]),
                new MonumentStageFacts(2, MonumentStageState.Later, [new MonumentBillLine("Timber", Amount.FromUnits(10))]),
            ]);
        var model = new MonumentSectionModel(new FakeMonuments(facts), Strings) { Target = facts.Building };

        model.Advance(0.0).ShouldBeTrue();

        model.StageLine.ShouldBe(Strings.Format(
            "ui.events.monument.stage", ("done", "2"), ("count", "3")));
        model.StallLine.ShouldBe(string.Empty);
        model.Stages[0].State.ShouldBe(MonumentStageState.Done);
        model.Stages[1].State.ShouldBe(MonumentStageState.Active);
    }

    [Fact]
    public void OutOfReach_NamesTheReach()
    {
        var facts = new MonumentFacts(
            new EntityId(3),
            "Standing Stones",
            StageIndex: 0,
            StageCount: 1,
            ProgressPerMille: 0,
            StallGoodName: null,
            OutOfReach: true,
            EffectText: "+4 culture",
            IsComplete: false,
            [
                new MonumentStageFacts(0, MonumentStageState.Active, [new MonumentBillLine("Stone", Amount.FromUnits(20))]),
            ]);
        var model = new MonumentSectionModel(new FakeMonuments(facts), Strings) { Target = facts.Building };

        model.Advance(0.0).ShouldBeTrue();

        model.StallLine.ShouldBe(Strings.Get("ui.events.monument.reach"));
    }

    [Fact]
    public void Finished_MarksAllStagesDoneWithEffects()
    {
        var facts = new MonumentFacts(
            new EntityId(3),
            "Standing Stones",
            StageIndex: 2,
            StageCount: 3,
            ProgressPerMille: 1000,
            StallGoodName: null,
            OutOfReach: false,
            EffectText: "+4 culture",
            IsComplete: true,
            [
                new MonumentStageFacts(0, MonumentStageState.Done, [new MonumentBillLine("Stone", Amount.FromUnits(20))]),
                new MonumentStageFacts(1, MonumentStageState.Done, [new MonumentBillLine("Stone", Amount.FromUnits(30))]),
                new MonumentStageFacts(2, MonumentStageState.Done, [new MonumentBillLine("Timber", Amount.FromUnits(10))]),
            ]);
        var model = new MonumentSectionModel(new FakeMonuments(facts), Strings) { Target = facts.Building };

        model.Advance(0.0).ShouldBeTrue();

        model.StageLine.ShouldBe(Strings.Format(
            "ui.events.monument.stage.finished", ("done", "3"), ("count", "3")));
        model.ProgressLine.ShouldBe(string.Empty);
        model.StallLine.ShouldBe(string.Empty);
        model.EffectsLine.ShouldBe(Strings.Format("ui.events.monument.complete", ("effects", "+4 culture")));
    }

    private sealed class FakeMonuments(MonumentFacts? facts) : IMonumentData
    {
        public MonumentFacts? ForBuilding(EntityId building) => facts?.Building == building ? facts : null;
    }
}
