using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Evenmere.Tools.Perf;

/// <summary>
/// The performance budgets <c>evenmere perf</c> enforces (architecture.md, §Performance budgets): the per-tick
/// averages each system group may take on Steam Deck–class hardware at the scale target, and the totals.
/// The nightly job records the raw numbers for the trend; the M2 gate binds on the Deck-equivalent average
/// (the raw development-Mac average × <see cref="MacToDeckFactor"/>).
/// </summary>
public static class PerfBudgets
{
    /// <summary>
    /// Gets the Mac→Deck factor the report multiplies raw Mac averages by: a single-core M4 Pro is about
    /// three times as fast as a Deck Zen 2 core (architecture.md, §Performance budgets). One named constant,
    /// used for every Deck-equivalent figure the report prints.
    /// </summary>
    public const double MacToDeckFactor = 3.0;

    /// <summary>Gets the total average budget per tick in milliseconds (2.0 ms).</summary>
    public const double TotalAverageMs = 2.0;

    /// <summary>Gets the total p99 budget per tick in milliseconds (6 ms).</summary>
    public const double TotalP99Ms = 6.0;

    /// <summary>
    /// Gets how far above a budget a Deck-equivalent average may run before <c>--enforce</c> fails:
    /// budget × 1.25.
    /// </summary>
    public const double OverBudgetTolerance = 1.25;

    /// <summary>One budget row: the profiler steps it covers and the average milliseconds per tick they may take together.</summary>
    /// <param name="Name">The row's display name.</param>
    /// <param name="Steps">The profiler step names in this row, in tick order.</param>
    /// <param name="BudgetMs">The average milliseconds per tick the row's steps may take together.</param>
    public sealed record BudgetRow(string Name, ImmutableArray<string> Steps, double BudgetMs);

    /// <summary>Gets the budget rows, in the order architecture.md lists them.</summary>
    public static ImmutableArray<BudgetRow> Rows { get; } =
    [
        new("construction", ["construction"], 0.10),
        new("workforce", ["workforce"], 0.05),
        new("production", ["production"], 0.40),
        new("logistics", ["logistics"], 0.50),
        new("needs, population, economy", ["needs", "population", "economy"], 0.40),
        new("research, culture, progression", ["research", "culture", "progression"], 0.05),
        new("happenings, advisors, statistics", ["happenings", "advisors", "statistics"], 0.15),
        // The clock and command steps, plus every step the budget table does not name (exploration and any
        // future system): the table's "clock, commands and other" row.
        new("clock, commands, other", ["clock", "commands"], 0.35),
    ];

    /// <summary>Finds the budget row a profiler step belongs to; unnamed steps join "clock, commands, other".</summary>
    /// <param name="step">The profiler step name.</param>
    /// <returns>The row.</returns>
    public static BudgetRow RowFor(string step)
    {
        foreach (BudgetRow row in Rows)
        {
            if (row.Steps.Contains(step))
            {
                return row;
            }
        }

        return Rows[^1];
    }
}

/// <summary>The measured cost of one profiler step: the raw milliseconds per tick and the Deck equivalents.</summary>
/// <param name="Name">The profiler step name.</param>
/// <param name="Samples">How many ticks were measured.</param>
/// <param name="AverageMs">The raw mean milliseconds per tick.</param>
/// <param name="P50Ms">The raw median milliseconds per tick.</param>
/// <param name="P99Ms">The raw 99th-percentile milliseconds per tick.</param>
/// <param name="MaxMs">The raw slowest tick in milliseconds.</param>
public sealed record PerfStepStats(
    string Name, long Samples, double AverageMs, double P50Ms, double P99Ms, double MaxMs)
{
    /// <summary>Gets the Deck-equivalent average: the raw average × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckAverageMs => AverageMs * PerfBudgets.MacToDeckFactor;

    /// <summary>Gets the Deck-equivalent p99: the raw p99 × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckP99Ms => P99Ms * PerfBudgets.MacToDeckFactor;
}

