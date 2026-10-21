using Evenmere.Sim.Persistence;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary><c>evenmere hash --save &lt;file&gt;</c>: prints the state hash stored in a save file, the same 16-hex format <c>run</c> prints.</summary>
internal static class HashCommand
{
    /// <summary>Reads a save and prints its state hash.</summary>
    /// <remarks>
    /// Stdout holds <c>hash=&lt;x16&gt;</c> and nothing else. The hash is recomputed from the save's body, so a corrupt
    /// file fails instead of printing a stale number. Problems go to <paramref name="error"/> and the exit code is 1.
    /// </remarks>
    /// <param name="savePath">The save file.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, or <see cref="ExitCodes.ContentErrors"/> if the file cannot be read as a save.</returns>
    public static int Execute(string savePath, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(savePath);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ulong hash;
        try
        {
            using FileStream stream = File.OpenRead(savePath);
            hash = SaveGame.ReadStateHash(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            error.WriteLine($"evenmere hash: cannot read the save \"{savePath}\": {exception.Message}");
            return ExitCodes.ContentErrors;
        }

        output.WriteLine($"hash={Hex.Format(hash)}");
        return ExitCodes.Success;
    }
}
