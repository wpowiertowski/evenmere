using System.Reflection;
using Json.Schema;

namespace Evenmere.Content.Lint;

/// <summary>
/// The JSON Schemas (draft 2020-12) of every content type, built once from the copies of <c>content/schemas</c> that are
/// compiled into this assembly. Per-type schemas reference the shared definitions in <c>common.schema.json</c> through
/// <c>$ref</c>, so the common file is registered first.
/// </summary>
internal sealed class SchemaSet
{
    private const string ResourcePrefix = "Evenmere.Content.Schemas.";
    private const string CommonName = "common";

    private static readonly Lazy<SchemaSet> _default = new(Load);

    private readonly JsonSchema[] _byType;

    private SchemaSet(JsonSchema[] byType)
    {
        _byType = byType;
    }

    /// <summary>Gets the schemas compiled into this assembly.</summary>
    public static SchemaSet Default => _default.Value;

    /// <summary>Gets the schema that every file of a content type must satisfy.</summary>
    /// <param name="type">The content type.</param>
    /// <returns>The built schema.</returns>
    public JsonSchema For(ContentType type) => _byType[(int)type];

    private static SchemaSet Load()
    {
        Assembly assembly = typeof(SchemaSet).Assembly;

        // A private registry, so nothing leaks into (or depends on) the library's global one.
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };

        // $refs resolve while a schema is built, so the shared definitions must already be registered.
        JsonSchema.FromText(ReadResource(assembly, CommonName), options);

        var byType = new JsonSchema[ContentTypes.All.Length];
        foreach (ContentType type in ContentTypes.All)
        {
            byType[(int)type] = JsonSchema.FromText(ReadResource(assembly, type.DirectoryName()), options);
        }

        return new SchemaSet(byType);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        string resource = $"{ResourcePrefix}{name}.schema.json";
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The schema '{resource}' is not compiled into {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
