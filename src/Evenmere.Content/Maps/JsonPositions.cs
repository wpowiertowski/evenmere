using System.Text;
using System.Text.Json;
using Evenmere.Content.Loading;

namespace Evenmere.Content.Maps;

/// <summary>
/// Finds the line and column of every property name and value in a JSON document, by RFC 6901 pointer, because
/// <see cref="JsonDocument"/> forgets where it read things. Only valid JSON is indexed; a document that does not parse
/// has been rejected before this runs.
/// </summary>
internal sealed class JsonPositions
{
    // Lookup only, never iterated.
    private readonly Dictionary<string, (int Line, int Column)> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Line, int Column)> _values = new(StringComparer.Ordinal);

    private JsonPositions()
    {
    }

    /// <summary>Indexes a document.</summary>
    /// <param name="utf8">The document's bytes, without a byte order mark.</param>
    /// <returns>The index.</returns>
    public static JsonPositions Build(byte[] utf8)
    {
        var positions = new JsonPositions();
        List<int> lineStarts = [0];
        for (int i = 0; i < utf8.Length; i++)
        {
            if (utf8[i] == (byte)'\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        var frames = new List<Frame>();
        string pendingProperty = string.Empty;
        while (reader.Read())
        {
            JsonTokenType token = reader.TokenType;
            if (token is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                frames.RemoveAt(frames.Count - 1);
                continue;
            }

            (int line, int column) = Locate(utf8, lineStarts, reader.TokenStartIndex);
            if (token == JsonTokenType.PropertyName)
            {
                pendingProperty = JsonPointer.Append(frames[^1].Pointer, reader.GetString() ?? string.Empty);
                positions._keys[pendingProperty] = (line, column);
                continue;
            }

            string pointer;
            if (frames.Count == 0)
            {
                pointer = string.Empty;
            }
            else if (frames[^1].IsArray)
            {
                pointer = JsonPointer.Append(frames[^1].Pointer, frames[^1].NextIndex++);
            }
            else
            {
                pointer = pendingProperty;
            }

            positions._values[pointer] = (line, column);
            if (token is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                frames.Add(new Frame(pointer, token == JsonTokenType.StartArray));
            }
        }

        return positions;
    }

    /// <summary>Gets the position of a property name.</summary>
    /// <param name="pointer">The pointer of the property.</param>
    /// <returns>The 1-based line and column, or (0, 0) if the pointer is unknown.</returns>
    public (int Line, int Column) KeyOf(string pointer) => _keys.TryGetValue(pointer, out (int, int) at) ? at : (0, 0);

    /// <summary>Gets the position where a value starts.</summary>
    /// <param name="pointer">The pointer of the value.</param>
    /// <returns>The 1-based line and column, or (0, 0) if the pointer is unknown.</returns>
    public (int Line, int Column) ValueOf(string pointer) => _values.TryGetValue(pointer, out (int, int) at) ? at : (0, 0);

    private static (int Line, int Column) Locate(byte[] utf8, List<int> lineStarts, long offset)
    {
        int index = lineStarts.BinarySearch((int)offset);
        int line = index >= 0 ? index : ~index - 1;
        int start = lineStarts[line];

        // The column counts characters, not bytes, so a multi-byte character in a string does not shift it.
        int column = Encoding.UTF8.GetCharCount(utf8, start, (int)offset - start) + 1;
        return (line + 1, column);
    }

    private sealed class Frame(string pointer, bool isArray)
    {
        public string Pointer { get; } = pointer;

        public bool IsArray { get; } = isArray;

        public int NextIndex { get; set; }
    }
}
