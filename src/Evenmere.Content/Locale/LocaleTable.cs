using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Unicode;
using Evenmere.Content.Loading;

namespace Evenmere.Content.Locale;

/// <summary>The outcome of loading a language: the merged table and any problems found.</summary>
/// <param name="Table">The merged table. It holds every readable entry even when diagnostics exist.</param>
/// <param name="Diagnostics">The problems found, sorted by code, file, pointer and message.</param>
public sealed record LocaleLoadResult(LocaleTable Table, ImmutableArray<ContentDiagnostic> Diagnostics)
{
    /// <summary>Gets a value indicating whether any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Display text for one language, merged from every <c>locale/&lt;lang&gt;/*.json</c> file. A file is a flat JSON object of
/// key to text, where the key is like <c>building.bakery.name</c>; a value is a string, or an array of strings for name
/// tables (great works, natural wonders). The same key twice in one root is an error; in a later root it replaces
/// the earlier text, like any other content.
/// </summary>
public sealed class LocaleTable
{
    private readonly Dictionary<string, Entry> _entries;

    private LocaleTable(string language, Dictionary<string, Entry> entries)
    {
        Language = language;
        _entries = entries;
        Keys = [.. entries.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>Gets the language code, such as <c>en</c>.</summary>
    public string Language { get; }

    /// <summary>Gets every key, in ordinal order.</summary>
    public ImmutableArray<string> Keys { get; }

    /// <summary>Gets the number of keys.</summary>
    public int Count => _entries.Count;

    /// <summary>Loads the locale files of one language from one content root.</summary>
    /// <param name="root">The content root.</param>
    /// <param name="language">The language code, such as <c>en</c>.</param>
    /// <returns>The table and its diagnostics.</returns>
    public static LocaleLoadResult Load(string root, string language)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Load([root], language);
    }

    /// <summary>Loads and merges the locale files of one language from several content roots, in load order.</summary>
    /// <param name="roots">The content roots; a key in a later root replaces the same key from an earlier one.</param>
    /// <param name="language">The language code, such as <c>en</c>.</param>
    /// <returns>The table and its diagnostics.</returns>
    public static LocaleLoadResult Load(IReadOnlyList<string> roots, string language)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(language);
        if (language.Length == 0 || !language.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException($"'{language}' is not a language code such as 'en' or 'pt-BR'.", nameof(language));
        }

        var diagnostics = new List<ContentDiagnostic>();

        // Looked up by key only; the key list is sorted explicitly, so dictionary order never leaks.
        var merged = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (string root in roots)
        {
            string directory = Path.Combine(root, "locale", language);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string[] names = [.. Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(static path => Path.GetFileName(path))
                .Where(static name => !name.StartsWith('.'))];
            Array.Sort(names, StringComparer.Ordinal);

            // Keys seen in this root, to tell a duplicate within a root from an override by a later root.
            var seenInRoot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                string relative = $"locale/{language}/{name}";
                ReadFile(Path.Combine(directory, name), relative, merged, seenInRoot, diagnostics);
            }
        }

        diagnostics.Sort(ContentDiagnostic.Compare);
        return new LocaleLoadResult(new LocaleTable(language, merged), [.. diagnostics]);
    }

    /// <summary>Gets the text for a key.</summary>
    /// <param name="key">The locale key.</param>
    /// <returns>The text, or the key itself when it has no text (or holds only a name list), so a missing string is visible and never fatal.</returns>
    public string Get(string key) => TryGet(key, out string text) ? text : key;

    /// <summary>Finds the text for a key.</summary>
    /// <param name="key">The locale key.</param>
    /// <param name="text">The text, when the key holds a single string.</param>
    /// <returns><see langword="true"/> if the key holds a single string.</returns>
    public bool TryGet(string key, out string text)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_entries.TryGetValue(key, out Entry? entry) && entry.Text is not null)
        {
            text = entry.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>Finds a name table: a key whose value is an array of strings.</summary>
    /// <param name="key">The locale key.</param>
    /// <param name="names">The names, in file order, when the key holds a list.</param>
    /// <returns><see langword="true"/> if the key holds a list.</returns>
    public bool TryGetList(string key, out ImmutableArray<string> names)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_entries.TryGetValue(key, out Entry? entry) && entry.List is { } list)
        {
            names = list;
            return true;
        }

        names = [];
        return false;
    }

    /// <summary>Tests whether a key has any text or list.</summary>
    /// <param name="key">The locale key.</param>
    /// <returns><see langword="true"/> if the key is present.</returns>
    public bool ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.ContainsKey(key);
    }

    private static void ReadFile(
        string fullPath,
        string relative,
        Dictionary<string, Entry> merged,
        Dictionary<string, string> seenInRoot,
        List<ContentDiagnostic> diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(relative, string.Empty, $"cannot read the file: {ex.Message}"));
            return;
        }

        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            diagnostics.Add(Error(relative, string.Empty, "the file starts with a UTF-8 byte order mark; save it as UTF-8 without BOM"));
            return;
        }

        if (!Utf8.IsValid(bytes))
        {
            diagnostics.Add(Error(relative, string.Empty, "the file is not valid UTF-8"));
            return;
        }

        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
            AllowDuplicateProperties = false,
        };

        try
        {
            using var document = JsonDocument.Parse(bytes, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Error(relative, string.Empty, "a locale file must be a JSON object of key to text"));
                return;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                AddEntry(property, relative, merged, seenInRoot, diagnostics);
            }
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error(relative, string.Empty, RawContentReader.DescribeJsonError(ex)));
        }
    }

    private static void AddEntry(
        JsonProperty property,
        string relative,
        Dictionary<string, Entry> merged,
        Dictionary<string, string> seenInRoot,
        List<ContentDiagnostic> diagnostics)
    {
        string key = property.Name;
        string pointer = JsonPointer.Append(string.Empty, key);

        if (key.Length == 0)
        {
            diagnostics.Add(Error(relative, pointer, "a locale key must not be empty"));
            return;
        }

        Entry? entry = ReadValue(property.Value, relative, pointer, diagnostics);
        if (entry is null)
        {
            return;
        }

        if (seenInRoot.TryGetValue(key, out string? firstFile))
        {
            diagnostics.Add(Error(relative, pointer, $"duplicate locale key '{key}' (first defined in {firstFile})"));
            return;
        }

        seenInRoot.Add(key, relative);
        merged[key] = entry;
    }

    private static Entry? ReadValue(JsonElement value, string relative, string pointer, List<ContentDiagnostic> diagnostics)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return new Entry(value.GetString() ?? string.Empty, null);
            case JsonValueKind.Array:
                ImmutableArray<string>.Builder list = ImmutableArray.CreateBuilder<string>();
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        diagnostics.Add(Error(relative, JsonPointer.Append(pointer, index), "a name list may contain only strings"));
                        return null;
                    }

                    list.Add(item.GetString() ?? string.Empty);
                    index++;
                }

                return new Entry(null, list.ToImmutable());
            default:
                diagnostics.Add(Error(relative, pointer, "a locale value must be a string, or an array of strings for a name list"));
                return null;
        }
    }

    private static ContentDiagnostic Error(string file, string pointer, string message) =>
        new(DiagnosticCodes.Evl001, DiagnosticSeverity.Error, file, pointer, message);

    /// <summary>One key's value: a single string, or a list of strings.</summary>
    private sealed record Entry(string? Text, ImmutableArray<string>? List);
}
