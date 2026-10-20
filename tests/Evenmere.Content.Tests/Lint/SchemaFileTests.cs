using System.Collections.Immutable;
using System.Text.Json;
using Json.Schema;

namespace Evenmere.Content.Tests.Lint;

/// <summary>Checks the schema files themselves: that they exist, are valid JSON Schema, and follow the conventions.</summary>
public sealed class SchemaFileTests
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private const string IdPattern = "^[a-z][a-z0-9]*(_[a-z0-9]+)*$";

    public static TheoryData<string> SchemaFileNames => [.. SchemaFiles.Names];

    public static TheoryData<string> TypeSchemaFileNames =>
        [.. SchemaFiles.Names.Where(static name => name != SchemaFiles.Common)];

    [Fact]
    public void Schemas_ThereIsOneForEveryContentTypePlusTheSharedDefinitions()
    {
        string[] expected = [.. ContentTypes.All.Select(SchemaFiles.FileOf), SchemaFiles.Common];
        Array.Sort(expected, StringComparer.Ordinal);

        SchemaFiles.Names.ShouldBe(expected);
        ContentTypes.All.Length.ShouldBe(14, "13 content types and the rules file.");
    }

    [Theory]
    [MemberData(nameof(SchemaFileNames))]
    public void Schema_IsValidAgainstTheDraft202012MetaSchema(string fileName)
    {
        var meta = JsonSchema.FromText($$"""{ "$ref": "{{Draft202012}}" }""", new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        JsonElement schema = SchemaFiles.Read(fileName);

        EvaluationResults results = meta.Evaluate(schema);

        results.IsValid.ShouldBeTrue($"{fileName} is not a valid draft 2020-12 schema.");
    }

    [Theory]
    [MemberData(nameof(SchemaFileNames))]
    public void Schema_DeclaresDraft202012AndTheIdMatchingItsFileName(string fileName)
    {
        JsonElement schema = SchemaFiles.Read(fileName);

        schema.GetProperty("$schema").GetString().ShouldBe(Draft202012);
        schema.GetProperty("$id").GetString().ShouldBe("https://evenmere.example/schemas/" + fileName);
        schema.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(SchemaFileNames))]
    public void Schema_EveryObjectSchemaWithPropertiesForbidsOthers(string fileName)
    {
        // A typo in a content file must never be silently ignored (ADR-0004), so no object shape may be open.
        JsonElement schema = SchemaFiles.Read(fileName);

        foreach ((string path, JsonElement node) in SchemaFiles.Walk(schema))
        {
            if (node.ValueKind != JsonValueKind.Object
                || !node.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object"
                || !node.TryGetProperty("properties", out _))
            {
                continue;
            }

            node.TryGetProperty("additionalProperties", out JsonElement additional).ShouldBeTrue($"{fileName}#{path} lists properties but allows others.");
            additional.ValueKind.ShouldBe(JsonValueKind.False, $"{fileName}#{path} must set additionalProperties to false.");
        }
    }

    [Theory]
    [MemberData(nameof(TypeSchemaFileNames))]
    public void Schema_EveryTypeSchemaIsAnObjectSchemaWithClosedProperties(string fileName)
    {
        JsonElement schema = SchemaFiles.Read(fileName);

        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("additionalProperties").ValueKind.ShouldBe(JsonValueKind.False);
        schema.GetProperty("required").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(TypeSchemaFileNames))]
    public void Schema_EveryItemTypeRequiresASnakeCaseId_AndTheRulesFileHasNone(string fileName)
    {
        JsonElement schema = SchemaFiles.Read(fileName);
        JsonElement properties = schema.GetProperty("properties");
        string[] required = [.. SchemaFiles.Strings(schema.GetProperty("required"))];

        if (fileName == SchemaFiles.FileOf(ContentType.Rules))
        {
            properties.TryGetProperty("id", out _).ShouldBeFalse("content/rules/core.json has no id.");
            required.ShouldNotContain("id");
            return;
        }

        required.ShouldContain("id");
        JsonElement id = properties.GetProperty("id");
        if (id.TryGetProperty("$ref", out JsonElement reference))
        {
            reference.GetString().ShouldBe("common.schema.json#/$defs/id");
        }
        else
        {
            // Goods exclude the reserved ID "coin" with a lookahead and otherwise use the same grammar.
            fileName.ShouldBe(SchemaFiles.FileOf(ContentType.Goods));
            id.GetProperty("pattern").GetString().ShouldBe("^(?!coin$)" + IdPattern[1..]);
            id.GetProperty("maxLength").GetInt32().ShouldBe(64);
        }
    }

    [Fact]
    public void Schema_TheSharedIdIsTheSnakeCaseGrammarOfTheLoader()
    {
        JsonElement id = SchemaFiles.Definition("id");

        id.GetProperty("pattern").GetString().ShouldBe(IdPattern);
        id.GetProperty("maxLength").GetInt32().ShouldBe(64);
    }

    [Fact]
    public void Schema_QuantitiesAreMultiplesOfOneThousandth()
    {
        JsonElement positive = SchemaFiles.Definition("quantity");
        JsonElement nonNegative = SchemaFiles.Definition("quantityOrZero");

        positive.GetProperty("type").GetString().ShouldBe("number");
        positive.GetProperty("exclusiveMinimum").GetInt32().ShouldBe(0);
        positive.GetProperty("multipleOf").GetDecimal().ShouldBe(0.001m);
        nonNegative.GetProperty("minimum").GetInt32().ShouldBe(0);
        nonNegative.GetProperty("multipleOf").GetDecimal().ShouldBe(0.001m);
    }

    [Fact]
    public void Schema_EveryReferenceResolvesToADefinition()
    {
        ImmutableArray<string> files = SchemaFiles.Names;
        int checkedReferences = 0;

        foreach (string fileName in files)
        {
            foreach ((string path, JsonElement node) in SchemaFiles.Walk(SchemaFiles.Read(fileName)))
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("$ref", out JsonElement reference) || reference.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string text = reference.GetString()!;
                int hash = text.IndexOf('#', StringComparison.Ordinal);
                string target = hash <= 0 ? fileName : text[..hash];
                string fragment = hash < 0 ? string.Empty : text[(hash + 1)..];

                files.ShouldContain(target, $"{fileName}#{path} refers to {text}, which is not a schema file.");
                JsonElement resolved = SchemaFiles.Read(target);
                foreach (string token in fragment.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    resolved.TryGetProperty(token, out resolved).ShouldBeTrue($"{fileName}#{path} refers to {text}, which does not exist.");
                }

                checkedReferences++;
            }
        }

        checkedReferences.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void Schema_NoCommonDefinitionIsDeadWeight()
    {
        // A definition nobody uses is a vocabulary that can drift unnoticed.
        JsonElement common = SchemaFiles.Read(SchemaFiles.Common);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string fileName in SchemaFiles.Names)
        {
            foreach ((_, JsonElement node) in SchemaFiles.Walk(SchemaFiles.Read(fileName)))
            {
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("$ref", out JsonElement reference) && reference.ValueKind == JsonValueKind.String)
                {
                    string text = reference.GetString()!;
                    used.Add(text[(text.LastIndexOf('/') + 1)..]);
                }
            }
        }

        foreach (string name in SchemaFiles.Keys(common.GetProperty("$defs")))
        {
            used.ShouldContain(name, $"common.schema.json defines '{name}' but nothing refers to it.");
        }
    }
}
