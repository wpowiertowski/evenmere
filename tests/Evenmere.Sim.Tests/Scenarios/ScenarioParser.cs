using System.Globalization;
using System.Text;
using System.Text.Json;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// Reads a scenario file into a <see cref="ScenarioDefinition"/>. Reading is strict, like the content loader: no
/// comments, no trailing commas, no duplicate properties and no unknown properties, so a typo such as <c>"tick"</c> for
/// <c>"ticks"</c> is an error rather than a silently ignored setting. Every error is a <see cref="ScenarioException"/>
/// that starts with the file and the property it is about.
/// </summary>
internal static class ScenarioParser
{
    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private static readonly JsonElement _noArgs = ParseNoArgs();

    /// <summary>Reads a scenario file from disk.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The scenario.</returns>
    /// <exception cref="ScenarioException">The file is not a valid scenario.</exception>
    public static ScenarioDefinition ParseFile(string path) => Parse(File.ReadAllBytes(path), path);

    /// <summary>Reads scenario text.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="sourcePath">The path to report and to rewrite on a golden update.</param>
    /// <returns>The scenario.</returns>
    /// <exception cref="ScenarioException">The text is not a valid scenario.</exception>
    public static ScenarioDefinition Parse(string json, string sourcePath) => Parse(Encoding.UTF8.GetBytes(json), sourcePath);

