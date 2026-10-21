using System.Globalization;
using Evenmere.Sim.Diagnostics;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>Settings for a scenario run.</summary>
/// <param name="UpdateGolden">
/// Whether a hash mismatch (or a missing hash) rewrites <c>expect.hash</c> in the file and passes, instead of failing.
/// </param>
internal sealed record ScenarioRunOptions(bool UpdateGolden)
{
    /// <summary>The environment variable that turns golden updating on when it is <c>1</c>.</summary>
    public const string UpdateGoldenVariable = "EVENMERE_UPDATE_GOLDEN";

    /// <summary>Gets options that fail on a hash mismatch.</summary>
    public static ScenarioRunOptions Strict { get; } = new(false);

    /// <summary>Reads <see cref="UpdateGoldenVariable"/>.</summary>
    /// <returns>The options the process was started with.</returns>
    public static ScenarioRunOptions FromEnvironment() => FromVariable(Environment.GetEnvironmentVariable(UpdateGoldenVariable));

    /// <summary>Maps the variable's value to options: only <c>1</c> turns golden updating on.</summary>
    /// <param name="value">The value, or <see langword="null"/> when the variable is not set.</param>
    /// <returns>The options.</returns>
    public static ScenarioRunOptions FromVariable(string? value) => new(value == "1");
}

/// <summary>What a scenario run produced.</summary>
/// <param name="Scenario">The scenario file as the report shows it.</param>
/// <param name="Passed">Whether every expectation held (or the golden hash was rewritten).</param>
/// <param name="ActualHash">The state hash after the last tick.</param>
/// <param name="ExpectedHash">The hash the file expected, or <see langword="null"/> when it had none.</param>
/// <param name="Events">Every event the run drained, in emission order.</param>
/// <param name="Failures">One line per expectation that did not hold, in the order they were checked: events, stock, then the hash.</param>
/// <param name="GoldenUpdated">Whether the file's <c>expect.hash</c> was rewritten.</param>
/// <param name="Report">
/// The readable account of the run: the full failure report when it failed, the golden-update notice when the file was
/// rewritten, and a one-line summary otherwise.
/// </param>
/// <param name="Timings">The profiler's per-step timings at the end of the run, for diagnostics only (never asserted).</param>
/// <param name="TimerTicksPerSecond">The profiler's timer frequency, for converting <paramref name="Timings"/> to time.</param>
/// <param name="Ticks">How many ticks the scenario ran.</param>
internal sealed record ScenarioResult(
    string Scenario,
    bool Passed,
    ulong ActualHash,
    ulong? ExpectedHash,
    IReadOnlyList<SimEvent> Events,
    IReadOnlyList<string> Failures,
    bool GoldenUpdated,
    string Report,
    IReadOnlyList<SystemTiming> Timings,
    long TimerTicksPerSecond,
    long Ticks);

/// <summary>Formats state hashes the way scenario files spell them.</summary>
internal static class HashText
{
    /// <summary>Formats a hash as 16 lowercase hex characters.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The text.</returns>
    public static string Format(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>Formats a hash that may be absent.</summary>
    /// <param name="hash">The hash, or <see langword="null"/>.</param>
    /// <returns>The text, or a note that the file has none.</returns>
    public static string Format(ulong? hash) => hash is { } value ? Format(value) : "(the file has no expect.hash)";
}
