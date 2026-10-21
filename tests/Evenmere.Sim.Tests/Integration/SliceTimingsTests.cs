using System.Globalization;
using System.Text;
using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Integration;

/// <summary>
/// Prints the per-system timing tables for the slice scenarios, read through <see cref="IProfilerReadout"/> the way the
/// CLI's timing table does. The numbers are wall-clock measurements for the report only: nothing here asserts on them,
/// because timing asserts are flaky in CI.
/// </summary>
public sealed class SliceTimingsTests(ITestOutputHelper output)
{
    [Fact]
    public void Timings_SliceVillagers_PrintsThePerSystemTable()
    {
        ScenarioResult result = ScenarioRunner.RunFile(Slice("slice_villagers.json"), ScenarioRunOptions.Strict);

        result.Passed.ShouldBeTrue(result.Report);
        output.WriteLine($"Timings for {result.Scenario}, {result.Ticks} ticks, seed 1:");
        output.WriteLine(FormatTable(result));
    }

    [Fact]
    public void Timings_SliceBreadChain_PrintsThePerSystemTable()
    {
        ScenarioResult result = ScenarioRunner.RunFile(Slice("slice_bread_chain.json"), ScenarioRunOptions.Strict);

        result.Passed.ShouldBeTrue(result.Report);
        output.WriteLine($"Timings for {result.Scenario}, {result.Ticks} ticks, seed 1:");
        output.WriteLine(FormatTable(result));
    }

    private static string Slice(string name) => Path.Combine(ScenarioPaths.ScenarioDirectory, name);

    // The same table the CLI prints: one row per step plus a total row, with the mean per call in microseconds.
    private static string FormatTable(ScenarioResult result)
    {
        string[] headers = ["step", "calls", "total ms", "mean us/call"];
        var rows = new List<string[]>();
        long totalElapsed = 0;
        foreach (SystemTiming timing in result.Timings)
        {
            rows.Add(Row(timing.Name, timing.Calls, timing.ElapsedTicks, result.TimerTicksPerSecond));
            totalElapsed += timing.ElapsedTicks;
        }

        rows.Add(Row("total", result.Ticks, totalElapsed, result.TimerTicksPerSecond));

        int[] widths = new int[headers.Length];
        for (int column = 0; column < widths.Length; column++)
        {
            widths[column] = headers[column].Length;
            foreach (string[] row in rows)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        var text = new StringBuilder();
        WriteRow(text, headers, widths);
        foreach (string[] row in rows)
        {
            WriteRow(text, row, widths);
        }

        double totalMs = (double)totalElapsed / result.TimerTicksPerSecond * 1000;
        text.Append(CultureInfo.InvariantCulture, $"average total per tick: {totalMs / result.Ticks:F3} ms");
        return text.ToString();
    }

    private static string[] Row(string name, long calls, long elapsedTicks, long timerTicksPerSecond)
    {
        double seconds = (double)elapsedTicks / timerTicksPerSecond;
        string mean = calls > 0 ? (seconds * 1_000_000 / calls).ToString("F3", CultureInfo.InvariantCulture) : "-";
        return [name, calls.ToString(CultureInfo.InvariantCulture), (seconds * 1000).ToString("F3", CultureInfo.InvariantCulture), mean];
    }

    private static void WriteRow(StringBuilder text, string[] cells, int[] widths)
    {
        for (int column = 0; column < cells.Length; column++)
        {
            string cell = cells[column];
            text.Append(column == 0 ? cell.PadRight(widths[column]) : cell.PadLeft(widths[column]));
            text.Append(column == cells.Length - 1 ? '\n' : "  ");
        }
    }
}
