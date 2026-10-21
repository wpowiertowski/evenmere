using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary><c>evenmere run</c>: creates a new game and ticks it headless, the same way the Godot host does, and prints the state hash.</summary>
internal static class RunCommand
{
    /// <summary>Runs the simulation.</summary>
    /// <remarks>
    /// Stdout is machine-readable: with <paramref name="hashEvery"/> above zero a <c>tick=&lt;t&gt; hash=&lt;x16&gt;</c> line after every
    /// that-many ticks, then always a final <c>hash=&lt;x16&gt;</c> line (16 lowercase hex digits). Everything for humans goes to
    /// <paramref name="error"/>: load diagnostics, and after the run a per-step timing table. If the content or the map has errors
    /// nothing runs, the diagnostics and a summary go to <paramref name="error"/>, and the exit code is 1.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="ticks">How many ticks to run; zero prints the hash of the new world.</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="hashEvery">Print an intermediate hash every this many ticks; zero for none.</param>
    /// <param name="mapId">The island to start on (a folder under <c>maps/</c>), or <see langword="null"/> for the empty world.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, or <see cref="ExitCodes.ContentErrors"/> if the content or the map has errors.</returns>
    public static int Execute(
        IReadOnlyList<string> roots, long ticks, ulong seed, long hashEvery, string? mapId, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(hashEvery);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        MapDef? map = null;
        if (mapId is not null)
        {
            MapLoadResult loaded = MapLoader.Load(roots, mapId, result.Db);
            TextReport.WriteDiagnostics(loaded.Diagnostics, error);
            if (loaded.Map is null)
            {
                error.WriteLine($"evenmere run: cannot load map \"{mapId}\".");
                return ExitCodes.ContentErrors;
            }

            map = loaded.Map;
        }

        var simulation = Simulation.CreateNew(result.Db, new NewGameOptions { Seed = seed, Map = map });
        for (long tick = 1; tick <= ticks; tick++)
        {
            simulation.Tick();
            if (hashEvery > 0 && tick % hashEvery == 0)
            {
                output.WriteLine($"tick={tick} hash={Hex.Format(simulation.ComputeStateHash())}");
            }
        }

        output.WriteLine($"hash={Hex.Format(simulation.ComputeStateHash())}");
        output.Flush();

        TimingTable.Write(error, simulation.GetTimings(), simulation.TimerTicksPerSecond, ticks);
        return ExitCodes.Success;
    }
}
