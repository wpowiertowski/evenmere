using Evenmere.Content;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary><c>evenmere replay --save &lt;file&gt; --log &lt;file&gt; --ticks &lt;n&gt;</c>: loads a save, feeds it the logged commands and ticks to a tick, then prints the state hash.</summary>
internal static class ReplayCommand
{
    /// <summary>Replays a session.</summary>
    /// <remarks>
    /// Stdout holds <c>hash=&lt;x16&gt;</c> and nothing else. The content roots provide the current content the save's IDs
    /// remap into. Problems go to <paramref name="error"/> and the exit code is 1.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="savePath">The save file.</param>
    /// <param name="logPath">The command log recorded with the save's content.</param>
    /// <param name="untilTick">The tick to stop at; at least the save's tick.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, or <see cref="ExitCodes.ContentErrors"/> if the content, save or log cannot be read.</returns>
    public static int Execute(
        IReadOnlyList<string> roots, string savePath, string logPath, long untilTick, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentException.ThrowIfNullOrEmpty(logPath);
        ArgumentOutOfRangeException.ThrowIfNegative(untilTick);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        ulong hash;
        try
        {
            using FileStream save = File.OpenRead(savePath);
            using FileStream log = File.OpenRead(logPath);
            hash = Replay.Run(save, log, result.Db, untilTick);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or ArgumentOutOfRangeException or NotSupportedException)
        {
            error.WriteLine($"evenmere replay: cannot replay \"{savePath}\" with \"{logPath}\": {exception.Message}");
            return ExitCodes.ContentErrors;
        }

        output.WriteLine($"hash={Hex.Format(hash)}");
        return ExitCodes.Success;
    }
}
