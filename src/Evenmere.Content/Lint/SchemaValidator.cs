using System.Globalization;
using Evenmere.Content.Loading;
using Json.Schema;

namespace Evenmere.Content.Lint;

/// <summary>
/// EVL001: validates every parsed content file against the JSON Schema of its type and turns the failures into
/// diagnostics that carry the same file and RFC 6901 pointer the loader uses, so a failure both report can be merged.
/// </summary>
internal static class SchemaValidator
{
    // Keywords that only combine subschemas. A failure of one of these is explained by the failing subschemas below it,
    // and newer library versions also attach an aggregate message to them, which would repeat the real error one level up.
    private static readonly HashSet<string> _applicators = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "not", "if", "then", "else", "properties", "patternProperties", "additionalProperties",
        "unevaluatedProperties", "items", "prefixItems", "contains", "unevaluatedItems", "dependentSchemas", "propertyNames",
        "$ref", "$dynamicRef",
    };

    private static readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.Hierarchical,
        Culture = CultureInfo.InvariantCulture,
    };

    /// <summary>Validates every item that parsed; items that failed to parse were already reported by the loader.</summary>
    /// <param name="raw">The raw content.</param>
    /// <param name="schemas">The schemas to validate against.</param>
    /// <param name="skipFiles">Files not to validate, such as those the loader already rejected; <see langword="null"/> for none.</param>
    /// <returns>The EVL001 diagnostics, unsorted.</returns>
    public static List<ContentDiagnostic> Validate(RawContent raw, SchemaSet schemas, IReadOnlySet<string>? skipFiles = null)
    {
        var diagnostics = new List<ContentDiagnostic>();
        foreach (RawItem item in raw.Items)
        {
            if (item.Json is not { } json || skipFiles?.Contains(item.File) == true)
            {
                continue;
            }

            EvaluationResults results = schemas.For(item.Type).Evaluate(json, _options);
            Collect(results, item.File, diagnostics);
        }

        return diagnostics;
    }

    private static void Collect(EvaluationResults node, string file, List<ContentDiagnostic> sink)
    {
        if (node.IsValid)
        {
            return;
        }

        string pointer = node.InstanceLocation.ToString();
        string path = node.EvaluationPath.ToString();
        int before = sink.Count;

        if (node.Errors is { Count: > 0 } errors)
        {
            // Sorted so the order never depends on how the library stores its dictionary.
            foreach (string keyword in errors.Keys.Order(StringComparer.Ordinal))
            {
                if (!_applicators.Contains(keyword))
                {
                    sink.Add(Error(file, pointer, Describe(keyword, errors[keyword], pointer)));
                }
            }
        }

        var alternatives = new List<EvaluationResults>();
        if (node.Details is { Count: > 0 } details)
        {
            foreach (EvaluationResults child in details)
            {
                switch (KeywordUnder(path, child.EvaluationPath.ToString()))
                {
                    // The condition of an if/then/else is a test, not a requirement; a failing "if" is not an error.
                    // A failing "not" has no useful detail and is covered by the fallback message below.
                    case "if":
                    case "not":
                        break;
                    case "anyOf":
                    case "oneOf":
                        alternatives.Add(child);
                        break;
                    default:
                        Collect(child, file, sink);
                        break;
                }
            }
        }

        // anyOf/oneOf fail only when every alternative fails; one message then names all of them.
        if (alternatives.Count > 0 && alternatives.TrueForAll(static a => !a.IsValid))
        {
            sink.Add(Error(file, pointer, CombineAlternatives(alternatives, file, pointer)));
        }

        if (sink.Count == before)
        {
            sink.Add(Error(file, pointer, $"the value does not satisfy the schema rule at '{path}'"));
        }
    }

    private static string CombineAlternatives(List<EvaluationResults> alternatives, string file, string pointer)
    {
        var inner = new List<ContentDiagnostic>();
        foreach (EvaluationResults alternative in alternatives)
        {
            Collect(alternative, file, inner);
        }

        var reasons = new List<string>();
        foreach (ContentDiagnostic diagnostic in inner)
        {
            string where = diagnostic.JsonPointer.StartsWith(pointer, StringComparison.Ordinal) && diagnostic.JsonPointer.Length > pointer.Length
                ? diagnostic.JsonPointer[pointer.Length..] + ": "
                : string.Empty;
            string reason = where + diagnostic.Message;
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }

        return "the value matches none of the allowed forms (" + string.Join("; ", reasons) + ")";
    }

    /// <summary>
    /// Names the schema keyword that produced a child result: the first segment of its evaluation path below the parent's.
    /// </summary>
    private static string KeywordUnder(string parentPath, string childPath)
    {
        if (!childPath.StartsWith(parentPath, StringComparison.Ordinal) || childPath.Length <= parentPath.Length + 1)
        {
            return string.Empty;
        }

        string rest = childPath[(parentPath.Length + 1)..];
        int slash = rest.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? rest : rest[..slash];
    }

    private static string Describe(string keyword, string message, string pointer)
    {
        // The false schema ("properties": { "x": false }, "additionalProperties": false) has no keyword.
        if (keyword.Length == 0)
        {
            int slash = pointer.LastIndexOf('/');
            string name = slash < 0 ? pointer : pointer[(slash + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            return $"unknown property '{name}' (not allowed here)";
        }

        return $"{message} [{keyword}]";
    }

    private static ContentDiagnostic Error(string file, string pointer, string message) =>
        new(DiagnosticCodes.Evl001, DiagnosticSeverity.Error, file, pointer, message);
}
