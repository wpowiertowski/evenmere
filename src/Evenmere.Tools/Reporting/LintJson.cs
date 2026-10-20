using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Evenmere.Content;

namespace Evenmere.Tools.Reporting;

/// <summary>
/// The JSON form of a content report, written by <c>evenmere lint --format json</c>. The shape is a contract for scripts:
/// <code>
/// {
///   "contentHash": "6114913bc26ea39a",
///   "counts": { "buildings": 3, "civics": 0, "eras": 1, ... },
///   "diagnostics": [
///     { "code": "EVL003", "severity": "error", "file": "techs/a.json", "pointer": "/requires/0", "message": "..." }
///   ]
/// }
/// </code>
/// <list type="bullet">
/// <item><description><c>contentHash</c> is a string of 16 lowercase hex digits (a JSON number cannot hold 64 bits exactly).</description></item>
/// <item><description><c>counts</c> has one key per content type (the directory name), in ordinal key order, with 0 for an empty type; the tunables file is not counted.</description></item>
/// <item><description><c>diagnostics</c> keeps the linter's order (by code, file, pointer, message). <c>severity</c> is <c>"error"</c> or <c>"warning"</c>; <c>file</c> and <c>pointer</c> are empty strings when no single file or value is to blame.</description></item>
/// </list>
/// The output is indented with two spaces and ends with one newline, so identical content gives byte-identical output.
/// </summary>
internal static class LintJson
{
    /// <summary>Writes the report.</summary>
    /// <param name="summary">The content hash and item counts.</param>
    /// <param name="diagnostics">The diagnostics, already sorted.</param>
    /// <param name="writer">Where to write.</param>
    public static void Write(ContentSummary summary, IEnumerable<ContentDiagnostic> diagnostics, TextWriter writer)
    {
        using var buffer = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",

            // The diagnostics quote content text; keep apostrophes and non-ASCII letters readable instead of ' escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (var json = new Utf8JsonWriter(buffer, options))
        {
            json.WriteStartObject();
            json.WriteString("contentHash", summary.ContentHashText);

            json.WriteStartObject("counts");
            foreach (KeyValuePair<string, int> count in summary.Counts)
            {
                json.WriteNumber(count.Key, count.Value);
            }

            json.WriteEndObject();

            json.WriteStartArray("diagnostics");
            foreach (ContentDiagnostic diagnostic in diagnostics)
            {
                json.WriteStartObject();
                json.WriteString("code", diagnostic.Code);
                json.WriteString("severity", diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning");
                json.WriteString("file", diagnostic.File);
                json.WriteString("pointer", diagnostic.JsonPointer);
                json.WriteString("message", diagnostic.Message);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        writer.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        writer.Write('\n');
    }
}
