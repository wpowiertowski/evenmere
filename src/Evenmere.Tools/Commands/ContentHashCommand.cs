using Evenmere.Content;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary><c>evenmere content-hash</c>: prints the hash that saves store to detect changed content.</summary>
internal static class ContentHashCommand
{
    /// <summary>Loads the content and prints its hash.</summary>
    /// <remarks>
    /// Stdout holds the hash as 16 lowercase hex digits and nothing else. If the content does not load, the loader's
    /// diagnostics and a summary go to <paramref name="error"/>, stdout stays empty and the exit code is 1: the hash of
    /// content the game cannot use is not worth recording.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, or <see cref="ExitCodes.ContentErrors"/> if the content has errors.</returns>
    public static int Execute(IReadOnlyList<string> roots, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        if (result.Db is null)
        {
            var summary = ContentSummary.Create(result, result.Diagnostics);
            TextReport.WriteDiagnostics(result.Diagnostics, error);
            TextReport.WriteSummary(summary, error);
            return ExitCodes.ContentErrors;
        }

        TextReport.WriteDiagnostics(result.Diagnostics, error);
        output.WriteLine(Hex.Format(result.Db.ContentHash));
        return ExitCodes.Success;
    }
}