    /// <summary>Reads scenario bytes.</summary>
    /// <param name="utf8">The UTF-8 JSON.</param>
    /// <param name="sourcePath">The path to report and to rewrite on a golden update.</param>
    /// <returns>The scenario.</returns>
    /// <exception cref="ScenarioException">The bytes are not a valid scenario.</exception>
    public static ScenarioDefinition Parse(byte[] utf8, string sourcePath)
    {
        string file = ScenarioPaths.Display(sourcePath);
        try
        {
            using var document = JsonDocument.Parse(utf8, _documentOptions);
            return new Reader(file, Path.GetFullPath(sourcePath)).Read(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new ScenarioException($"{file}: not valid JSON: {ex.Message}", ex);
        }
    }

    private static JsonElement ParseNoArgs()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private sealed class Reader(string file, string sourcePath)
    {
        public ScenarioDefinition Read(JsonElement root)
        {
            RequireObject(root, "the scenario");
            RequireKnown(root, "the scenario", "description", "content", "map", "seed", "ticks", "commands", "expect");

            string description = OptionalString(root, "description") ?? string.Empty;
            string content = RequiredString(root, "content");
            string? map = OptionalString(root, "map");
            ulong seed = RequiredSeed(root);
            int ticks = RequiredInt(root, "ticks", 0, int.MaxValue);
            IReadOnlyList<ScenarioCommandEntry> commands = ReadCommands(root, ticks);
            ScenarioExpectations expect = ReadExpect(root);
            return new ScenarioDefinition(sourcePath, description, content, map, seed, ticks, commands, expect);
        }

        private List<ScenarioCommandEntry> ReadCommands(JsonElement root, int ticks)
        {
            var commands = new List<ScenarioCommandEntry>();
            if (!root.TryGetProperty("commands", out JsonElement array) || array.ValueKind == JsonValueKind.Null)
            {
                return commands;
            }

            if (array.ValueKind != JsonValueKind.Array)
            {
                throw Fail("commands", "must be an array.");
            }

            int index = 0;
            foreach (JsonElement entry in array.EnumerateArray())
            {
                string where = $"commands[{index}]";
                RequireObject(entry, where);
                RequireKnown(entry, where, "tick", "type", "args");

                int tick = RequiredInt(
                    entry,
                    "tick",
                    1,
                    ticks,
                    where,
                    " A command with tick T is applied on tick T, and the scenario runs ticks 1 to " + ticks.ToString(CultureInfo.InvariantCulture) + ".");
                string type = RequiredString(entry, "type", where);
                JsonElement args = _noArgs;
                if (entry.TryGetProperty("args", out JsonElement given) && given.ValueKind != JsonValueKind.Null)
                {
                    if (given.ValueKind != JsonValueKind.Object)
                    {
                        throw Fail($"{where}.args", "must be an object.");
                    }

                    args = given.Clone();
                }

                commands.Add(new ScenarioCommandEntry(index, tick, type, args));
                index++;
            }

            return commands;
        }

        private ScenarioExpectations ReadExpect(JsonElement root)
        {
            if (!root.TryGetProperty("expect", out JsonElement expect) || expect.ValueKind == JsonValueKind.Null)
            {
                return ScenarioExpectations.None;
            }

            RequireObject(expect, "expect");
            RequireKnown(expect, "expect", "hash", "events", "stock");
            return new ScenarioExpectations(ReadHash(expect), ReadEvents(expect), ReadStock(expect));
        }

        private ulong? ReadHash(JsonElement expect)
        {
            if (!expect.TryGetProperty("hash", out JsonElement hash) || hash.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            string? text = hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            if (text is null || text.Length != 16 || !text.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                throw Fail("expect.hash", "must be 16 lowercase hex characters, such as \"0123456789abcdef\".");
            }

            return ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        private List<EventExpectation> ReadEvents(JsonElement expect)
        {
            var events = new List<EventExpectation>();
            if (!expect.TryGetProperty("events", out JsonElement array) || array.ValueKind == JsonValueKind.Null)
            {
                return events;
            }

            if (array.ValueKind != JsonValueKind.Array)
            {
                throw Fail("expect.events", "must be an array.");
            }

            int index = 0;
            foreach (JsonElement entry in array.EnumerateArray())
            {
                string where = $"expect.events[{index}]";
                RequireObject(entry, where);
                RequireKnown(entry, where, "type", "atLeast", "exactly");

                string type = RequiredString(entry, "type", where);
                bool hasAtLeast = entry.TryGetProperty("atLeast", out JsonElement atLeast);
                bool hasExactly = entry.TryGetProperty("exactly", out JsonElement exactly);
                if (hasAtLeast == hasExactly)
                {
                    throw Fail(where, "needs exactly one of \"atLeast\" and \"exactly\".");
                }

                int count = RequiredInt(entry, hasAtLeast ? "atLeast" : "exactly", 0, int.MaxValue, where);
                events.Add(new EventExpectation(type, count, Exact: hasExactly));
                index++;
            }

            return events;
        }

        private List<StockExpectation> ReadStock(JsonElement expect)
        {
            var stock = new List<StockExpectation>();
            if (!expect.TryGetProperty("stock", out JsonElement table) || table.ValueKind == JsonValueKind.Null)
            {
                return stock;
            }

            if (table.ValueKind != JsonValueKind.Object)
            {
                throw Fail("expect.stock", "must be an object that maps a good id to \"<op> <quantity>\".");
            }

            foreach (JsonProperty property in table.EnumerateObject())
            {
                string where = $"expect.stock.{property.Name}";
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw Fail(where, $"must be a string such as \">= 10\" (operators: {StockComparison.Spellings}).");
                }

                if (!StockComparison.TryParse(property.Value.GetString() ?? string.Empty, out ComparisonOp op, out Amount quantity, out string error))
                {
                    throw Fail(where, error);
                }

                stock.Add(new StockExpectation(property.Name, op, quantity));
            }

            return stock;
        }

        private ulong RequiredSeed(JsonElement obj)
        {
            if (!obj.TryGetProperty("seed", out JsonElement value))
            {
                throw Fail("seed", "is required.");
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong seed))
            {
                throw Fail("seed", "must be a non-negative integer that fits in 64 bits.");
            }

            return seed;
        }

        private int RequiredInt(JsonElement obj, string name, int min, int max, string? parent = null, string hint = "")
        {
            string where = parent is null ? name : $"{parent}.{name}";
            if (!obj.TryGetProperty(name, out JsonElement value))
            {
                throw Fail(where, "is required.");
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < min || number > max)
            {
                string range = max == int.MaxValue ? $"at least {min}" : $"from {min} to {max}";
                throw Fail(where, $"must be a whole number {range}.{hint}");
            }

            return number;
        }

        private string RequiredString(JsonElement obj, string name, string? parent = null)
        {
            string where = parent is null ? name : $"{parent}.{name}";
            if (!obj.TryGetProperty(name, out JsonElement value))
            {
                throw Fail(where, "is required.");
            }

            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw Fail(where, "must be a non-empty string.");
            }

            return value.GetString() ?? string.Empty;
        }

        private string? OptionalString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw Fail(name, "must be a string or null.");
        }

        private void RequireObject(JsonElement element, string where)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Fail(where, "must be a JSON object.");
            }
        }

        private void RequireKnown(JsonElement obj, string where, params ReadOnlySpan<string> known)
        {
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (!known.Contains(property.Name))
                {
                    throw Fail(where, $"has an unknown property \"{property.Name}\" (known: {string.Join(", ", known.ToArray())}).");
                }
            }
        }

        private ScenarioException Fail(string where, string message) => new($"{file}: {where}: {message}");
    }
}
