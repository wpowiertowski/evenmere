using System.Globalization;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>A scratch folder for scenario files that a harness test writes, rewrites or expects to fail. Deleted on dispose.</summary>
internal sealed class ScenarioTempFolder : IDisposable
{
    public ScenarioTempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "evenmere-scenarios-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Writes a scenario file and returns its path.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="text">The file's text.</param>
    /// <returns>The path.</returns>
    public string Write(string name, string text)
    {
        string path = Path.Combine(Root, name);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>Builds scenario text for the harness tests, on the minimal content pack.</summary>
internal static class ScenarioText
{
    /// <summary>Builds a scenario with the given parts; the rest is the smallest valid scenario.</summary>
    /// <param name="ticks">How many ticks to run.</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="commands">The JSON array of commands.</param>
    /// <param name="expect">The JSON object of expectations.</param>
    /// <returns>The JSON text.</returns>
    public static string Make(int ticks = 10, ulong seed = 1, string commands = "[]", string expect = "{}") =>
        $$"""
        {
          "content": "tests/fixtures/content/minimal",
          "map": null,
          "seed": {{seed}},
          "ticks": {{ticks}},
          "commands": {{commands}},
          "expect": {{expect}}
        }
        """;

    /// <summary>Builds a <c>RecordTick</c> command entry.</summary>
    /// <param name="tick">The tick to apply it on.</param>
    /// <param name="label">The label the event carries.</param>
    /// <returns>The JSON object.</returns>
    public static string RecordTickAt(int tick, string label = "x") =>
        string.Create(CultureInfo.InvariantCulture, $$"""{ "tick": {{tick}}, "type": "RecordTick", "args": { "label": "{{label}}" } }""");

    /// <summary>Wraps command entries in a JSON array.</summary>
    /// <param name="entries">The command objects.</param>
    /// <returns>The array.</returns>
    public static string CommandList(params string[] entries) => "[" + string.Join(", ", entries) + "]";

    /// <summary>Parses and runs scenario text with golden updating off.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The result.</returns>
    public static ScenarioResult Run(string json) =>
        ScenarioRunner.Run(ScenarioParser.Parse(json, "harness-test.json"), ScenarioRunOptions.Strict);
}