/// <summary>The measured cost of one budget row, summed over its steps, with the enforcement verdict.</summary>
/// <param name="Name">The budget row's display name.</param>
/// <param name="Steps">The profiler step names in this row.</param>
/// <param name="AverageMs">The raw mean milliseconds per tick over the row's steps.</param>
/// <param name="P50Ms">The raw median milliseconds per tick over the row's steps.</param>
/// <param name="P99Ms">The raw 99th-percentile milliseconds per tick over the row's steps.</param>
/// <param name="MaxMs">The raw slowest tick in milliseconds over the row's steps.</param>
/// <param name="BudgetMs">The average budget in milliseconds per tick.</param>
/// <param name="OverBudget">Whether the Deck-equivalent average exceeds the budget × 1.25.</param>
public sealed record PerfBucketStats(
    string Name,
    ImmutableArray<string> Steps,
    double AverageMs,
    double P50Ms,
    double P99Ms,
    double MaxMs,
    double BudgetMs,
    bool OverBudget)
{
    /// <summary>Gets the Deck-equivalent average: the raw average × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckAverageMs => AverageMs * PerfBudgets.MacToDeckFactor;

    /// <summary>Gets the Deck-equivalent p99: the raw p99 × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckP99Ms => P99Ms * PerfBudgets.MacToDeckFactor;
}

