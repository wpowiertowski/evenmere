using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary><c>evenmere balance</c>: plays the island with a scripted auto-player and reports pacing and stalls.</summary>
internal static class BalanceCommand
{
    private const long DefaultSeed = 1;
    private const long DefaultMaxMinutes = 90;

    /// <summary>Runs the bot.</summary>
    /// <remarks>
    /// Stdout is the machine-readable JSON report: a <c>deterministic</c> section (byte-identical for one seed and
    /// script) and a <c>timings</c> section with the wall-clock milliseconds per tick. Everything human-readable goes
    /// to <paramref name="error"/>: the chosen content roots, the text report and a per-step timing table. Reaching
    /// or missing the script's target is reported, not an error: the exit is non-zero only for bad input.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="mapId">The island to settle (a folder under <c>maps/</c>).</param>
    /// <param name="botPath">The bot script file.</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="maxMinutes">The cap on game minutes to simulate.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, <see cref="ExitCodes.ContentErrors"/> or <see cref="ExitCodes.Usage"/>.</returns>
    public static int Execute(
        IReadOnlyList<string> roots,
        string mapId,
        string botPath,
        ulong seed,
        long maxMinutes,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(botPath);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        MapLoadResult loaded = MapLoader.Load(roots, mapId, result.Db);
        TextReport.WriteDiagnostics(loaded.Diagnostics, error);
        if (loaded.Map is null)
        {
            error.WriteLine($"evenmere balance: cannot load map \"{mapId}\".");
            return ExitCodes.ContentErrors;
        }

        Balance.BotScript script;
        try
        {
            script = Balance.BotScript.ParseFile(botPath);
            script.ValidateContent(result.Db);
        }
        catch (Balance.BotScriptException ex)
        {
            error.WriteLine($"evenmere balance: {ex.Message}");
            return ExitCodes.Usage;
        }

        long maxTicks = checked(maxMinutes * Balance.BotScript.TicksPerGameMinute);
        Balance.BalanceReport report = Balance.BalanceBot.Run(
            result.Db, loaded.Map, script, new Balance.BalanceOptions((ulong)seed, maxTicks));

        output.Write(report.ToJson());
        output.Flush();
        error.Write(report.ToText());
        return ExitCodes.Success;
    }
}
