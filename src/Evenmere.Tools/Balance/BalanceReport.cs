using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Evenmere.Tools.Balance;

/// <summary>Whether a goal finished, ran out of time, or could not be done.</summary>
public enum BalanceGoalStatus
{
    /// <summary>The goal's condition held.</summary>
    Done,

    /// <summary>The goal's timeout passed first.</summary>
    Timeout,

    /// <summary>The goal could not be done (for example its anchor never existed).</summary>
    Failed,
}

/// <summary>How one goal ended, for the report.</summary>
/// <param name="Index">The goal's index in the script.</param>
/// <param name="Kind">The goal kind (<c>build</c>, <c>research</c>, ...).</param>
/// <param name="Target">What the goal wanted, in words.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Ticks">How many ticks it took (or ran before the run ended).</param>
public sealed record BalanceGoalResult(int Index, string Kind, string Target, BalanceGoalStatus Status, long Ticks);

/// <summary>A full season (10 days) with no resident growth, with the top advice seen at its end.</summary>
/// <param name="FromTick">The tick growth last stopped advancing.</param>
/// <param name="ToTick">The tick the stall was recorded.</param>
/// <param name="FromResidents">The total residents when growth stopped.</param>
/// <param name="ToResidents">The total residents when the stall was recorded.</param>
/// <param name="Advice">The top advisor note then, such as <c>GoodShortage:fish</c>; empty when no advice was active.</param>
public sealed record BalanceStall(long FromTick, long ToTick, int FromResidents, int ToResidents, string Advice);

/// <summary>Whether the script's target was reached and when.</summary>
/// <param name="Tier">The tier the script grows, by content name.</param>
/// <param name="Residents">How many residents the script wants.</param>
/// <param name="Reached">Whether they were reached.</param>
/// <param name="ReachedTick">The tick they were reached, or <see langword="null"/>.</param>
public sealed record BalanceTargetResult(string Tier, int Residents, bool Reached, long? ReachedTick)
{
    /// <summary>Gets the game minutes when the target was reached, or <see langword="null"/>.</summary>
    public double? ReachedMinutes => ReachedTick is long tick ? tick / (double)BotScript.TicksPerGameMinute : null;
}

/// <summary>The resident milestones: the first tick each level of the target tier was reached.</summary>
/// <param name="Tier">The tier watched, by content name.</param>
/// <param name="FirstTick">The first tick any resident of the tier lived, or <see langword="null"/>.</param>
/// <param name="FiftyTick">The first tick at least 50 lived, or <see langword="null"/>.</param>
/// <param name="HundredTick">The first tick at least 100 lived, or <see langword="null"/>.</param>
public sealed record BalanceMilestones(string Tier, long? FirstTick, long? FiftyTick, long? HundredTick);

/// <summary>The wall-clock timing of the run. It is kept apart from everything else, because it varies from machine to machine.</summary>
/// <param name="TicksRun">How many ticks ran.</param>
/// <param name="MeanMsPerTick">The mean wall-clock milliseconds per tick.</param>
/// <param name="PeakMsPerTick">The slowest single tick in wall-clock milliseconds.</param>
public sealed record BalanceTimings(long TicksRun, double MeanMsPerTick, double PeakMsPerTick);