/// <summary>
/// What <c>evenmere perf</c> measured: the per-step and per-budget cost of the run, the save it ran on,
/// and whether the Deck-equivalent averages fit the budgets. Timing is wall-clock and varies between
/// machines; only the shape (which steps dominate) is comparable across runs.
/// </summary>
/// <param name="SavePath">The save file that was measured, as given on the command line.</param>
/// <param name="SaveTick">The tick the save was written at (the first tick the run measured).</param>
/// <param name="Ticks">How many ticks were measured.</param>
/// <param name="Residents">The total residents at the end of the run.</param>
/// <param name="Hash">The state hash at the end of the run, 16 lowercase hex digits.</param>
/// <param name="Steps">One row per profiler step, in tick order.</param>
/// <param name="Buckets">One row per budget, in architecture.md order.</param>
/// <param name="TotalAverageMs">The raw mean milliseconds per tick over the whole tick.</param>
/// <param name="TotalP50Ms">The raw median milliseconds per whole tick.</param>
/// <param name="TotalP99Ms">The raw 99th-percentile milliseconds per whole tick.</param>
/// <param name="TotalMaxMs">The raw slowest whole tick in milliseconds.</param>
/// <param name="Enforced">Whether the run was checked against budget × 1.25.</param>
/// <param name="Passed">Whether every Deck-equivalent average fits its budget × 1.25 (always <see langword="true"/> when not enforced).</param>
public sealed record PerfReport(
    string SavePath,
    long SaveTick,
    long Ticks,
    int Residents,
    string Hash,
    ImmutableArray<PerfStepStats> Steps,
    ImmutableArray<PerfBucketStats> Buckets,
    double TotalAverageMs,
    double TotalP50Ms,
    double TotalP99Ms,
    double TotalMaxMs,
    bool Enforced,
    bool Passed)
{
    /// <summary>Gets the Deck-equivalent total average: the raw average × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckTotalAverageMs => TotalAverageMs * PerfBudgets.MacToDeckFactor;

    /// <summary>Gets the Deck-equivalent total p99: the raw p99 × <see cref="PerfBudgets.MacToDeckFactor"/>.</summary>
    public double DeckTotalP99Ms => TotalP99Ms * PerfBudgets.MacToDeckFactor;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Serialises the machine-readable report: budgets, per-step and per-budget stats, and the verdict.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline.</returns>
    public string ToJson()
    {
        var steps = new JsonArray();
        foreach (PerfStepStats step in Steps)
        {
            steps.Add(new JsonObject
            {
                ["name"] = step.Name,
                ["samples"] = step.Samples,
                ["averageMs"] = step.AverageMs,
                ["p50Ms"] = step.P50Ms,
                ["p99Ms"] = step.P99Ms,
                ["maxMs"] = step.MaxMs,
                ["deckAverageMs"] = step.DeckAverageMs,
                ["deckP99Ms"] = step.DeckP99Ms,
            });
        }

        var buckets = new JsonArray();
        foreach (PerfBucketStats bucket in Buckets)
        {
            buckets.Add(new JsonObject
            {
                ["name"] = bucket.Name,
                ["averageMs"] = bucket.AverageMs,
                ["p50Ms"] = bucket.P50Ms,
                ["p99Ms"] = bucket.P99Ms,
                ["maxMs"] = bucket.MaxMs,
                ["deckAverageMs"] = bucket.DeckAverageMs,
                ["deckP99Ms"] = bucket.DeckP99Ms,
                ["budgetMs"] = bucket.BudgetMs,
                ["overBudget"] = bucket.OverBudget,
            });
        }

        var root = new JsonObject
        {
            ["save"] = SavePath,
            ["saveTick"] = SaveTick,
            ["ticks"] = Ticks,
            ["residents"] = Residents,
            ["hash"] = Hash,
            ["macToDeckFactor"] = PerfBudgets.MacToDeckFactor,
            ["totalAverageMs"] = TotalAverageMs,
            ["totalP50Ms"] = TotalP50Ms,
            ["totalP99Ms"] = TotalP99Ms,
            ["totalMaxMs"] = TotalMaxMs,
            ["deckTotalAverageMs"] = DeckTotalAverageMs,
            ["deckTotalP99Ms"] = DeckTotalP99Ms,
            ["totalBudgetMs"] = PerfBudgets.TotalAverageMs,
            ["totalP99BudgetMs"] = PerfBudgets.TotalP99Ms,
            ["enforced"] = Enforced,
            ["passed"] = Passed,
            ["steps"] = steps,
            ["buckets"] = buckets,
        };
        return root.ToJsonString(_json) + "\n";
    }

    /// <summary>Renders the human-readable report: the save line, the per-budget table against the budgets, and the verdict.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"perf: {Ticks} ticks from {SavePath} (tick {SaveTick}, {Residents} residents, hash {Hash})\n");
        text.Append(CultureInvariant($"raw average {TotalAverageMs:F3} ms/tick, p50 {TotalP50Ms:F3}, p99 {TotalP99Ms:F3}, max {TotalMaxMs:F3}\n"));
        text.Append(CultureInvariant($"deck-equivalent (x{PerfBudgets.MacToDeckFactor:F1}): average {DeckTotalAverageMs:F3} ms/tick (budget {PerfBudgets.TotalAverageMs:F2}), p99 {DeckTotalP99Ms:F3} ms/tick (budget {PerfBudgets.TotalP99Ms:F2})\n"));
        text.Append("budget                          raw avg   deck avg    budget   p50     p99     max\n");
        foreach (PerfBucketStats bucket in Buckets)
        {
            string flag = bucket.OverBudget ? " OVER" : string.Empty;
            text.Append(CultureInvariant($"{bucket.Name,-30} {bucket.AverageMs,7:F3} {bucket.DeckAverageMs,8:F3} {bucket.BudgetMs,7:F2} {bucket.P50Ms,7:F3} {bucket.P99Ms,7:F3} {bucket.MaxMs,7:F3}{flag}\n"));
        }

        text.Append("step                            raw avg   deck avg     p50     p99     max\n");
        foreach (PerfStepStats step in Steps)
        {
            text.Append(CultureInvariant($"{step.Name,-30} {step.AverageMs,7:F3} {step.DeckAverageMs,8:F3} {step.P50Ms,7:F3} {step.P99Ms,7:F3} {step.MaxMs,7:F3}\n"));
        }

        text.Append(Enforced
            ? (Passed
                ? CultureInvariant($"verdict: PASS: every deck-equivalent average fits its budget x {PerfBudgets.OverBudgetTolerance:F2}\n")
                : CultureInvariant($"verdict: FAIL: a deck-equivalent average exceeds its budget x {PerfBudgets.OverBudgetTolerance:F2}\n"))
            : "verdict: not enforced (pass --enforce to check budget x 1.25)\n");
        return text.ToString();

        static string CultureInvariant(FormattableString formattable) =>
            formattable.ToString(CultureInfo.InvariantCulture);
    }
}
