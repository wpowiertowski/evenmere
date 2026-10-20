using System.Text.Json;
using Evenmere.Content.Loading;
using Evenmere.Content.Locale;

namespace Evenmere.Content.Lint;

/// <summary>
/// What the rules EVL003 to EVL008 share: the built database, the raw files (to say which file and pointer a problem is in),
/// the English locale, and the list the rules report into.
/// </summary>
internal sealed class LintContext
{
    // Looked up by key only; nothing iterates it, so its order never reaches the output.
    private readonly Dictionary<(ContentType Type, string Id), RawItem> _items = [];
    private readonly List<ContentDiagnostic> _diagnostics = [];

    public LintContext(ContentDb db, RawContent raw, LocaleTable locale)
    {
        Db = db;
        Locale = locale;
        foreach (RawItem item in raw.Items)
        {
            _items[(item.Type, item.Id)] = item;
        }
    }

    public ContentDb Db { get; }

    public LocaleTable Locale { get; }

    public IReadOnlyList<ContentDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Gets the file of an item, relative to its content root.</summary>
    public string FileOf(ContentType type, string id) =>
        _items.TryGetValue((type, id), out RawItem? item) ? item.File : $"{type.DirectoryName()}/{id}.json";

    /// <summary>Gets the parsed JSON of an item, or <see langword="null"/> if it is not known.</summary>
    public JsonElement? JsonOf(ContentType type, string id) =>
        _items.TryGetValue((type, id), out RawItem? item) ? item.Json : null;

    public void Error(string code, string file, string pointer, string message) =>
        _diagnostics.Add(new ContentDiagnostic(code, DiagnosticSeverity.Error, file, pointer, message));

    public void Warning(string code, string file, string pointer, string message) =>
        _diagnostics.Add(new ContentDiagnostic(code, DiagnosticSeverity.Warning, file, pointer, message));

    /// <summary>Builds the pointer to an element of an array property, such as <c>/requires/1</c>.</summary>
    public static string ItemPointer(string property, int index) => JsonPointer.Append(JsonPointer.Append(string.Empty, property), index);

    /// <summary>Builds an RFC 6901 pointer from reference tokens, escaping <c>~</c> and <c>/</c> inside each one.</summary>
    public static string Pointer(params string[] tokens)
    {
        string pointer = string.Empty;
        foreach (string token in tokens)
        {
            pointer = JsonPointer.Append(pointer, token);
        }

        return pointer;
    }
}
