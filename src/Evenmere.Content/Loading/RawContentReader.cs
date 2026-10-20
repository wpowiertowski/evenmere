using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Unicode;
using Evenmere.Core.Hashing;

namespace Evenmere.Content.Loading;

/// <summary>
/// Phase 1 of loading: walks the content roots, hashes every file, picks the winning file for each item
/// (a later root replaces an earlier item with the same type and ID whole) and parses it as JSON. Problems become
/// diagnostics; nothing here throws for bad content.
/// </summary>
internal static class RawContentReader
{
    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

    // Strict JSON: no comments, no trailing commas, no duplicate property names.
    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
        AllowDuplicateProperties = false,
    };

    public static RawContent Read(IReadOnlyList<string> roots, List<ContentDiagnostic> diagnostics)
    {
        var hasher = new StableHasher();

        // Only looked up by key; the winners are sorted explicitly before use, so dictionary order never matters.
        var winners = new Dictionary<(ContentType Type, string Id), Candidate>();

        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            string root = roots[rootIndex];
            hasher.Add(rootIndex);

            if (!Directory.Exists(root))
            {
                diagnostics.Add(Error(string.Empty, string.Empty, $"content root '{root}' does not exist"));
                continue;
            }

            var warnedDirectories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string relativePath in ListFiles(root))
            {
                byte[] raw;
                try
                {
                    raw = File.ReadAllBytes(Path.Combine(root, relativePath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(Error(relativePath, string.Empty, $"cannot read the file: {ex.Message}"));
                    continue;
                }

                byte[] bytes = NormaliseLineEndings(raw);
                hasher.Add(relativePath);
                hasher.Add((long)bytes.Length);
                hasher.Add(bytes);

                Classify(relativePath, diagnostics, warnedDirectories, out ContentType type, out string? id);
                if (id is not null)
                {
                    winners[(type, id)] = new Candidate(rootIndex, relativePath, bytes);
                }
            }
        }

        ImmutableArray<RawItem>.Builder items = ImmutableArray.CreateBuilder<RawItem>(winners.Count);
        foreach (KeyValuePair<(ContentType Type, string Id), Candidate> entry in winners
            .OrderBy(static e => e.Key.Type)
            .ThenBy(static e => e.Key.Id, StringComparer.Ordinal))
        {
            items.Add(Parse(entry.Key.Type, entry.Key.Id, entry.Value, diagnostics));
        }

        return new RawContent([.. roots], items.ToImmutable(), hasher.ToUInt64());
    }

    /// <summary>Turns every CRLF into LF. A lone CR is left alone, and a BOM is never stripped.</summary>
    internal static byte[] NormaliseLineEndings(byte[] raw)
    {
        int crlf = 0;
        for (int i = 0; i + 1 < raw.Length; i++)
        {
            if (raw[i] == (byte)'\r' && raw[i + 1] == (byte)'\n')
            {
                crlf++;
            }
        }

        if (crlf == 0)
        {
            return raw;
        }

        byte[] normalised = new byte[raw.Length - crlf];
        int written = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'\r' && i + 1 < raw.Length && raw[i + 1] == (byte)'\n')
            {
                continue;
            }

            normalised[written++] = raw[i];
        }

        return normalised;
    }

    private static string[] ListFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        var relative = new List<string>();
        foreach (string full in Directory.EnumerateFiles(root, "*", options))
        {
            string path = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');

            if (IsExcludedFromContent(path))
            {
                continue;
            }

            relative.Add(path);
        }

        string[] sorted = [.. relative];
        Array.Sort(sorted, StringComparer.Ordinal);
        return sorted;
    }

    /// <summary>
    /// Tests whether a file is neither content nor part of the content hash. Dot files and directories (.DS_Store, .git,
    /// .gitkeep) would make the hash differ per machine; JSON Schemas and Markdown (the authoring README) do not change
    /// gameplay, and editing them must not change the hash that saves compare.
    /// </summary>
    internal static bool IsExcludedFromContent(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        return segments.Any(static segment => segment.StartsWith('.'))
            || (segments.Length > 1 && segments[0] == "schemas")
            || relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static void Classify(
        string path,
        List<ContentDiagnostic> diagnostics,
        HashSet<string> warnedDirectories,
        out ContentType type,
        out string? id)
    {
        type = default;
        id = null;

        string[] segments = path.Split('/');
        if (segments.Length < 2 || !path.EndsWith(".json", StringComparison.Ordinal))
        {
            return;
        }

        string directory = segments[0];
        if (directory is "locale" or "schemas" or "maps")
        {
            return;
        }

        if (!ContentTypes.TryFromDirectory(directory, out type))
        {
            if (warnedDirectories.Add(directory))
            {
                diagnostics.Add(Warning(path, string.Empty, $"'{directory}' is not a content directory, so its files are ignored"));
            }

            return;
        }

        if (segments.Length != 2)
        {
            diagnostics.Add(Warning(path, string.Empty, "files in subdirectories are ignored; put the file directly in the type's directory"));
            return;
        }

        string stem = segments[1][..^".json".Length];
        if (type == ContentType.Rules)
        {
            if (stem == ContentTypes.RulesId)
            {
                id = stem;
            }
            else
            {
                diagnostics.Add(Warning(path, string.Empty, $"only rules/{ContentTypes.RulesId}.json is read; this file is ignored"));
            }

            return;
        }

        if (!Ids.IsValid(stem))
        {
            diagnostics.Add(Error(path, string.Empty, $"'{stem}' is not a valid ID; use snake_case (lowercase letters, digits and single underscores, starting with a letter)"));
            return;
        }

        id = stem;
    }

    private static RawItem Parse(ContentType type, string id, Candidate candidate, List<ContentDiagnostic> diagnostics)
    {
        string file = candidate.File;
        byte[] bytes = candidate.Bytes;

        if (bytes.AsSpan().StartsWith(_utf8Bom))
        {
            diagnostics.Add(Error(file, string.Empty, "the file starts with a UTF-8 byte order mark; save it as UTF-8 without BOM"));
            return new RawItem(type, id, candidate.RootIndex, file, null);
        }

        // The JSON parser does not check the UTF-8 inside strings, so a corrupt file would load with replacement characters.
        if (!Utf8.IsValid(bytes))
        {
            diagnostics.Add(Error(file, string.Empty, "the file is not valid UTF-8"));
            return new RawItem(type, id, candidate.RootIndex, file, null);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, _jsonOptions);

            // Clone so the element outlives the document and owns its memory.
            JsonElement root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Error(file, string.Empty, $"the file must contain a JSON object, not {ItemReader.Describe(root)}"));
                return new RawItem(type, id, candidate.RootIndex, file, null);
            }

            return new RawItem(type, id, candidate.RootIndex, file, root);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error(file, string.Empty, DescribeJsonError(ex)));
            return new RawItem(type, id, candidate.RootIndex, file, null);
        }
    }

    /// <summary>Formats a syntax error with a one-based line and column when System.Text.Json knows them.</summary>
    internal static string DescribeJsonError(JsonException ex)
    {
        string message = ex.Message;
        int detail = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (detail >= 0)
        {
            message = message[..detail];
        }

        return ex.LineNumber is { } line && ex.BytePositionInLine is { } column
            ? $"invalid JSON at line {line + 1}, column {column + 1}: {message}"
            : $"invalid JSON: {message}";
    }

    private static ContentDiagnostic Error(string file, string pointer, string message) =>
        new(DiagnosticCodes.Evl001, DiagnosticSeverity.Error, file, pointer, message);

    private static ContentDiagnostic Warning(string file, string pointer, string message) =>
        new(DiagnosticCodes.Evl001, DiagnosticSeverity.Warning, file, pointer, message);

    private readonly record struct Candidate(int RootIndex, string File, byte[] Bytes);
}
