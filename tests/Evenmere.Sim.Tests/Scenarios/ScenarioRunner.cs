using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// Runs a scenario: creates the world, enqueues each scripted command just before the tick it is applied on, ticks the
/// simulation, drains the events and checks the file's expectations.
/// <para>
/// <b>When a command runs.</b> A command whose <c>tick</c> is T is enqueued just before the simulation ticks from T-1 to
/// T. The simulation applies queued commands at the start of the next tick, so the command is applied on tick T, and
/// anything it emits carries tick T. Tick 1 is therefore the earliest, "before anything has run", and a command at the
/// scenario's last tick is applied on the last tick. Commands with the same tick are enqueued in file order.
/// </para>
/// <para>
/// <b>What is checked, in order.</b> Event counts, then stock and residents (read after the last tick), then the golden hash. With golden
/// updating on, a wrong or missing hash is rewritten and the run passes, but only when every other expectation held.
/// </para>
/// </summary>
internal static class ScenarioRunner
{
    // Content never changes during a test run, so each folder is loaded once and shared by every scenario that names it.
    private static readonly ConcurrentDictionary<string, Lazy<ContentDb>> _contentByPath = new(StringComparer.Ordinal);

    /// <summary>Reads and runs a scenario file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="options">Whether to rewrite golden hashes.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ScenarioException">The file cannot be run.</exception>
    public static ScenarioResult RunFile(string path, ScenarioRunOptions options) => Run(ScenarioParser.ParseFile(path), options);

    /// <summary>Runs a scenario.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="options">Whether to rewrite golden hashes.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ScenarioException">The scenario cannot be run.</exception>
    public static ScenarioResult Run(ScenarioDefinition scenario, ScenarioRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);

