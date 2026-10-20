using System.Globalization;
using System.Text;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>Writes the text of a <see cref="ScenarioResult"/>: a failure report a developer can act on without a debugger.</summary>
internal static class ScenarioReport
{
    /// <summary>The most events a failure report lists.</summary>
    public const int MaxEventsShown = 20;

    /// <summary>Writes the report for a run with failed expectations.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="ticksRun">How many ticks ran.</param>
    /// <param name="actualHash">The hash the run ended with.</param>
    /// <param name="events">Every event the run drained.</param>
    /// <param name="failures">The failed expectations, in the order they were checked; not empty.</param>
    /// <param name="updateRequested">Whether golden updating was on, which changes the hint at the end.</param>
    /// <returns>The report.</returns>
    public static string Failure(
        ScenarioDefinition scenario,
        long ticksRun,
        ulong actualHash,
        IReadOnlyList<SimEvent> events,
        IReadOnlyList<string> failures,
        bool updateRequested)
    {
        var text = new StringBuilder();
        text.Append("Scenario FAILED: ").AppendLine(scenario.DisplayName);
        AppendHeader(text, scenario, ticksRun);
        text.AppendLine();
        text.Append("First failing expectation: ").AppendLine(failures[0]);
        if (failures.Count > 1)
        {
            text.AppendLine("Also failing:");
            for (int i = 1; i < failures.Count; i++)
            {
                text.Append("  - ").AppendLine(failures[i]);
            }
        }

        text.AppendLine();
        text.Append("  expected hash: ").AppendLine(HashText.Format(scenario.Expect.Hash));
        text.Append("  actual hash:   ").AppendLine(HashText.Format(actualHash));
        text.AppendLine();
        AppendEvents(text, events);

        bool onlyTheHashFailed = failures.Count == 1 && failures[0].StartsWith("hash:", StringComparison.Ordinal);
        if (onlyTheHashFailed && !updateRequested)
        {
            text.AppendLine();
            text.Append("If the behaviour change is intended, rewrite the golden hash with ")
                .Append(ScenarioRunOptions.UpdateGoldenVariable)
                .AppendLine("=1 dotnet test tests/Evenmere.Sim.Tests -c Release, and explain the change in the commit body.");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Writes the notice for a run that rewrote its golden hash.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="actualHash">The new hash.</param>
    /// <returns>The notice.</returns>
    public static string GoldenUpdated(ScenarioDefinition scenario, ulong actualHash) =>
        $"{ScenarioRunOptions.UpdateGoldenVariable}=1: rewrote expect.hash in {scenario.DisplayName}: "
        + $"{HashText.Format(scenario.Expect.Hash)} -> {HashText.Format(actualHash)}. "
        + "Review the diff, and explain the behaviour change in the commit body.";

    /// <summary>Writes the one-line summary of a passing run.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="ticksRun">How many ticks ran.</param>
    /// <param name="actualHash">The hash the run ended with.</param>
    /// <param name="eventCount">How many events the run drained.</param>
    /// <returns>The summary.</returns>
    public static string Passed(ScenarioDefinition scenario, long ticksRun, ulong actualHash, int eventCount) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Scenario passed: {scenario.DisplayName} ({ticksRun} ticks, seed {scenario.Seed}, hash {HashText.Format(actualHash)}, {eventCount} events).");

    private static void AppendHeader(StringBuilder text, ScenarioDefinition scenario, long ticksRun)
    {
        if (scenario.Description.Length > 0)
        {
            text.Append("  description: ").AppendLine(scenario.Description);
        }

        text.Append("  content: ").Append(scenario.ContentPath)
            .Append("   seed: ").Append(scenario.Seed)
            .Append("   ticks: ").Append(scenario.Ticks);
        if (ticksRun != scenario.Ticks)
        {
            text.Append(" (stopped after ").Append(ticksRun).Append(')');
        }

        text.AppendLine();
        text.Append("  commands: ").Append(scenario.Commands.Count).AppendLine();
    }

    private static void AppendEvents(StringBuilder text, IReadOnlyList<SimEvent> events)
    {
        if (events.Count == 0)
        {
            text.AppendLine("Events: none were emitted.");
            return;
        }

        int shown = Math.Min(events.Count, MaxEventsShown);
        text.Append("Events: ").Append(events.Count).Append(" emitted");
        text.Append(shown < events.Count ? $"; the last {shown}, oldest first:" : ", oldest first:").AppendLine();
        for (int i = events.Count - shown; i < events.Count; i++)
        {
            SimEvent simEvent = events[i];
            text.Append("  tick ").Append(simEvent.Tick.ToString(CultureInfo.InvariantCulture).PadLeft(7))
                .Append("  ").Append(simEvent.GetType().Name);
            if (simEvent is CommandRejected rejected)
            {
                text.Append("  ").Append(rejected.Code).Append(" for ").Append(rejected.Command.GetType().Name).Append(": ").Append(rejected.Detail);
            }

            text.AppendLine();
        }
    }
}
