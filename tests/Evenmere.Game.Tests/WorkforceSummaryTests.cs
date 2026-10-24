using Evenmere.Game.Ui.Hud;

namespace Evenmere.Game.Tests;

/// <summary>M1-37: the shared workforce summary the HUD, the build menu and the inspector agree on.</summary>
public sealed class WorkforceSummaryTests
{
    [Fact]
    public void PlaytestSettlers_CoveredWithNoShortage()
    {
        var summary = WorkforceSummary.From(supply: 45, demand: 51, workingBelow: 0, filledFromAbove: 6);
        summary.OwnFilled.ShouldBe(45);
        summary.Covered.ShouldBe(6);
        summary.Shortfall.ShouldBe(0);
        summary.Free.ShouldBe(0);
        summary.Below.ShouldBe(0);
    }

    [Fact]
    public void PlaytestVillagers_FreeAndBelow()
    {
        var summary = WorkforceSummary.From(supply: 303, demand: 23, workingBelow: 6, filledFromAbove: 0);
        summary.OwnFilled.ShouldBe(23);
        summary.Covered.ShouldBe(0);
        summary.Shortfall.ShouldBe(0);
        summary.Free.ShouldBe(274);
        summary.Below.ShouldBe(6);
    }

    [Fact]
    public void RealShortage_ReportsShortfall()
    {
        var summary = WorkforceSummary.From(supply: 12, demand: 20, workingBelow: 0, filledFromAbove: 2);
        summary.OwnFilled.ShouldBe(12);
        summary.Shortfall.ShouldBe(6);
        summary.Free.ShouldBe(0);
    }

    [Fact]
    public void StaleCounts_ClampAtZero()
    {
        // Live supply and demand moved since the last daily allocation: nothing goes negative.
        var summary = WorkforceSummary.From(supply: 5, demand: 10, workingBelow: 8, filledFromAbove: 9);
        summary.OwnFilled.ShouldBe(5);
        summary.Shortfall.ShouldBe(0);
        summary.Free.ShouldBe(0);
        summary.Below.ShouldBe(8);
        summary.Covered.ShouldBe(9);

        var negative = WorkforceSummary.From(supply: -3, demand: -5, workingBelow: -2, filledFromAbove: -1);
        negative.Supply.ShouldBe(0);
        negative.Demand.ShouldBe(0);
        negative.OwnFilled.ShouldBe(0);
        negative.Shortfall.ShouldBe(0);
        negative.Free.ShouldBe(0);
        negative.Below.ShouldBe(0);
        negative.Covered.ShouldBe(0);
    }

    [Fact]
    public void UnderemployedCount_InvertsMoraleLikeExplain()
    {
        WorkforceSummary.UnderemployedCount(residents: 12, moralePerMille: 1000).ShouldBe(0);
        WorkforceSummary.UnderemployedCount(residents: 0, moralePerMille: 500).ShouldBe(0);
        WorkforceSummary.UnderemployedCount(residents: 6, moralePerMille: 0).ShouldBe(6);
        // 12 residents at 670 per-mille: 12 × 330 ÷ 1000 floored is 3.
        WorkforceSummary.UnderemployedCount(residents: 12, moralePerMille: 670).ShouldBe(3);
    }
}
