using System.Text;
using System.Text.Json;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// Rewrites the <c>expect.hash</c> value of a scenario file in place. The file is edited as bytes, not re-serialised,
/// so everything else (the description's text, property order, the author's line breaks and spacing) stays exactly as it
/// was. When there is no <c>hash</c> yet, one is added in the file's own style: the lines it has to create are indented
/// by the same step the file uses for its top-level properties, which is two spaces in a file written to the repo's
/// editorconfig.
/// </summary>
internal static class GoldenFile
{
    private const string Step = "  ";
    private static readonly byte[] _bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Writes a hash into a scenario file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="hash">The state hash.</param>
    public static void Write(string path, ulong hash) =>
        File.WriteAllBytes(path, WithHash(File.ReadAllBytes(path), HashText.Format(hash)));

    /// <summary>Returns the file's bytes with <c>expect.hash</c> set to a value.</summary>
    /// <param name="json">The file's UTF-8 bytes; it must be a valid scenario.</param>
    /// <param name="hashHex">The 16 hex characters to write.</param>
    /// <returns>The edited bytes.</returns>
    public static byte[] WithHash(byte[] json, string hashHex)
    {
        Layout layout = Scan(json);
        string eol = Contains(json, "\r\n"u8) ? "\r\n" : "\n";
        string literal = $"\"{hashHex}\"";

        if (layout.HashStart >= 0)
        {
            return Splice(json, layout.HashStart, layout.HashEnd, literal);
        }

        // One indentation step is what the file already uses for its top-level properties; two spaces when it has none to copy.
        string rootIndent = LineIndent(json, layout.FirstRootPropertyStart) ?? string.Empty;
        string step = rootIndent.Length > 0 ? rootIndent : Step;

        if (layout.ExpectNullStart >= 0)
        {
            // "expect": null reads as no expectations, so give it a block instead of adding a second "expect".
            string indent = LineIndent(json, layout.ExpectNameStart) ?? string.Empty;
            return Splice(json, layout.ExpectNullStart, layout.ExpectNullEnd, $"{{{eol}{indent}{step}\"hash\": {literal}{eol}{indent}}}");
        }

        if (layout.ExpectOpen >= 0 && layout.ExpectFirstPropertyStart >= 0)
        {
            // Put "hash" first, reusing whatever whitespace the author put after the "{" so one-line and multi-line blocks both look right.
            int afterOpen = layout.ExpectOpen + 1;
            string lead = Encoding.UTF8.GetString(json, afterOpen, layout.ExpectFirstPropertyStart - afterOpen);
            return Splice(json, afterOpen, afterOpen, $"{lead}\"hash\": {literal},");
        }

        if (layout.ExpectOpen >= 0)
        {
            string indent = LineIndent(json, layout.ExpectNameStart) ?? string.Empty;
            return Splice(json, layout.ExpectOpen, layout.ExpectClose + 1, $"{{{eol}{indent}{step}\"hash\": {literal}{eol}{indent}}}");
        }

        string block = $",{eol}{step}\"expect\": {{{eol}{step}{step}\"hash\": {literal}{eol}{step}}}";
        return Splice(json, layout.LastRootValueEnd, layout.LastRootValueEnd, block);
    }

    private static Layout Scan(byte[] json)
    {
        int offset = json.AsSpan().StartsWith(_bom) ? _bom.Length : 0;
        var reader = new Utf8JsonReader(json.AsSpan(offset));
        var layout = new Layout();
        bool inExpect = false;
        bool atExpect = false;

        while (reader.Read())
        {
            int depth = reader.CurrentDepth;
            JsonTokenType token = reader.TokenType;
            int start = offset + (int)reader.TokenStartIndex;

            if (token == JsonTokenType.PropertyName && depth == 1)
            {
                layout.FirstRootPropertyStart = layout.FirstRootPropertyStart < 0 ? start : layout.FirstRootPropertyStart;
                atExpect = reader.ValueTextEquals("expect"u8);
                if (atExpect)
                {
                    layout.ExpectNameStart = start;
                }
            }
            else if (token == JsonTokenType.Null && depth == 1 && atExpect)
            {
                layout.ExpectNullStart = start;
                layout.ExpectNullEnd = offset + (int)reader.BytesConsumed;
            }
            else if (token == JsonTokenType.StartObject && depth == 1 && atExpect)
            {
                layout.ExpectOpen = start;
                inExpect = true;
            }
            else if (token == JsonTokenType.EndObject && depth == 1 && inExpect)
            {
                layout.ExpectClose = start;
                inExpect = false;
            }
            else if (token == JsonTokenType.PropertyName && depth == 2 && inExpect)
            {
                layout.ExpectFirstPropertyStart = layout.ExpectFirstPropertyStart < 0 ? start : layout.ExpectFirstPropertyStart;
                if (reader.ValueTextEquals("hash"u8))
                {
                    reader.Read();
                    if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                    {
                        throw new ScenarioException("expect.hash must be a string or null.");
                    }

                    layout.HashStart = offset + (int)reader.TokenStartIndex;
                    layout.HashEnd = offset + (int)reader.BytesConsumed;
                    continue;
                }
            }

            // The end of the last complete value directly under the root is where a new root property goes.
            if (depth == 1 && token is not (JsonTokenType.PropertyName or JsonTokenType.StartObject or JsonTokenType.StartArray))
            {
                layout.LastRootValueEnd = offset + (int)reader.BytesConsumed;
            }
        }

        return layout;
    }

    // The whitespace before a token, but only when the token is the first thing on its line.
    private static string? LineIndent(byte[] json, int tokenStart)
    {
        int begin = tokenStart;
        while (begin > 0 && json[begin - 1] is (byte)' ' or (byte)'\t')
        {
            begin--;
        }

        bool startsLine = begin == 0 || json[begin - 1] == (byte)'\n';
        return startsLine ? Encoding.UTF8.GetString(json, begin, tokenStart - begin) : null;
    }

    private static byte[] Splice(byte[] json, int start, int end, string replacement)
    {
        byte[] inserted = Encoding.UTF8.GetBytes(replacement);
        byte[] result = new byte[json.Length - (end - start) + inserted.Length];
        json.AsSpan(0, start).CopyTo(result);
        inserted.CopyTo(result, start);
        json.AsSpan(end).CopyTo(result.AsSpan(start + inserted.Length));
        return result;
    }

    private static bool Contains(byte[] json, ReadOnlySpan<byte> needle) => json.AsSpan().IndexOf(needle) >= 0;

    private sealed class Layout
    {
        public int FirstRootPropertyStart { get; set; } = -1;

        public int LastRootValueEnd { get; set; } = -1;

        public int ExpectNameStart { get; set; } = -1;

        public int ExpectNullStart { get; set; } = -1;

        public int ExpectNullEnd { get; set; } = -1;

        public int ExpectOpen { get; set; } = -1;

        public int ExpectClose { get; set; } = -1;

        public int ExpectFirstPropertyStart { get; set; } = -1;

        public int HashStart { get; set; } = -1;

        public int HashEnd { get; set; } = -1;
    }
}
