using System.Text.Json;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Smoke;

/// <summary>
/// Builds and checks the committed Godot-smoke fixture from <c>slice_first_houses</c>.
/// The fixture is a tick-0 save plus the scenario's commands as a command log, so neither
/// the game nor the CLI needs to read scenario JSON. Regenerate with
/// <c>EVENMERE_UPDATE_GOLDEN=1</c>, like the other goldens.
/// </summary>
public sealed class FirstHousesSmokeTests
{
    private const string ScenarioFileName = "slice_first_houses.json";
    private const string SaveFileName = "first_houses.evsave";
    private const string LogFileName = "first_houses.evlog";

    private static string ScenarioPath => Path.Combine(ScenarioPaths.ScenarioDirectory, ScenarioFileName);

    private static string FixtureDirectory => Path.Combine(ScenarioPaths.RepoRoot, "tests", "fixtures", "smoke");

    private static string SavePath => Path.Combine(FixtureDirectory, SaveFileName);

    private static string LogPath => Path.Combine(FixtureDirectory, LogFileName);

    [Fact]
    public void SmokeFixture_MatchesScenario()
    {
        if (Environment.GetEnvironmentVariable(ScenarioRunOptions.UpdateGoldenVariable) == "1")
        {
            SmokeFixtureBuilder.Rebuild(ScenarioPath, SavePath, LogPath);
        }

        File.Exists(SavePath).ShouldBeTrue($"the smoke fixture {SavePath} is missing; run with EVENMERE_UPDATE_GOLDEN=1 to create it.");
        File.Exists(LogPath).ShouldBeTrue($"the smoke fixture {LogPath} is missing; run with EVENMERE_UPDATE_GOLDEN=1 to create it.");

        ScenarioDefinition scenario = ScenarioParser.ParseFile(ScenarioPath);
        ScenarioResult result = ScenarioRunner.Run(scenario, ScenarioRunOptions.Strict);
        result.Passed.ShouldBeTrue(result.Report);

        ContentDb content = SmokeFixtureBuilder.LoadContent(scenario);
        ulong replayed;
        using (FileStream save = File.OpenRead(SavePath))
        using (FileStream log = File.OpenRead(LogPath))
        {
            replayed = Replay.Run(save, log, content, scenario.Ticks);
        }

        replayed.ShouldBe(result.ActualHash);
        scenario.Expect.Hash.ShouldBe(result.ActualHash);
    }

    [Fact]
    public void SmokeFixture_LogHoldsEveryScenarioCommandAtItsAppliedTick()
    {
        if (Environment.GetEnvironmentVariable(ScenarioRunOptions.UpdateGoldenVariable) == "1")
        {
            SmokeFixtureBuilder.Rebuild(ScenarioPath, SavePath, LogPath);
        }

        ScenarioDefinition scenario = ScenarioParser.ParseFile(ScenarioPath);
        var expected = SmokeFixtureBuilder.SortedCommands(scenario)
            .Select(static entry => (entry.Tick, entry.Type))
            .ToList();

        ContentDb content = SmokeFixtureBuilder.LoadContent(scenario);
        IReadOnlyList<(long Tick, Command Command)> logged;
        using (FileStream save = File.OpenRead(SavePath))
        using (FileStream log = File.OpenRead(LogPath))
        {
            (ISimulation _, SaveReport _, SaveTables tables) = SmokeFixtureBuilder.ReadWithTables(save, content);
            logged = new CommandLogReader(log, tables).ReadAll();
        }

        logged.Count.ShouldBe(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            logged[i].Tick.ShouldBe(expected[i].Tick);
            logged[i].Command.GetType().Name.ShouldBe(expected[i].Type);
        }
    }
}

/// <summary>
/// Reads <c>slice_first_houses</c> through the scenario machinery and writes the smoke
/// fixture: a tick-0 save and the scenario's commands as a command log.
/// </summary>
internal static class SmokeFixtureBuilder
{
    /// <summary>Rebuilds the fixture files from the scenario.</summary>
    /// <param name="scenarioPath">The scenario file.</param>
    /// <param name="savePath">The save to write.</param>
    /// <param name="logPath">The log to write.</param>
    internal static void Rebuild(string scenarioPath, string savePath, string logPath)
    {
        ScenarioDefinition scenario = ScenarioParser.ParseFile(scenarioPath);
        ContentDb content = LoadContent(scenario);
        MapDef? map = LoadMap(scenario, content);
        IReadOnlyList<(int Tick, Command Command)> commands = DeserializeCommands(scenario, content);

        var simulation = Simulation.CreateNew(content, new NewGameOptions { Seed = scenario.Seed, Map = map });

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        using (FileStream save = File.Create(savePath))
        {
            SaveGame.Write(save, simulation, new SaveMeta(content, "smoke", scenario.Map));
        }

        using (FileStream log = File.Create(logPath))
        {
            var writer = new CommandLogWriter(log);
            foreach ((int tick, Command command) in commands)
            {
                writer.Append(tick, command);
            }

            writer.Dispose();
        }
    }

    /// <summary>Loads the scenario's content.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns>The content.</returns>
    internal static ContentDb LoadContent(ScenarioDefinition scenario)
    {
        string folder = ScenarioPaths.Resolve(scenario.ContentPath);
        return ContentLoader.Load(folder).GetDbOrThrow();
    }

    /// <summary>Lists the scenario's commands in the order the runner enqueues them.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns>The (tick, type) pairs in enqueue order.</returns>
    internal static IReadOnlyList<ScenarioCommandEntry> SortedCommands(ScenarioDefinition scenario) =>
        scenario.Commands.OrderBy(static entry => entry.Tick).ToList();

    /// <summary>Reads a save and keeps its ID maps, for log verification.</summary>
    /// <param name="save">The save stream.</param>
    /// <param name="content">The current content.</param>
    /// <returns>The simulation, report and tables.</returns>
    internal static (ISimulation Simulation, SaveReport Report, SaveTables Tables) ReadWithTables(Stream save, ContentDb content) =>
        SaveGame.ReadInner(save, content);

    private static MapDef? LoadMap(ScenarioDefinition scenario, ContentDb content)
    {
        if (scenario.Map is null)
        {
            return null;
        }

        string folder = ScenarioPaths.Resolve(scenario.ContentPath);
        MapLoadResult loaded = MapLoader.Load([folder], scenario.Map, content);
        return loaded.Map ?? throw new InvalidOperationException(
            $"Cannot load map \"{scenario.Map}\" for the smoke fixture: " +
            $"{loaded.Diagnostics.FirstOrDefault()?.ToString() ?? "no details"}");
    }

    private static List<(int Tick, Command Command)> DeserializeCommands(ScenarioDefinition scenario, ContentDb content)
    {
        JsonSerializerOptions json = ScenarioJson.CreateOptions(content);
        CommandRegistry registry = CommandRegistry.Default;
        var prepared = new List<(int Tick, int Index, Command Command)>(scenario.Commands.Count);
        foreach (ScenarioCommandEntry entry in scenario.Commands)
        {
            if (!registry.TryGet(entry.Type, out CommandRegistration? registration) || registration is null)
            {
                throw new InvalidOperationException($"Unknown command type \"{entry.Type}\".");
            }

            Command command = CommandRegistry.Deserialize(registration, entry.Args, json);
            prepared.Add((entry.Tick, entry.Index, command));
        }

        // OrderBy is stable, so commands with the same tick keep their file order, like the runner.
        return prepared.OrderBy(static item => item.Tick)
            .Select(static item => (item.Tick, item.Command))
            .ToList();
    }
}
