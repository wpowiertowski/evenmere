using System.Globalization;
using System.Text.Json.Nodes;

namespace Evenmere.Content.Tests.Lint;

/// <summary>Edits a JSON document by RFC 6901 pointer, to turn a valid content file into one with a single defect.</summary>
internal static class JsonEdit
{
    /// <summary>Replaces the value at the pointer, or adds it when the last token is a new property.</summary>
    public static string Set(string json, string pointer, string valueJson)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("The document is JSON null.");
        (JsonNode parent, string token) = Locate(root, pointer);
        var value = JsonNode.Parse(valueJson);
        switch (parent)
        {
            case JsonObject obj:
                obj[token] = value;
                break;
            case JsonArray array:
                array[int.Parse(token, CultureInfo.InvariantCulture)] = value;
                break;
            default:
                throw new InvalidOperationException($"{pointer} is not inside an object or an array.");
        }

        return root.ToJsonString();
    }

    /// <summary>Adds a value at the end of the array at the pointer, creating the array when the pointer's last token is a new property.</summary>
    public static string Append(string json, string arrayPointer, string valueJson)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("The document is JSON null.");
        (JsonNode parent, string token) = Locate(root, arrayPointer);
        var value = JsonNode.Parse(valueJson);
        if (parent is not JsonObject obj)
        {
            throw new InvalidOperationException($"{arrayPointer} is not a property of an object.");
        }

        if (obj[token] is JsonArray existing)
        {
            existing.Add(value);
        }
        else
        {
            obj[token] = new JsonArray(value);
        }

        return root.ToJsonString();
    }

    /// <summary>Removes the property or array element at the pointer.</summary>
    public static string Remove(string json, string pointer)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("The document is JSON null.");
        (JsonNode parent, string token) = Locate(root, pointer);
        switch (parent)
        {
            case JsonObject obj:
                obj.Remove(token).ShouldBeTrue($"{pointer} does not exist.");
                break;
            case JsonArray array:
                array.RemoveAt(int.Parse(token, CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException($"{pointer} is not inside an object or an array.");
        }

        return root.ToJsonString();
    }

    private static (JsonNode Parent, string Token) Locate(JsonNode root, string pointer)
    {
        string[] tokens = [.. pointer.Split('/').Skip(1).Select(static t => t.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];
        JsonNode current = root;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            current = current switch
            {
                JsonObject obj => obj[tokens[i]] ?? throw new InvalidOperationException($"{pointer}: no '{tokens[i]}'."),
                JsonArray array => array[int.Parse(tokens[i], CultureInfo.InvariantCulture)] ?? throw new InvalidOperationException($"{pointer}: no '{tokens[i]}'."),
                _ => throw new InvalidOperationException($"{pointer}: cannot step into '{tokens[i]}'."),
            };
        }

        return (current, tokens[^1]);
    }
}
