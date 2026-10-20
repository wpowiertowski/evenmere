using Evenmere.Content;

namespace Evenmere.Tools.Reporting;

/// <summary>The plain-text form of a content report: one diagnostic per line, then a short summary.</summary>
internal static class TextReport
{
    /// <summary>Writes one line per diagnostic, in the order given (the loader and linter sort them).</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <param name="writer">Where to write.</param>
    public static void WriteDiagnostics(IEnumerable<ContentDiagnostic> diagnostics, TextWriter writer)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            writer.WriteLine(diagnostic.ToString());
        }
    }

    /// <summary>Writes the summary: the error and warning counts, the content hash, and the item counts per type.</summary>
    /// <param name="summary">The summary.</param>
    /// <param name="writer">Where to write.</param>
    public static void WriteSummary(ContentSummary summary, TextWriter writer)
    {
        writer.WriteLine($"errors: {summary.ErrorCount}, warnings: {summary.WarningCount}");
        writer.WriteLine($"content hash: {summary.ContentHashText}");
        writer.WriteLine($"items: {summary.ItemCount} ({summary.FormatCounts()})");
    }
}
