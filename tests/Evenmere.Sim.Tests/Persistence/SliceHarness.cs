using System.Text.Json;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Tests.Integration;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// Runs the slice scenario's scripted commands without the scenario expectations, so save/load tests can stop at any
/// tick, save, load and continue. The scenario file is the authority for the build order; this harness only changes
/// how long the run goes.
/// </summary>
internal static class SliceHarness
{
    /// <summary>Gets the slice scenario file.</summary>
    public static string ScenarioFile => Path.Combine(ScenarioPaths.ScenarioDirectory, "slice_villagers.json");

    /// <summary>Loads the slice scenario definition.</summary>
    /// <returns>The parsed scenario.</returns>
    public static ScenarioDefinition LoadScenario() => ScenarioParser.ParseFile(ScenarioFile);

    /// <summary>Deserialises the scenario's commands with the given content.</summary>
    /// <param name="content">The content the string IDs resolve against.</param>
    /// <returns>The (tick, command) pairs in file order.</returns>
    public static IReadOnlyList<(int Tick, Command Command)> LoadCommands(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        ScenarioDefinition scenario = LoadScenario();
        JsonSerializerOptions json = ScenarioJson.CreateOptions(content);
        var commands = new List<(int Tick, Command Command)>(scenario.Commands.Count);
        foreach (ScenarioCommandEntry entry in scenario.Commands)
        {
            if (!CommandRegistry.Default.TryGet(entry.Type, out CommandRegistration? registration))
            {
                throw new InvalidOperationException($"Unknown command type '{entry.Type}'.");
            }

            commands.Add((entry.Tick, CommandRegistry.Deserialize(registration, entry.Args, json)));
        }

        return commands;
    }

    /// <summary>Runs the slice commands to a tick on the starter island.</summary>
    /// <param name="content">The content to run on; the real slice content by default.</param>
    /// <param name="map">The island; the starter island by default.</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="ticks">The tick to stop at (inclusive).</param>
    /// <param name="commands">The commands to play; the slice's own by default.</param>
    /// <returns>The simulation at <paramref name="ticks"/>.</returns>
    public static Simulation RunTo(
        int ticks,
        ContentDb? content = null,
        MapDef? map = null,
        ulong seed = 1,
        IReadOnlyList<(int Tick, Command Command)>? commands = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        ContentDb db = content ?? SiteFinder.Content;
        MapDef island = map ?? SiteFinder.Map;
        IReadOnlyList<(int Tick, Command Command)> script = commands ?? LoadCommands(db);

        var simulation = Simulation.CreateNew(db, new NewGameOptions { Seed = seed, Map = island });
        int next = 0;
        for (int tick = 1; tick <= ticks; tick++)
        {
            while (next < script.Count && script[next].Tick == tick)
            {
                simulation.Enqueue(script[next].Command);
                next++;
            }

            simulation.Tick();
        }

        return simulation;
    }
}