/// <summary>
/// What the balance bot saw: milestones, stalls, commands, stock, residents and the final hash, plus the
/// wall-clock timings in their own section. The <c>deterministic</c> section is byte-identical for the same
/// seed and script; the <c>timings</c> section is not.
/// </summary>
/// <param name="Script">The script's name.</param>
/// <param name="Seed">The world seed.</param>
/// <param name="Ticks">How many ticks ran.</param>
/// <param name="Target">Whether the script's target was reached.</param>
/// <param name="Milestones">The resident milestones.</param>
/// <param name="Goals">How each goal ended, in script order.</param>
/// <param name="Stalls">The stalls, in time order.</param>
/// <param name="Issued">Commands issued, by command type name, in ordinal order.</param>
/// <param name="Rejected">Commands rejected, by <c>Type(Reason)</c>, in ordinal order.</param>
/// <param name="Residents">Final residents per tier, by tier name, in ordinal order.</param>
/// <param name="Stock">Final stock per good in whole units, by good name, in ordinal order.</param>
/// <param name="Notes">Bot decisions worth knowing (for example a shortage with no unlocked producer), in order.</param>
/// <param name="Hash">The final state hash, 16 lowercase hex digits.</param>
/// <param name="Timings">The wall-clock timing, kept separate so tests can compare the rest byte for byte.</param>
public sealed record BalanceReport(
    string Script,
    ulong Seed,
    long Ticks,
    BalanceTargetResult Target,
    BalanceMilestones Milestones,
    ImmutableArray<BalanceGoalResult> Goals,
    ImmutableArray<BalanceStall> Stalls,
    ImmutableSortedDictionary<string, int> Issued,
    ImmutableSortedDictionary<string, int> Rejected,
    ImmutableSortedDictionary<string, int> Residents,
    ImmutableSortedDictionary<string, double> Stock,
    ImmutableArray<string> Notes,
    string Hash,
    BalanceTimings Timings)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Gets the game minutes that ran.</summary>
    public double Minutes => Ticks / (double)BotScript.TicksPerGameMinute;

    /// <summary>Serialises the deterministic section: everything except the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline, byte-identical for the same run.</returns>
    public string ToDeterministicJson() => DeterministicNode().ToJsonString(_json) + "\n";

    /// <summary>Serialises the whole report: the deterministic section plus the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline.</returns>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["deterministic"] = DeterministicNode(),
            ["timings"] = new JsonObject
            {
                ["ticksRun"] = Timings.TicksRun,
                ["meanMsPerTick"] = Timings.MeanMsPerTick,
                ["peakMsPerTick"] = Timings.PeakMsPerTick,
            },
        };
        return root.ToJsonString(_json) + "\n";
    }

    /// <summary>Renders the human-readable text report.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"balance: script {Script}, seed {Seed}, {Ticks} ticks ({Minutes:F1} game minutes)\n");
        text.Append(CultureInfo.InvariantCulture, $"target: {(Target.Reached ? $"reached {Target.Tier} {Target.Residents} at tick {Target.ReachedTick} ({Target.ReachedMinutes:F1} min)" : $"missed {Target.Tier} {Target.Residents} (stopped at tick {Ticks})")}\n");
        text.Append(CultureInfo.InvariantCulture, $"milestones ({Milestones.Tier}): first {DescribeTick(Milestones.FirstTick)}, 50 {DescribeTick(Milestones.FiftyTick)}, 100 {DescribeTick(Milestones.HundredTick)}\n");
        text.Append(CultureInfo.InvariantCulture, $"stalls: {(Stalls.Length == 0 ? "none" : Stalls.Length.ToString(CultureInfo.InvariantCulture))}\n");
        foreach (BalanceStall stall in Stalls)
        {
            text.Append(CultureInfo.InvariantCulture, $"  stall ticks {stall.FromTick}-{stall.ToTick} ({stall.FromTick / (double)BotScript.TicksPerGameMinute:F1}-{(stall.ToTick / (double)BotScript.TicksPerGameMinute):F1} min): {stall.FromResidents} -> {stall.ToResidents} residents, top advice {(stall.Advice.Length == 0 ? "none" : stall.Advice)}\n");
        }

        text.Append("commands issued:");
        if (Issued.IsEmpty)
        {
            text.Append(" none\n");
        }
        else
        {
            text.Append('\n');
            foreach ((string name, int count) in Issued)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {name}: {count}\n");
            }
        }

        text.Append("commands rejected:");
        if (Rejected.IsEmpty)
        {
            text.Append(" none\n");
        }
        else
        {
            text.Append('\n');
            foreach ((string name, int count) in Rejected)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {name}: {count}\n");
            }
        }

        text.Append("residents:");
        foreach ((string name, int count) in Residents)
        {
            text.Append(CultureInfo.InvariantCulture, $" {name}={count}");
        }

        text.Append("\nstock:");
        foreach ((string name, double units) in Stock)
        {
            text.Append(CultureInfo.InvariantCulture, $" {name}={units:F3}");
        }

        text.Append('\n');
        foreach (string note in Notes)
        {
            text.Append(CultureInfo.InvariantCulture, $"note: {note}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"hash: {Hash}\n");
        text.Append(CultureInfo.InvariantCulture, $"timings: {Timings.TicksRun} ticks, mean {Timings.MeanMsPerTick:F3} ms/tick, peak {Timings.PeakMsPerTick:F3} ms/tick (wall clock, not deterministic)\n");
        return text.ToString();
    }

    private static string DescribeTick(long? tick) =>
        tick is long value
            ? string.Create(CultureInfo.InvariantCulture, $"tick {value} ({value / (double)BotScript.TicksPerGameMinute:F1} min)")
            : "not reached";

    private JsonObject DeterministicNode()
    {
        var goals = new JsonArray();
        foreach (BalanceGoalResult goal in Goals)
        {
            goals.Add(new JsonObject
            {
                ["index"] = goal.Index,
                ["kind"] = goal.Kind,
                ["target"] = goal.Target,
                ["status"] = goal.Status.ToString().ToLowerInvariant(),
                ["ticks"] = goal.Ticks,
            });
        }

        var stalls = new JsonArray();
        foreach (BalanceStall stall in Stalls)
        {
            stalls.Add(new JsonObject
            {
                ["fromTick"] = stall.FromTick,
                ["toTick"] = stall.ToTick,
                ["fromResidents"] = stall.FromResidents,
                ["toResidents"] = stall.ToResidents,
                ["advice"] = stall.Advice,
            });
        }

        var issued = new JsonObject();
        foreach ((string name, int count) in Issued)
        {
            issued[name] = count;
        }

        var rejected = new JsonObject();
        foreach ((string name, int count) in Rejected)
        {
            rejected[name] = count;
        }

        var residents = new JsonObject();
        foreach ((string name, int count) in Residents)
        {
            residents[name] = count;
        }

        var stock = new JsonObject();
        foreach ((string name, double units) in Stock)
        {
            stock[name] = units;
        }

        var notes = new JsonArray();
        foreach (string note in Notes)
        {
            notes.Add(note);
        }

        return new JsonObject
        {
            ["script"] = Script,
            ["seed"] = Seed,
            ["ticks"] = Ticks,
            ["minutes"] = Minutes,
            ["target"] = new JsonObject
            {
                ["tier"] = Target.Tier,
                ["residents"] = Target.Residents,
                ["reached"] = Target.Reached,
                ["reachedTick"] = Target.ReachedTick is long reached ? JsonValue.Create(reached) : null,
            },
            ["milestones"] = new JsonObject
            {
                ["tier"] = Milestones.Tier,
                ["firstTick"] = Milestones.FirstTick is long first ? JsonValue.Create(first) : null,
                ["fiftyTick"] = Milestones.FiftyTick is long fifty ? JsonValue.Create(fifty) : null,
                ["hundredTick"] = Milestones.HundredTick is long hundred ? JsonValue.Create(hundred) : null,
            },
            ["goals"] = goals,
            ["stalls"] = stalls,
            ["issued"] = issued,
            ["rejected"] = rejected,
            ["residents"] = residents,
            ["stock"] = stock,
            ["notes"] = notes,
            ["hash"] = Hash,
        };
    }
}
