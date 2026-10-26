using System.Collections.Immutable;
using Evenmere.Tools.Perf;

namespace Evenmere.Tools.Tests.Perf;

/// <summary>
/// The perf report's verdict and OVER marks, from synthetic inputs: no simulation is ticked, so the
/// test stays deterministic and instant. The Deck-equivalent figures always use the single named factor.
/// </summary>
public sealed class PerfReportFormattingTests
{
    private static PerfReport EnforcedReport(bool passed, bool bucketOver) => new(
        SavePath: "perf-2k.evsave",
        SaveTick: 100,
        Ticks: 2000,
        Residents: 2000,
        Hash: "0123456789abcdef",
        Steps: [new PerfStepStats("logistics", 2000, 0.25, 0.25, 0.30, 0.50)],
        Buckets:
        [
            new PerfBucketStats(
                "logistics", ["logistics"], AverageMs: 0.25, P50Ms: 0.25, P99Ms: 0.30, MaxMs: 0.50,
                BudgetMs: 0.50, OverBudget: bucketOver),
        ],
        TotalAverageMs: 0.50,
        TotalP50Ms: 0.45,
        TotalP99Ms: 0.60,
        TotalMaxMs: 1.00,
        Enforced: true,
        Passed: passed);

    [Fact]
    public void ToText_WhenEnforcedAndPassed_PrintsPassVerdictAndDeckAverage()
    {
        string text = EnforcedReport(passed: true, bucketOver: false).ToText();

        text.ShouldContain("verdict: PASS");
        text.ShouldContain($"deck-equivalent (x{PerfBudgets.MacToDeckFactor:F1}): average 1.500 ms/tick");
        text.ShouldNotContain("OVER");
    }

    [Fact]
    public void ToText_WhenBucketOverBudget_MarksRowAndFailsVerdict()
    {
        string text = EnforcedReport(passed: false, bucketOver: true).ToText();

        text.ShouldContain("OVER");
        text.ShouldContain("verdict: FAIL");
    }

    [Fact]
    public void ToText_WhenNotEnforced_SaysSoAndNeverFails()
    {
        PerfReport report = EnforcedReport(passed: true, bucketOver: true) with { Enforced = false, Passed = true };

        report.ToText().ShouldContain("verdict: not enforced");
    }

    [Fact]
    public void ToJson_ReportsVerdictBudgetsAndDeckFigures()
    {
        string json = EnforcedReport(passed: false, bucketOver: true).ToJson();

        json.ShouldContain("\"passed\": false");
        json.ShouldContain("\"overBudget\": true");
        json.ShouldContain("\"macToDeckFactor\": 3");
        json.ShouldContain("\"deckAverageMs\": 0.75");
    }
}
