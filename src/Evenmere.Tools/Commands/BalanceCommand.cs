using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;
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
    /// <param name="size">When not 0, generates an island this many tiles across (256, 320 or 384) from the seed instead of loading <paramref name="mapId"/>.</param>
    /// <returns><see cref="ExitCodes.Success"/>, <see cref="ExitCodes.ContentErrors"/> or <see cref="ExitCodes.Usage"/>.</returns>
    public static int Execute(
        IReadOnlyList<string> roots,
        string mapId,
        string botPath,
        ulong seed,
        long maxMinutes,
        TextWriter output,
        TextWriter error,
        int size = 0)
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

        MapDef map;
        if (size != 0)
        {
            if (size != IslandGenerator.MinSize && size != IslandGenerator.DefaultSize && size != IslandGenerator.MaxSize)
            {
                error.WriteLine($"evenmere balance: --size must be 256, 320 or 384 (got {size}).");
                return ExitCodes.Usage;
            }

            map = IslandGenerator.Generate(unchecked((int)seed), size, result.Db);
            error.WriteLine($"evenmere balance: generated island seed {seed}, size {size}.");
        }
        else
        {
            MapLoadResult loaded = MapLoader.Load(roots, mapId, result.Db);
            TextReport.WriteDiagnostics(loaded.Diagnostics, error);
            if (loaded.Map is null)
            {
                error.WriteLine($"evenmere balance: cannot load map \"{mapId}\".");
                return ExitCodes.ContentErrors;
            }

            map = loaded.Map;
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
        if (string.Equals(script.Name, "era1", StringComparison.Ordinal))
        {
            // The Era I script runs the Era I bot: expeditions, traditions, festivals and happenings on top of
            // the v1 build order, playing to both gateways instead of a resident target.
            Balance.EraOne.EraOneReport era = Balance.EraOne.EraOneBot.Run(
                result.Db, map, script, new Balance.BalanceOptions(seed, maxTicks));

            output.Write(era.ToJson());
            output.Flush();
            error.Write(era.ToText());
            return ExitCodes.Success;
        }

        Balance.BalanceReport report = Balance.BalanceBot.Run(
            result.Db, map, script, new Balance.BalanceOptions(seed, maxTicks));

        output.Write(report.ToJson());
        output.Flush();
        error.Write(report.ToText());
        return ExitCodes.Success;
    }
}