        ContentDb content = LoadContent(scenario);
        MapDef? mapDef = LoadMap(scenario, content);
        JsonSerializerOptions json = ScenarioJson.CreateOptions(content);
        List<PreparedCommand> commands = PrepareCommands(scenario, content, json);
        List<(StockExpectation Expectation, GoodId Good)> stockChecks = PrepareStock(scenario, content);
        List<(ResidentExpectation Expectation, TierId Tier)> residentChecks = PrepareResidents(scenario, content);
        CheckEventNames(scenario);

        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = scenario.Seed, Map = mapDef });
        RegisterTestHandlers(sim, commands);

        var events = new List<SimEvent>();
        int next = 0;
        for (int tick = 1; tick <= scenario.Ticks; tick++)
        {
            while (next < commands.Count && commands[next].Tick == tick)
            {
                sim.Enqueue(ResolveCommand(scenario, sim, commands[next], json));
                next++;
            }

            sim.Tick();
            events.AddRange(sim.DrainEvents());
        }

        ulong actualHash = sim.ComputeStateHash();
        var failures = new List<string>();
        CheckEvents(scenario, events, failures);
        CheckStock(sim, content, stockChecks, failures);
        CheckResidents(sim, content, residentChecks, failures);

        ulong? expectedHash = scenario.Expect.Hash;
        bool hashDiffers = expectedHash != actualHash;
        bool update = options.UpdateGolden && hashDiffers && failures.Count == 0;
        if (update)
        {
            GoldenFile.Write(scenario.SourcePath, actualHash);
        }
        else if (expectedHash is { } expected && hashDiffers)
        {
            string note = options.UpdateGolden ? " (not rewritten, because another expectation failed)" : string.Empty;
            failures.Add($"hash: the state hash is {HashText.Format(actualHash)}, but the file expects {HashText.Format(expected)}{note}.");
        }

        bool passed = failures.Count == 0;
        IReadOnlyList<SystemTiming> timings = sim.GetTimings();
        string report = update
            ? ScenarioReport.GoldenUpdated(scenario, actualHash)
            : passed
                ? ScenarioReport.Passed(scenario, sim.CurrentTick, actualHash, events.Count)
                : ScenarioReport.Failure(scenario, sim.CurrentTick, actualHash, events, failures, options.UpdateGolden);

        return new ScenarioResult(scenario.DisplayName, passed, actualHash, expectedHash, events, failures, update, report, timings, sim.TimerTicksPerSecond, sim.CurrentTick);
    }

    private static ContentDb LoadContent(ScenarioDefinition scenario)
    {
        string folder = ScenarioPaths.Resolve(scenario.ContentPath);
        if (!Directory.Exists(folder))
        {
            throw new ScenarioException(
                $"{scenario.DisplayName}: content: the folder \"{scenario.ContentPath}\" does not exist (looked for {folder}). The path is relative to the repository root.");
        }

        try
        {
            return _contentByPath.GetOrAdd(folder, static path => new Lazy<ContentDb>(() => ContentLoader.Load(path).GetDbOrThrow())).Value;
        }
        catch (ContentLoadException ex)
        {
            throw new ScenarioException($"{scenario.DisplayName}: content: \"{scenario.ContentPath}\" does not load. {ex.Message}", ex);
        }
    }

    // Maps live outside the content database, so the named map is loaded through MapLoader with the scenario's content
    // roots. A null map keeps the empty world the harness uses.
    private static MapDef? LoadMap(ScenarioDefinition scenario, ContentDb content)
    {
        if (scenario.Map is null)
        {
            return null;
        }

        string folder = ScenarioPaths.Resolve(scenario.ContentPath);
        MapLoadResult loaded = MapLoader.Load([folder], scenario.Map, content);
        if (loaded.Map is null)
        {
            string detail = loaded.Diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error)?.ToString()
                ?? "no details";
            throw new ScenarioException(
                $"{scenario.DisplayName}: map: cannot load map \"{scenario.Map}\": {detail} The map id names a folder under \"maps/\".");
        }

        return loaded.Map;
    }

    // Every command is resolved and deserialised before the first tick, so a mistake in the last command fails at once.
    // An entity id may be written as { "at": [x, y] }, which the runner resolves to the building on that tile when the
    // command is enqueued, at its own tick. The shape is checked here, up front; the tile must hold a building then.
    private static List<PreparedCommand> PrepareCommands(ScenarioDefinition scenario, ContentDb content, JsonSerializerOptions json)
    {
        CommandRegistry registry = CommandRegistry.Default;
        var prepared = new List<PreparedCommand>(scenario.Commands.Count);
        foreach (ScenarioCommandEntry entry in scenario.Commands)
        {
            string where = $"{scenario.DisplayName}: commands[{entry.Index}]";
            if (!registry.TryGet(entry.Type, out CommandRegistration? registration))
            {
                throw new ScenarioException($"{where}.type: {registry.DescribeUnknown(entry.Type)}");
            }

            List<AtResolution> atForms = ReadAtForms(scenario, entry, registration);
            JsonElement args = atForms.Count == 0 ? entry.Args : SubstitutePlaceholders(entry.Args, atForms);
            try
            {
                prepared.Add(new PreparedCommand(entry.Tick, entry.Index, CommandRegistry.Deserialize(registration, args, json), registration, entry.Args, atForms));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new ScenarioException($"{where}.args: the args do not fit {registration.Name}: {WithoutPosition(ex.Message)}", ex);
            }
        }

        // OrderBy is stable, so commands with the same tick keep their file order.
        return [.. prepared.OrderBy(static c => c.Tick)];
    }

    // Reads every { "at": [x, y] } in a command's args, which must sit on an entity-id property of the command record.
    private static List<AtResolution> ReadAtForms(ScenarioDefinition scenario, ScenarioCommandEntry entry, CommandRegistration registration)
    {
        var found = new List<AtResolution>();
        if (entry.Args.ValueKind != JsonValueKind.Object)
        {
            return found;
        }

        HashSet<string> entityProperties =
        [
            .. registration.CommandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static p => p.PropertyType == typeof(EntityId))
                .Select(static p => p.Name),
        ];
        StringComparer comparer = StringComparer.OrdinalIgnoreCase;
        var names = new HashSet<string>(entityProperties, comparer);

        foreach (JsonProperty property in entry.Args.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object || !IsAtForm(property.Value))
            {
                continue;
            }

            string where = $"{scenario.DisplayName}: commands[{entry.Index}].args.{property.Name}";
            if (!names.Contains(property.Name))
            {
                throw new ScenarioException(
                    $"{where}: {{ \"at\": [x, y] }} names the building on a tile, so it fits only an entity id; \"{property.Name}\" is not one.");
            }

            found.Add(new AtResolution(property.Name, ReadAtCoords(where, property.Value)));
        }

        return found;
    }

    // Tests whether an args object is written in the tile form: exactly one property, "at".
    private static bool IsAtForm(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        int count = 0;
        bool at = false;
        foreach (JsonProperty inner in value.EnumerateObject())
        {
            count++;
            at |= inner.Name.Equals("at", StringComparison.OrdinalIgnoreCase);
        }

        return count == 1 && at;
    }

    // Reads the [x, y] of an { "at": [x, y] } form.
    private static TileCoord ReadAtCoords(string where, JsonElement value)
    {
        JsonElement coords = value.EnumerateObject().First().Value;
        if (coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() == 2
            && coords[0].ValueKind == JsonValueKind.Number && coords[0].TryGetInt32(out int x)
            && coords[1].ValueKind == JsonValueKind.Number && coords[1].TryGetInt32(out int y))
        {
            return new TileCoord(x, y);
        }

        throw new ScenarioException($"{where}: must be {{ \"at\": [x, y] }} with two whole tile numbers.");
    }

    // Rewrites each tile form in a copy of the args to a placeholder id, so the upfront deserialisation checks the rest.
    private static JsonElement SubstitutePlaceholders(JsonElement args, List<AtResolution> atForms)
    {
        var node = JsonNode.Parse(args.GetRawText());
        if (node is not JsonObject patched)
        {
            throw new InvalidOperationException("A command's args must be a JSON object.");
        }

        foreach (AtResolution resolution in atForms)
        {
            patched[resolution.Property] = -1;
        }

        return JsonDocument.Parse(patched.ToJsonString()).RootElement;
    }

    // Enqueues a prepared command, resolving its tile forms against the island as it stands on the command's tick.
    private static Command ResolveCommand(ScenarioDefinition scenario, Simulation sim, PreparedCommand prepared, JsonSerializerOptions json)
    {
        if (prepared.AtResolutions.Count == 0)
        {
            return prepared.Command;
        }

        var node = JsonNode.Parse(prepared.Args.GetRawText());
        if (node is not JsonObject patched)
        {
            throw new InvalidOperationException("A command's args must be a JSON object.");
        }

        foreach (AtResolution resolution in prepared.AtResolutions)
        {
            EntityId building = sim.Query.Map.BuildingAt(resolution.At.X, resolution.At.Y);
            if (building.IsNone)
            {
                throw new ScenarioException(
                    $"{scenario.DisplayName}: commands[{prepared.Index}]: {{ \"at\": [{resolution.At.X}, {resolution.At.Y}] }} names no building; nothing stands on that tile at tick {sim.CurrentTick}.");
            }

            patched[resolution.Property] = building.Value;
        }

        JsonElement args = JsonDocument.Parse(patched.ToJsonString()).RootElement;
        try
        {
            return CommandRegistry.Deserialize(prepared.Registration, args, json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new ScenarioException(
                $"{scenario.DisplayName}: commands[{prepared.Index}].args: the resolved args do not fit {prepared.Registration.Name}: {WithoutPosition(ex.Message)}", ex);
        }
    }

    private static List<(StockExpectation Expectation, GoodId Good)> PrepareStock(ScenarioDefinition scenario, ContentDb content)
    {
        var checks = new List<(StockExpectation, GoodId)>(scenario.Expect.Stock.Count);
        foreach (StockExpectation expectation in scenario.Expect.Stock)
        {
            if (!content.Goods.TryGet(expectation.Good, out GoodId good))
            {
                throw new ScenarioException(
                    $"{scenario.DisplayName}: expect.stock.{expectation.Good}: no good named \"{expectation.Good}\" in {scenario.ContentPath}. Known goods: {string.Join(", ", content.Goods.Names)}.");
            }

            checks.Add((expectation, good));
        }

        return checks;
    }

    private static List<(ResidentExpectation Expectation, TierId Tier)> PrepareResidents(ScenarioDefinition scenario, ContentDb content)
    {
        var checks = new List<(ResidentExpectation, TierId)>(scenario.Expect.Residents.Count);
        foreach (ResidentExpectation expectation in scenario.Expect.Residents)
        {
            if (!content.Tiers.TryGet(expectation.Tier, out TierId tier))
            {
                throw new ScenarioException(
                    $"{scenario.DisplayName}: expect.residents.{expectation.Tier}: no tier named \"{expectation.Tier}\" in {scenario.ContentPath}. Known tiers: {string.Join(", ", content.Tiers.Names)}.");
            }

            checks.Add((expectation, tier));
        }

        return checks;
    }

    private static void CheckEventNames(ScenarioDefinition scenario)
    {
        for (int i = 0; i < scenario.Expect.Events.Count; i++)
        {
            string name = scenario.Expect.Events[i].Type;
            if (!EventRegistry.Default.Contains(name))
            {
                throw new ScenarioException($"{scenario.DisplayName}: expect.events[{i}].type: {EventRegistry.Default.DescribeUnknown(name)}");
            }
        }
    }

    private static void RegisterTestHandlers(Simulation sim, List<PreparedCommand> commands)
    {
        // Only the test-only commands this scenario uses get a handler, one registration per command type.
        var registered = new HashSet<Type>();
        foreach (PreparedCommand command in commands)
        {
            if (registered.Add(command.Registration.CommandType))
            {
                command.Registration.RegisterHandler(sim.Router);
            }
        }
    }

    private static void CheckEvents(ScenarioDefinition scenario, List<SimEvent> events, List<string> failures)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SimEvent simEvent in events)
        {
            string name = simEvent.GetType().Name;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        foreach (EventExpectation expectation in scenario.Expect.Events)
        {
            int seen = counts.GetValueOrDefault(expectation.Type);
            bool met = expectation.Exact ? seen == expectation.Count : seen >= expectation.Count;
            if (!met)
            {
                string kind = expectation.Exact ? "exactly" : "at least";
                failures.Add($"events: expected {kind} {expectation.Count} {expectation.Type} event(s), but the run emitted {seen}.");
            }
        }
    }

    private static void CheckStock(Simulation sim, ContentDb content, List<(StockExpectation Expectation, GoodId Good)> checks, List<string> failures)
    {
        foreach ((StockExpectation expectation, GoodId good) in checks)
        {
            Amount actual = sim.Query.Storage.Stock(good);
            if (!StockComparison.Holds(actual, expectation.Op, expectation.Quantity))
            {
                failures.Add(
                    $"stock: expected {content.NameOf(good)} {StockComparison.Symbol(expectation.Op)} {expectation.Quantity}, but the island holds {actual}.");
            }
        }
    }

    private static void CheckResidents(Simulation sim, ContentDb content, List<(ResidentExpectation Expectation, TierId Tier)> checks, List<string> failures)
    {
        foreach ((ResidentExpectation expectation, TierId tier) in checks)
        {
            int actual = sim.Query.Population.Residents(tier);
            bool holds = expectation.Op switch
            {
                ComparisonOp.GreaterOrEqual => actual >= expectation.Quantity,
                ComparisonOp.LessOrEqual => actual <= expectation.Quantity,
                ComparisonOp.Equal => actual == expectation.Quantity,
                ComparisonOp.Greater => actual > expectation.Quantity,
                ComparisonOp.Less => actual < expectation.Quantity,
                _ => throw new ArgumentOutOfRangeException(nameof(checks), expectation.Op, "Unknown comparison operator."),
            };

            if (!holds)
            {
                failures.Add(
                    $"residents: expected {content.NameOf(tier)} {StockComparison.Symbol(expectation.Op)} {expectation.Quantity}, but the island holds {actual}.");
            }
        }
    }

    // System.Text.Json appends "LineNumber" and "BytePositionInLine" counted inside the args snippet, which mean nothing in the scenario file.
    private static string WithoutPosition(string message)
    {
        int cut = message.IndexOf(" | LineNumber:", StringComparison.Ordinal);
        return (cut < 0 ? message : message[..cut]).Trim();
    }

    private sealed record PreparedCommand(int Tick, int Index, Command Command, CommandRegistration Registration, JsonElement Args, IReadOnlyList<AtResolution> AtResolutions);

    /// <summary>One <c>{ "at": [x, y] }</c> tile form in a command's args: the args property it sits on and the tile.</summary>
    /// <param name="Property">The args property name as written in the file, which names an entity id of the command.</param>
    /// <param name="At">The tile whose building the runner resolves when the command is enqueued.</param>
    private sealed record AtResolution(string Property, TileCoord At);
}
