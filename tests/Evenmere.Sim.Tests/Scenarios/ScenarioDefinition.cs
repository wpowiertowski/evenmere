using System.Text.Json;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>A scenario file, read and checked for shape. See <c>tests/fixtures/scenarios/README.md</c> for the format.</summary>
/// <param name="SourcePath">The absolute path of the file, which golden updates rewrite.</param>
/// <param name="Description">Free text for people; may be empty.</param>
/// <param name="ContentPath">The content folder, relative to the repository root.</param>
/// <param name="Map">The map fixture id, or <see langword="null"/> for the empty world. Maps do not exist until M1-01.</param>
/// <param name="Seed">The world seed.</param>
/// <param name="Ticks">How many ticks to run.</param>
/// <param name="Commands">The scripted commands, in file order.</param>
/// <param name="Expect">What the run must satisfy.</param>
internal sealed record ScenarioDefinition(
    string SourcePath,
    string Description,
    string ContentPath,
    string? Map,
    ulong Seed,
    int Ticks,
    IReadOnlyList<ScenarioCommandEntry> Commands,
    ScenarioExpectations Expect)
{
    /// <summary>Gets the file path as the messages show it.</summary>
    public string DisplayName => ScenarioPaths.Display(SourcePath);
}

/// <summary>One scripted command, before its type is resolved.</summary>
/// <param name="Index">The position in the file's <c>commands</c> array, counted from zero.</param>
/// <param name="Tick">
/// The tick on which the command is applied: it is enqueued just before the simulation ticks from <c>Tick - 1</c> to
/// <c>Tick</c>, and the architecture applies queued commands at the start of the next tick.
/// </param>
/// <param name="Type">The simple type name of the command record.</param>
/// <param name="Args">The <c>args</c> object; an empty object when the file has none.</param>
internal sealed record ScenarioCommandEntry(int Index, int Tick, string Type, JsonElement Args);

/// <summary>The <c>expect</c> block of a scenario.</summary>
/// <param name="Hash">The golden state hash, or <see langword="null"/> when the file has none.</param>
/// <param name="Events">The event-count expectations, in file order.</param>
/// <param name="Stock">The island-stock expectations, in file order.</param>
internal sealed record ScenarioExpectations(ulong? Hash, IReadOnlyList<EventExpectation> Events, IReadOnlyList<StockExpectation> Stock)
{
    /// <summary>Gets an <c>expect</c> block with nothing in it.</summary>
    public static ScenarioExpectations None { get; } = new(null, [], []);
}

/// <summary>Expects how many events of one type the run emits.</summary>
/// <param name="Type">The simple type name of the event record.</param>
/// <param name="Count">The count to compare against.</param>
/// <param name="Exact"><see langword="true"/> for <c>exactly</c>, <see langword="false"/> for <c>atLeast</c>.</param>
internal sealed record EventExpectation(string Type, int Count, bool Exact);

/// <summary>Expects how much of a good the island storage holds after the last tick.</summary>
/// <param name="Good">The good's string id.</param>
/// <param name="Op">How to compare.</param>
/// <param name="Quantity">The quantity to compare against.</param>
internal sealed record StockExpectation(string Good, ComparisonOp Op, Amount Quantity);

/// <summary>The comparison operators of a stock expectation.</summary>
internal enum ComparisonOp
{
    /// <summary>The stock is at least the quantity (<c>&gt;=</c>).</summary>
    GreaterOrEqual,

    /// <summary>The stock is at most the quantity (<c>&lt;=</c>).</summary>
    LessOrEqual,

    /// <summary>The stock is exactly the quantity (<c>==</c>).</summary>
    Equal,

    /// <summary>The stock is more than the quantity (<c>&gt;</c>).</summary>
    Greater,

    /// <summary>The stock is less than the quantity (<c>&lt;</c>).</summary>
    Less,
}
