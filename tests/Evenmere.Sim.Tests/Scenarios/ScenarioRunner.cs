using System.Collections.Concurrent;
using System.Text.Json;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

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
/// <b>What is checked, in order.</b> Event counts, then stock (read after the last tick), then the golden hash. With golden
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

        if (scenario.Map is not null)
        {
            throw new ScenarioException(
                $"{scenario.DisplayName}: map: \"{scenario.Map}\" cannot be loaded; maps are not supported until M1-01. Use null for the empty world.");
        }

        ContentDb content = LoadContent(scenario);
        List<PreparedCommand> commands = PrepareCommands(scenario, content);
        List<(StockExpectation Expectation, GoodId Good)> stockChecks = PrepareStock(scenario, content);
        CheckEventNames(scenario);

        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = scenario.Seed });
        RegisterTestHandlers(sim, commands);

        var events = new List<SimEvent>();
        int next = 0;
        for (int tick = 1; tick <= scenario.Ticks; tick++)
        {
            while (next < commands.Count && commands[next].Tick == tick)
            {
                sim.Enqueue(commands[next].Command);
                next++;
            }

            sim.Tick();
            events.AddRange(sim.DrainEvents());
        }

        ulong actualHash = sim.ComputeStateHash();
        var failures = new List<string>();
        CheckEvents(scenario, events, failures);
        CheckStock(sim, content, stockChecks, failures);

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
        string report = update
            ? ScenarioReport.GoldenUpdated(scenario, actualHash)
            : passed
                ? ScenarioReport.Passed(scenario, sim.CurrentTick, actualHash, events.Count)
                : ScenarioReport.Failure(scenario, sim.CurrentTick, actualHash, events, failures, options.UpdateGolden);

        return new ScenarioResult(scenario.DisplayName, passed, actualHash, expectedHash, events, failures, update, report);
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

    // Every command is resolved and deserialised before the first tick, so a mistake in the last command fails at once.
    private static List<PreparedCommand> PrepareCommands(ScenarioDefinition scenario, ContentDb content)
    {
        CommandRegistry registry = CommandRegistry.Default;
        JsonSerializerOptions json = ScenarioJson.CreateOptions(content);
        var prepared = new List<PreparedCommand>(scenario.Commands.Count);
        foreach (ScenarioCommandEntry entry in scenario.Commands)
        {
            string where = $"{scenario.DisplayName}: commands[{entry.Index}]";
            if (!registry.TryGet(entry.Type, out CommandRegistration? registration))
            {
                throw new ScenarioException($"{where}.type: {registry.DescribeUnknown(entry.Type)}");
            }

            try
            {
                prepared.Add(new PreparedCommand(entry.Tick, CommandRegistry.Deserialize(registration, entry.Args, json), registration));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new ScenarioException($"{where}.args: the args do not fit {registration.Name}: {WithoutPosition(ex.Message)}", ex);
            }
        }

        // OrderBy is stable, so commands with the same tick keep their file order.
        return [.. prepared.OrderBy(static c => c.Tick)];
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

    // System.Text.Json appends "LineNumber" and "BytePositionInLine" counted inside the args snippet, which mean nothing in the scenario file.
    private static string WithoutPosition(string message)
    {
        int cut = message.IndexOf(" | LineNumber:", StringComparison.Ordinal);
        return (cut < 0 ? message : message[..cut]).Trim();
    }

    private sealed record PreparedCommand(int Tick, Command Command, CommandRegistration Registration);
}
