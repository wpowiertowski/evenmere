using System.Globalization;

namespace Evenmere.Game.Host;

/// <summary>
/// The command-line choices after Godot's <c>--</c> separator: <c>--seed=S</c> picks the world seed, <c>--map=ID</c> picks
/// the island (a folder under <c>maps/</c>, default <see cref="DefaultMap"/>), and <c>--smoke-ticks=N</c> turns on the
/// headless smoke mode (run N ticks, print the state hash, quit). With <c>--smoke-save</c> and <c>--smoke-log</c> the
/// smoke loads that save and replays that log across real frames instead of starting a new game.
/// Arguments that are not recognised are ignored, so other tooling can share the command line.
/// </summary>
/// <param name="Seed">The world seed.</param>
/// <param name="Map">The island to start on.</param>
/// <param name="SmokeTicks">The number of ticks to run in smoke mode, or <see langword="null"/> for a normal run.</param>
/// <param name="SmokeSave">The save to load in smoke v2, or <see langword="null"/> to start a new game.</param>
/// <param name="SmokeLog">The command log to replay in smoke v2, or <see langword="null"/> for no log.</param>
internal sealed record LaunchOptions(ulong Seed, string Map, int? SmokeTicks, string? SmokeSave, string? SmokeLog)
{
    /// <summary>The seed used when <c>--seed</c> is not given.</summary>
    public const ulong DefaultSeed = 42;

    /// <summary>The island used when <c>--map</c> is not given.</summary>
    public const string DefaultMap = "starter";

    private const string SeedPrefix = "--seed=";
    private const string MapPrefix = "--map=";
    private const string SmokeTicksPrefix = "--smoke-ticks";
    private const string SmokeSavePrefix = "--smoke-save=";
    private const string SmokeLogPrefix = "--smoke-log=";

    /// <summary>Tests whether the arguments ask for smoke mode, even if their values are malformed.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns><see langword="true"/> if any argument starts with <c>--smoke-ticks</c>.</returns>
    public static bool RequestsSmoke(IEnumerable<string> userArgs) =>
        userArgs.Any(static arg => arg.StartsWith(SmokeTicksPrefix, StringComparison.Ordinal));

    /// <summary>Reads the options from the user arguments.</summary>
    /// <param name="userArgs">The user arguments, as <c>OS.GetCmdlineUserArgs()</c> returns them.</param>
    /// <returns>The options, with defaults for anything not given.</returns>
    /// <exception cref="FormatException">A recognised argument has a missing or malformed value.</exception>
    public static LaunchOptions Parse(IEnumerable<string> userArgs)
    {
        ulong seed = DefaultSeed;
        string map = DefaultMap;
        int? smokeTicks = null;
        string? smokeSave = null;
        string? smokeLog = null;

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(SeedPrefix, StringComparison.Ordinal))
            {
                string text = arg[SeedPrefix.Length..];
                seed = ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                    ? parsed
                    : throw new FormatException($"--seed needs a non-negative whole number, not '{text}'.");
            }
            else if (arg.StartsWith(MapPrefix, StringComparison.Ordinal))
            {
                string text = arg[MapPrefix.Length..];
                map = !string.IsNullOrWhiteSpace(text)
                    ? text
                    : throw new FormatException($"--map needs an island id, not '{arg}'.");
            }
            else if (arg.StartsWith(SmokeTicksPrefix, StringComparison.Ordinal))
            {
                string text = arg.StartsWith(SmokeTicksPrefix + "=", StringComparison.Ordinal) ? arg[(SmokeTicksPrefix.Length + 1)..] : string.Empty;
                smokeTicks = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : throw new FormatException($"--smoke-ticks needs a non-negative whole number, not '{text}'.");
            }
            else if (arg.StartsWith(SmokeSavePrefix, StringComparison.Ordinal))
            {
                string text = arg[SmokeSavePrefix.Length..];
                smokeSave = !string.IsNullOrWhiteSpace(text)
                    ? text
                    : throw new FormatException($"--smoke-save needs a save path, not '{arg}'.");
            }
            else if (arg.StartsWith(SmokeLogPrefix, StringComparison.Ordinal))
            {
                string text = arg[SmokeLogPrefix.Length..];
                smokeLog = !string.IsNullOrWhiteSpace(text)
                    ? text
                    : throw new FormatException($"--smoke-log needs a log path, not '{arg}'.");
            }
        }

        return new LaunchOptions(seed, map, smokeTicks, smokeSave, smokeLog);
    }
}
