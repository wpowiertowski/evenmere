using Evenmere.Game.Rendering.Monuments;

namespace Evenmere.Game.Tests;

/// <summary>The monument stage geometry: placeholder tiers grow by stage, step inward as they rise, and split the finished height exactly.</summary>
public sealed class MonumentStageLayoutTests
{
    [Fact]
    public void SingleStage_FinishedIsWhole()
    {
        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount: 1, stagesDone: 1, stageProgressPerMille: 1000);

        boxes.ShouldHaveSingleItem().ShouldBe(new MonumentTierBox(1000, 1000, Built: true));
    }

    [Fact]
    public void ThreeStages_SplitTheFinishedHeight()
    {
        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount: 3, stagesDone: 3, stageProgressPerMille: 1000);

        boxes.Length.ShouldBe(3);
        int total = 0;
        foreach (MonumentTierBox box in boxes)
        {
            total += box.HeightShare;
        }

        total.ShouldBe(1000);
        MonumentStageLayout.FullHeightPerMille(3).ShouldBe(1000);
    }

    [Fact]
    public void FutureStages_AreGhostsWithNoHeight()
    {
        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount: 3, stagesDone: 0, stageProgressPerMille: 0);

        boxes[0].Built.ShouldBeFalse();
        boxes[0].HeightShare.ShouldBeGreaterThan(0);
        boxes[1].HeightShare.ShouldBe(0);
        boxes[2].HeightShare.ShouldBe(0);
    }

    [Fact]
    public void ActiveStage_GrowsWithProgress()
    {
        MonumentTierBox[] started = MonumentStageLayout.BoxesFor(stageCount: 2, stagesDone: 0, stageProgressPerMille: 100);
        MonumentTierBox[] grown = MonumentStageLayout.BoxesFor(stageCount: 2, stagesDone: 0, stageProgressPerMille: 800);

        grown[0].HeightShare.ShouldBeGreaterThan(started[0].HeightShare);
        grown[0].HeightShare.ShouldBeLessThanOrEqualTo(500);
    }

    [Fact]
    public void Tiers_StepInwardAsTheyRise()
    {
        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount: 3, stagesDone: 3, stageProgressPerMille: 1000);

        boxes[0].FootprintShare.ShouldBe(1000);
        boxes[1].FootprintShare.ShouldBe(1000 - MonumentStageLayout.StepInPerStagePerMille);
        boxes[2].FootprintShare.ShouldBeGreaterThanOrEqualTo(MonumentStageLayout.MinFootprintPerMille);
        (boxes[0].FootprintShare > boxes[1].FootprintShare).ShouldBeTrue();
        (boxes[1].FootprintShare > boxes[2].FootprintShare).ShouldBeTrue();
    }

    [Fact]
    public void Progress_ClampsToFull()
    {
        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount: 2, stagesDone: 0, stageProgressPerMille: 5000);

        boxes[0].HeightShare.ShouldBe(500);
    }

    [Fact]
    public void InvalidCounts_Throw()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MonumentStageLayout.BoxesFor(0, 0, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => MonumentStageLayout.BoxesFor(2, 3, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => MonumentStageLayout.FullHeightPerMille(0));
    }
}
