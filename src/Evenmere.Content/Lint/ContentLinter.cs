using System.Collections.Immutable;
using Evenmere.Content.Lint.Rules;
using Evenmere.Content.Loading;
using Evenmere.Content.Locale;

namespace Evenmere.Content.Lint;

/// <summary>
/// The content linter (ADR-0004): everything the loader reports, plus a JSON Schema check of every file (EVL001) and the
/// rules that need the whole content set (EVL003 to EVL008 and EVL011). It never throws for bad content; it returns diagnostics.
/// <list type="table">
/// <listheader><term>Code</term><description>Rule</description></listheader>
/// <item><term>EVL001</term><description>Shape: the loader's errors, and every file against its JSON Schema.</description></item>
/// <item><term>EVL002</term><description>Every reference resolves (reported by the loader).</description></item>
/// <item><term>EVL003</term><description>Tech and civic trees are acyclic and every node is reachable from a root.</description></item>
/// <item><term>EVL004</term><description>Every good has a producer and a consumer.</description></item>
/// <item><term>EVL005</term><description>Every building, tier, tradition and festival (and unreferenced recipe) has exactly one unlock source.</description></item>
/// <item><term>EVL006</term><description>Every tier's basic needs can be met with items of its era or earlier.</description></item>
/// <item><term>EVL007</term><description>Every item has a display name (error) and description (warning) in <c>locale/en</c>.</description></item>
/// <item><term>EVL008</term><description>Era gateways exist, and no node requires a node of a later era.</description></item>
/// <item><term>EVL011</term><description>A tier with an advanceFrom tier needs strictly more basic goods per resident than that tier.</description></item>
/// </list>
/// The rules EVL003 to EVL008 and EVL011 read the typed <see cref="ContentDb"/>, so they run only when loading produced one. Shape
/// errors, unresolved references and locale file problems are reported either way.
/// </summary>
public static class ContentLinter
{
    private const string Language = "en";

    /// <summary>
    /// Lints the result of <see cref="ContentLoader.Load(IReadOnlyList{string})"/>: the loader's diagnostics, the schema
    /// diagnostics, the diagnostics of the English locale files, and (when a database was built) the rule diagnostics.
    /// A file for which the loader reported an EVL001 or EVL002 error is not checked against its schema again: the loader's
    /// checks are at least as strict, so the file already fails and one defect is reported once, with the loader's message.
    /// Files the loader accepted are validated in full.
    /// </summary>
    /// <param name="result">The load result.</param>
    /// <returns>Every diagnostic, sorted with <see cref="ContentDiagnostic.Compare"/>, without exact duplicates.</returns>
    public static ImmutableArray<ContentDiagnostic> Run(ContentLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Lint(result.Db, result.Raw, result.Diagnostics);
    }

    /// <summary>
    /// Lints content that already loaded cleanly: the schema diagnostics, the diagnostics of the English locale files and the
    /// rule diagnostics. The loader's own diagnostics are not repeated; use <see cref="Run(ContentLoadResult)"/> for those.
    /// </summary>
    /// <param name="db">The database built from <paramref name="raw"/>.</param>
    /// <param name="raw">The raw files the database was built from.</param>
    /// <returns>Every diagnostic, sorted with <see cref="ContentDiagnostic.Compare"/>, without exact duplicates.</returns>
    public static ImmutableArray<ContentDiagnostic> Run(ContentDb db, RawContent raw)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(raw);
        return Lint(db, raw, []);
    }

    /// <summary>
    /// Checks every parsed file against the JSON Schema of its type (EVL001) and nothing else. Files that did not parse
    /// are skipped, since the loader already reported them.
    /// </summary>
    /// <param name="raw">The raw files.</param>
    /// <returns>The schema diagnostics, sorted with <see cref="ContentDiagnostic.Compare"/>.</returns>
    public static ImmutableArray<ContentDiagnostic> ValidateSchemas(RawContent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        List<ContentDiagnostic> diagnostics = SchemaValidator.Validate(raw, SchemaSet.Default);
        diagnostics.Sort(ContentDiagnostic.Compare);
        return [.. diagnostics];
    }

    private static ImmutableArray<ContentDiagnostic> Lint(ContentDb? db, RawContent raw, ImmutableArray<ContentDiagnostic> loaderDiagnostics)
    {
        var all = new List<ContentDiagnostic>(loaderDiagnostics);

        // The loader's checks are at least as strict as the schemas', so a file it rejected already fails and the schema would
        // only repeat the same defect at a slightly different spot. Files it accepted are still validated in full.
        // Looked up only; the output is sorted below.
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentDiagnostic diagnostic in loaderDiagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Code is DiagnosticCodes.Evl001 or DiagnosticCodes.Evl002)
            {
                rejected.Add(diagnostic.File);
            }
        }

        all.AddRange(SchemaValidator.Validate(raw, SchemaSet.Default, rejected));

        LocaleLoadResult locale = LocaleTable.Load(raw.Roots, Language);
        all.AddRange(locale.Diagnostics);

        if (db is not null)
        {
            var context = new LintContext(db, raw, locale.Table);
            var unlocks = UnlockIndex.Build(context);

            TreeRule.Run(context);
            GoodFlowRule.Run(context);
            UnlockRule.Run(context, unlocks);
            TierNeedsRule.Run(context, unlocks);
            LocaleRule.Run(context, raw);
            EraRule.Run(context);
            TierConsumptionRule.Run(context);
            all.AddRange(context.Diagnostics);
        }

        all.Sort(ContentDiagnostic.Compare);
        return [.. RemoveDuplicates(all)];
    }

    private static IEnumerable<ContentDiagnostic> RemoveDuplicates(List<ContentDiagnostic> sorted)
    {
        ContentDiagnostic? previous = null;
        foreach (ContentDiagnostic diagnostic in sorted)
        {
            if (previous is null || ContentDiagnostic.Compare(previous, diagnostic) != 0 || previous.Severity != diagnostic.Severity)
            {
                yield return diagnostic;
            }

            previous = diagnostic;
        }
    }
}
