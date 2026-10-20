using System.Collections.Immutable;
using Evenmere.Content.Loading;

namespace Evenmere.Content;

/// <summary>The outcome of <see cref="ContentLoader.Load(IReadOnlyList{string})"/>: the database if the content is sound, and always the diagnostics.</summary>
public sealed class ContentLoadResult
{
    internal ContentLoadResult(ContentDb? db, RawContent raw, ImmutableArray<ContentDiagnostic> diagnostics)
    {
        Db = db;
        Raw = raw;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the database, or <see langword="null"/> if any error was reported (warnings alone do not stop it).</summary>
    public ContentDb? Db { get; }

    /// <summary>Gets the phase 1 result: the winning files as parsed JSON, and the content hash. It is present even when loading failed.</summary>
    public RawContent Raw { get; }

    /// <summary>Gets every problem found, sorted by code, file, pointer and message.</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }

    /// <summary>Gets a value indicating whether any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Gets the database, or throws if the content had errors. For hosts that cannot continue without content.</summary>
    /// <returns>The database.</returns>
    /// <exception cref="ContentLoadException">The content had errors.</exception>
    public ContentDb GetDbOrThrow() => Db ?? throw new ContentLoadException(Diagnostics);
}

/// <summary>Thrown by <see cref="ContentLoadResult.GetDbOrThrow"/> when content did not load.</summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ContentLoadException"/> class.</summary>
    /// <param name="diagnostics">The problems that stopped loading.</param>
    public ContentLoadException(ImmutableArray<ContentDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the problems that stopped loading.</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }

    private static string BuildMessage(ImmutableArray<ContentDiagnostic> diagnostics)
    {
        int errors = diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
        string first = diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error)?.ToString() ?? "no details";
        return $"Content failed to load with {errors} error(s); first: {first}";
    }
}

/// <summary>
/// Loads content roots into an immutable <see cref="ContentDb"/> in two phases, collecting
/// <see cref="ContentDiagnostic"/>s instead of throwing for bad content.
/// <list type="number">
/// <item><description>
/// <b>Phase 1 (raw).</b> Every file under each root is hashed. Item files <c>&lt;type&gt;/&lt;id&gt;.json</c> and
/// <c>rules/core.json</c> are parsed as strict JSON (a leading BOM, comments, trailing commas and duplicate property
/// names are errors). In a later root, an item with the same type and ID replaces the earlier item whole.
/// </description></item>
/// <item><description>
/// <b>Phase 2 (typed).</b> Per type, string IDs are sorted ordinally and numbered from 0; then each item is read into
/// its definition, with every reference resolved to a typed ID (an unresolved reference is EVL002) and every quantity
/// read from the raw number text and parsed with <c>Amount.Parse</c>.
/// </description></item>
/// </list>
/// <para>
/// <b>Content hash.</b> One XxHash64 stream (<c>StableHasher</c>). For each root in load order it adds the root index
/// (<c>int</c>), then, for each file under the root in ordinal order of its relative path (forward slashes), the relative
/// path (<c>Add(string)</c>), the byte length of the normalised content
/// (<c>Add(long)</c>) and the normalised bytes (<c>Add(ReadOnlySpan&lt;byte&gt;)</c>). Normalising turns CRLF into LF and
/// nothing else. Three kinds of file are left out: dot files and dot directories (<c>.DS_Store</c>, <c>.git</c>), every file
/// under <c>schemas/</c>, and every <c>*.md</c> file. Schemas and the authoring README do not affect gameplay, so editing
/// them does not change the hash that saves compare; everything else, including locale files and maps, is hashed.
/// </para>
/// </summary>
public static class ContentLoader
{
    /// <summary>Loads one content root.</summary>
    /// <param name="root">The content directory, such as <c>content</c>.</param>
    /// <returns>The result.</returns>
    public static ContentLoadResult Load(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Load([root]);
    }

    /// <summary>Loads content roots in order: the base game first, then each content pack, which may add or replace whole items.</summary>
    /// <param name="roots">The content directories, in load order.</param>
    /// <returns>The result.</returns>
    public static ContentLoadResult Load(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var diagnostics = new List<ContentDiagnostic>();
        if (roots.Count == 0)
        {
            diagnostics.Add(new ContentDiagnostic(DiagnosticCodes.Evl001, DiagnosticSeverity.Error, string.Empty, string.Empty, "no content roots were given"));
        }

        RawContent raw = RawContentReader.Read(roots, diagnostics);
        ContentDb? db = roots.Count == 0 ? null : ContentBuilder.Build(raw, diagnostics);

        diagnostics.Sort(ContentDiagnostic.Compare);
        return new ContentLoadResult(db, raw, [.. diagnostics]);
    }
}
