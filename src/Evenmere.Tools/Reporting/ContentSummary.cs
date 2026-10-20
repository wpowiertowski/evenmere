using System.Collections.Immutable;
using System.Globalization;
using Evenmere.Content;
using Evenmere.Content.Loading;

namespace Evenmere.Tools.Reporting;

/// <summary>
/// What a load found, boiled down for printing: the content hash, how many items of each type there are, and how many
/// diagnostics of each severity. It is built from the raw files rather than the typed database, so it also exists when
/// the content has errors and no database could be built.
/// </summary>
/// <param name="ContentHash">The content hash.</param>
/// <param name="Counts">Item counts keyed by content directory name (<c>goods</c>, <c>buildings</c>, ...), in ordinal key order. Every type is present, with 0 if it has no items. The single tunables file (<c>rules/core.json</c>) is not an item and is not counted.</param>
/// <param name="ErrorCount">The number of error diagnostics.</param>
/// <param name="WarningCount">The number of warning diagnostics.</param>
internal sealed record ContentSummary(ulong ContentHash, ImmutableArray<KeyValuePair<string, int>> Counts, int ErrorCount, int WarningCount)
{
    /// <summary>Gets the content hash as 16 lowercase hex digits.</summary>
    public string ContentHashText => Hex.Format(ContentHash);

    /// <summary>Gets the total number of items over all types.</summary>
    public int ItemCount => Counts.Sum(static c => c.Value);

    /// <summary>Summarises a load and the diagnostics to report for it.</summary>
    /// <param name="result">The load result.</param>
    /// <param name="diagnostics">The diagnostics to count; for <c>lint</c> these include the linter's, for other commands just the loader's.</param>
    /// <returns>The summary.</returns>
    public static ContentSummary Create(ContentLoadResult result, IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (ContentType type in ContentTypes.All)
        {
            if (type != ContentType.Rules)
            {
                counts.Add(type.DirectoryName(), 0);
            }
        }

        foreach (RawItem item in result.Raw.Items)
        {
            if (item.Type != ContentType.Rules)
            {
                counts[item.Type.DirectoryName()]++;
            }
        }

        int errors = diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
        return new ContentSummary(result.Raw.ContentHash, [.. counts], errors, diagnostics.Count - errors);
    }

    /// <summary>Formats the counts as <c>buildings=3 civics=1 ...</c>.</summary>
    /// <returns>The text.</returns>
    public string FormatCounts() =>
        string.Join(' ', Counts.Select(static c => string.Create(CultureInfo.InvariantCulture, $"{c.Key}={c.Value}")));
}
