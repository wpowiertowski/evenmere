using System.Globalization;
using System.Text;
using Evenmere.Sim.Diagnostics;

namespace Evenmere.Tools.Reporting;

/// <summary>
/// The per-step timing table <c>evenmere run</c> prints on stderr, from the profiler's readout. Times are wall-clock
/// measurements for diagnostics only and vary from run to run, which is why this goes to stderr and never to stdout.
/// </summary>
internal static class TimingTable
{
    private static readonly string[] _headers = ["step", "calls", "total ms", "mean us/call"];

    /// <summary>Writes one row per step (the clock, the commands and each ticked system, in tick order) and a final <c>total</c> row.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="timings">The profiler's timings.</param>
    /// <param name="timerTicksPerSecond">The profiler's timer frequency.</param>
    /// <param name="ticksRun">How many ticks were run. In the <c>total</c> row, the calls column is this number and the mean is per tick.</param>
    public static void Write(TextWriter writer, IReadOnlyList<SystemTiming> timings, long timerTicksPerSecond, long ticksRun)
    {
        var rows = new List<string[]>(timings.Count + 1);
        long totalElapsed = 0;
        foreach (SystemTiming timing in timings)
        {
            rows.Add(Row(timing.Name, timing.Calls, timing.ElapsedTicks, timerTicksPerSecond));
            totalElapsed += timing.ElapsedTicks;
        }

        rows.Add(Row("total", ticksRun, totalElapsed, timerTicksPerSecond));

        int[] widths = new int[_headers.Length];
        for (int column = 0; column < widths.Length; column++)
        {
            widths[column] = _headers[column].Length;
            foreach (string[] row in rows)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        WriteRow(writer, _headers, widths);
        foreach (string[] row in rows)
        {
            WriteRow(writer, row, widths);
        }
    }

    private static string[] Row(string name, long calls, long elapsedTicks, long timerTicksPerSecond)
    {
        double seconds = (double)elapsedTicks / timerTicksPerSecond;
        string mean = calls > 0 ? (seconds * 1_000_000 / calls).ToString("F3", CultureInfo.InvariantCulture) : "-";
        return [name, calls.ToString(CultureInfo.InvariantCulture), (seconds * 1000).ToString("F3", CultureInfo.InvariantCulture), mean];
    }

    private static void WriteRow(TextWriter writer, string[] cells, int[] widths)
    {
        // The step name is left-aligned, the numbers right-aligned so the decimal points line up.
        var line = new StringBuilder();
        for (int column = 0; column < cells.Length; column++)
        {
            if (column > 0)
            {
                line.Append("  ");
            }

            line.Append(column == 0 ? cells[column].PadRight(widths[column]) : cells[column].PadLeft(widths[column]));
        }

        writer.WriteLine(line.ToString().TrimEnd());
    }
}
