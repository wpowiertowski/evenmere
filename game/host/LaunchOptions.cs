using System.Globalization;

namespace Evenmere.Game.Host;

/// <summary>
/// The command-line choices after Godot's <c>--</c> separator: <c>--seed=S</c> picks the world seed, and
/// <c>--smoke-ticks=N</c> turns on the headless smoke mode (run N ticks, print the state hash, quit).
/// Arguments that are not recognised are ignored, so other tooling can share the command line.
/// </summary>
/// <param name="Seed">The world seed.</param>
/// <param name="SmokeTicks">The number of ticks to run in smoke mode, or <see langword="null"/> for a normal run.</param>
internal sealed record LaunchOptions(ulong Seed, int? SmokeTicks)
{
    /// <summary>The seed used when <c>--seed</c> is not given.</summary>
    public const ulong DefaultSeed = 42;

    private const string SeedPrefix = "--seed=";
    private const string SmokeTicksPrefix = "--smoke-ticks";

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
        int? smokeTicks = null;

        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(SeedPrefix, StringComparison.Ordinal))
            {
                string text = arg[SeedPrefix.Length..];
                seed = ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                    ? parsed
                    : throw new FormatException($"--seed needs a non-negative whole number, not '{text}'.");
            }
            else if (arg.StartsWith(SmokeTicksPrefix, StringComparison.Ordinal))
            {
                string text = arg.StartsWith(SmokeTicksPrefix + "=", StringComparison.Ordinal) ? arg[(SmokeTicksPrefix.Length + 1)..] : string.Empty;
                smokeTicks = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : throw new FormatException($"--smoke-ticks needs a non-negative whole number, not '{text}'.");
            }
        }

        return new LaunchOptions(seed, smokeTicks);
    }
}
