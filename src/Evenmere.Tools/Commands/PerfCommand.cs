using Evenmere.Content;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Tools.Perf;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary>
/// <c>evenmere perf --save &lt;file&gt; --ticks &lt;n&gt;</c>: loads a save and measures the wall-clock cost
/// of ticking it, reporting the raw average, p50, p99 and maximum per system against the budget table, plus
/// the Deck-equivalent average and p99. With <c>--enforce</c>, exits non-zero when a Deck-equivalent average
/// exceeds its budget × 1.25.
/// </summary>
internal static class PerfCommand
{
    /// <summary>The exit code when the budgets are enforced and a Deck-equivalent average exceeds its budget × 1.25.</summary>
    internal const int BudgetExceeded = 3;

    /// <summary>Measures a save.</summary>    /// <remarks>
    /// Stdout is the machine-readable JSON report. Everything for humans — load diagnostics, the save-load
    /// notes, the text report — goes to <paramref name="error"/>. The timings are wall-clock measurements
    /// for diagnostics only and vary from run to run.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="savePath">The save file to measure.</param>
    /// <param name="ticks">How many ticks to measure; at least 1.</param>
    /// <param name="enforce">Whether to fail when a Deck-equivalent average exceeds its budget × 1.25.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, <see cref="ExitCodes.ContentErrors"/>, or <see cref="BudgetExceeded"/>.</returns>
    public static int Execute(
        IReadOnlyList<string> roots, string savePath, long ticks, bool enforce, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        ISimulation simulation;
        try
        {
            using FileStream save = File.OpenRead(savePath);
            (simulation, SaveReport report) = SaveGame.Read(save, result.Db);
            foreach (string line in report.Remapped)
            {
                error.WriteLine($"evenmere perf: remapped: {line}");
            }

            foreach (string line in report.Ruins)
            {
                error.WriteLine($"evenmere perf: ruin: {line}");
            }

            foreach (string line in report.Dropped)
            {
                error.WriteLine($"evenmere perf: dropped: {line}");
            }

            foreach (string line in report.Notes)
            {
                error.WriteLine($"evenmere perf: note: {line}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            error.WriteLine($"evenmere perf: cannot load \"{savePath}\": {exception.Message}");
            return ExitCodes.ContentErrors;
        }

        PerfReport report2 = PerfRunner.Run(simulation, savePath, ticks, enforce);
        output.Write(report2.ToJson());
        output.Flush();
        error.Write(report2.ToText());
        error.Flush();
        return report2.Passed ? ExitCodes.Success : BudgetExceeded;
    }

    /// <summary>Builds a provisional perf reference save.</summary>
    /// <remarks>
    /// Stdout is a one-line JSON summary. Everything human-readable, including the rejection histogram
    /// and the byte-identical check, goes to <paramref name="error"/>. The build runs twice and compares
    /// the save bytes, so a non-deterministic generator fails loudly instead of committing a flaky fixture.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="which">Which save to build: <c>2k</c> or <c>20k</c>.</param>
    /// <param name="outPath">The file to write, created or overwritten.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/> or <see cref="ExitCodes.ContentErrors"/>.</returns>
    public static int Build(
        IReadOnlyList<string> roots, string which, string outPath, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrEmpty(which);
        ArgumentException.ThrowIfNullOrEmpty(outPath);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "evenmere-perf-" + Path.GetRandomFileName());
        try
        {
            byte[] first = BuildBytes(result.Db, which, Path.Combine(scratch, "a"), error);
            byte[] second = BuildBytes(result.Db, which, Path.Combine(scratch, "b"), error);
            if (!first.SequenceEqual(second))
            {
                error.WriteLine($"evenmere perf: --build-save {which}: the two builds differ; the generator is not deterministic.");
                return ExitCodes.ContentErrors;
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outPath, first);
            error.WriteLine($"evenmere perf: --build-save {which}: wrote {first.Length} bytes to \"{outPath}\"; two builds byte-identical.");
            output.Write(BuildSummaryJson(which, outPath, first.Length) + "\n");
            output.Flush();
            return ExitCodes.Success;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static byte[] BuildBytes(ContentDb content, string which, string scratchRoot, TextWriter error)
    {
        string mapId = which == "20k" ? DensePlacement.Perf20kMapId : DensePlacement.Perf2kMapId;
        (Simulation simulation, DensePlacementReport report) = which == "20k"
            ? DensePlacement.Build20kDetailed(content, scratchRoot)
            : DensePlacement.Build2kDetailed(content, scratchRoot);
        error.WriteLine(
            $"evenmere perf: build {which}: houses {report.HousesPlaced}, services {report.ServicesPlaced}, " +
            $"producers {report.ProducersPlaced}, sites left {report.SitesLeft}, " +
            $"tick {report.FinalTick}, residents {report.FinalResidents}, hash {report.FinalHash}");
        foreach ((string reason, int count) in report.Rejections)
        {
            error.WriteLine($"evenmere perf: build {which}: rejected {count}x {reason}");
        }

        string path = Path.Combine(scratchRoot, "build.evsave");
        DensePlacement.WriteSave(simulation, content, mapId, path);
        return File.ReadAllBytes(path);
    }

    private static string BuildSummaryJson(string which, string outPath, long bytes) =>
        $"{{\"build\": \"{which}\", \"out\": {System.Text.Json.JsonSerializer.Serialize(outPath)}, \"bytes\": {bytes}}}";
}
