using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Lint;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary>How <c>evenmere lint</c> prints its report.</summary>
internal enum LintFormat
{
    /// <summary>One diagnostic per line, then a short summary.</summary>
    Text = 0,

    /// <summary>One JSON object; see <see cref="LintJson"/> for the shape.</summary>
    Json = 1,
}

/// <summary>
/// <c>evenmere lint</c>: loads the content roots, runs the content linter (the loader's checks, the JSON Schemas and rules EVL003 to EVL008)
/// and prints the result.
/// </summary>
internal static class LintCommand
{
    /// <summary>Lints content.</summary>
    /// <remarks>
    /// In text format the diagnostics always go to <paramref name="output"/>, so <c>lint | grep EVL003</c> works. The summary
    /// (error and warning counts, content hash, item counts) follows on <paramref name="output"/> when there are no errors and on
    /// <paramref name="error"/> otherwise, so a failing run leaves stdout holding only findings. In JSON format everything is on
    /// <paramref name="output"/>, success or not, so a script can always parse it; the exit code tells the verdict.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="format">The report format.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/> if there are no errors (warnings are allowed), otherwise <see cref="ExitCodes.ContentErrors"/>.</returns>
    public static int Execute(IReadOnlyList<string> roots, LintFormat format, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);

        // The linter's list already contains the loader's diagnostics.
        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);
        var summary = ContentSummary.Create(result, diagnostics);
        int exitCode = summary.ErrorCount > 0 ? ExitCodes.ContentErrors : ExitCodes.Success;

        if (format == LintFormat.Json)
        {
            LintJson.Write(summary, diagnostics, output);
        }
        else
        {
            TextReport.WriteDiagnostics(diagnostics, output);
            TextReport.WriteSummary(summary, exitCode == ExitCodes.Success ? output : error);
        }

        return exitCode;
    }
}
