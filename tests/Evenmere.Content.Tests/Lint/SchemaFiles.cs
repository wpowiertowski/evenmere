using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>Reads the schema files in <c>content/schemas</c> as the editors see them, straight from disk.</summary>
internal static class SchemaFiles
{
    /// <summary>Gets the schema folder.</summary>
    public static string Directory => Path.Combine(TestPaths.RepoRoot, "content", "schemas");

    /// <summary>Gets the file name of the shared definitions.</summary>
    public const string Common = "common.schema.json";

    /// <summary>Gets the names of every <c>*.schema.json</c> file, in ordinal order.</summary>
    public static ImmutableArray<string> Names =>
        [.. System.IO.Directory.GetFiles(Directory, "*.schema.json").Select(static path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    /// <summary>Gets the file name of the schema of a content type, such as <c>buildings.schema.json</c>.</summary>
    public static string FileOf(ContentType type) => type.DirectoryName() + ".schema.json";

    /// <summary>Reads and parses one schema file.</summary>
    public static JsonElement Read(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, fileName)));
        return document.RootElement.Clone();
    }

    /// <summary>Reads the shared definition <c>$defs/&lt;name&gt;</c> of <c>common.schema.json</c>.</summary>
    public static JsonElement Definition(string name) => Read(Common).GetProperty("$defs").GetProperty(name);

    /// <summary>Visits every JSON object and array in a document, with its pointer-like path.</summary>
    public static IEnumerable<(string Path, JsonElement Node)> Walk(JsonElement root, string path = "")
    {
        yield return (path, root);
        switch (root.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    foreach ((string, JsonElement) inner in Walk(property.Value, path + "/" + property.Name))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in root.EnumerateArray())
                {
                    foreach ((string, JsonElement) inner in Walk(item, path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        yield return inner;
                    }

                    index++;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Gets the strings of a JSON array, in order.</summary>
    public static ImmutableArray<string> Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(static item => item.GetString() ?? string.Empty)];

    /// <summary>Gets the property names of a JSON object, in file order.</summary>
    public static ImmutableArray<string> Keys(JsonElement obj) => [.. obj.EnumerateObject().Select(static property => property.Name)];
}
