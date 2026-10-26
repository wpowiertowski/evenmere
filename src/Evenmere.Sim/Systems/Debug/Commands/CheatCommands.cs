using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Systems.Debug.Commands;

/// <summary>
/// Grants goods straight into the island pool, for testers who want to skip production (ADR-0012).
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.CheatsDisabled"/> when the save's cheats flag is off, with
/// <see cref="RejectReason.UnknownTarget"/> when the content does not define the good, and with
/// <see cref="RejectReason.Invalid"/> when the amount is negative.
/// </remarks>
/// <param name="Good">The good to grant.</param>
/// <param name="Amount">How much to add, in milli-units; zero or more.</param>
public sealed record CheatGrantGoods(GoodId Good, Amount Amount) : Command;

/// <summary>
/// Completes one tech or one civic at once, applying its effects and emitting the researched or adopted event,
/// for testers who want to skip the trees (ADR-0012). Availability is bypassed on purpose: a cheat finishes
/// locked nodes too. Completing an already-done node is a no-op.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.CheatsDisabled"/> when the save's cheats flag is off, with
/// <see cref="RejectReason.Invalid"/> when the command names zero or two nodes, and with
/// <see cref="RejectReason.UnknownTarget"/> when the named node is not defined by the content.
/// </remarks>
/// <param name="Tech">The tech to complete, or <see langword="null"/> when completing a civic.</param>
/// <param name="Civic">The civic to adopt, or <see langword="null"/> when completing a tech.</param>
public sealed record CheatCompleteNode(TechId? Tech, CivicId? Civic) : Command;

/// <summary>
/// Moves the island into an era at once, emitting the era-began event, for testers who want to skip the
/// gateways (ADR-0012). Gateway completion is bypassed on purpose. Moving into the current era is a no-op.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.CheatsDisabled"/> when the save's cheats flag is off, and with
/// <see cref="RejectReason.UnknownTarget"/> when the content does not define the era.
/// </remarks>
/// <param name="Era">The era to move into.</param>
public sealed record CheatSetEra(EraId Era) : Command;

/// <summary>
/// Reveals fog, for testers who want the whole island visible (ADR-0012).
/// </summary>
/// <remarks>
/// <see cref="CheatRevealAll.AllRegions"/> reveals every region; any other value names one region by number
/// (map data, not a content ID, so command-log remap leaves it alone). Until exploration lands (M2-06) there
/// is no fog to lift, so the handler validates the cheats flag and the region number and then reports that
/// the reveal needs M2-06. Refused with <see cref="RejectReason.CheatsDisabled"/> when the save's cheats
/// flag is off, and with <see cref="RejectReason.Invalid"/> for a region below <see cref="AllRegions"/>.
/// </remarks>
/// <param name="Region">The region number to reveal, or <see cref="AllRegions"/> for all of them.</param>
public sealed record CheatRevealAll(int Region) : Command
{
    /// <summary>The region number that reveals every region instead of one.</summary>
    public const int AllRegions = -1;
}

/// <summary>
/// Moves residents into houses with free beds, in ascending house-ID order, for testers who want population
/// without waiting for move-ins (ADR-0012). The move is all or nothing: when the free beds do not fit the
/// headcount, nothing moves.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.CheatsDisabled"/> when the save's cheats flag is off, and with
/// <see cref="RejectReason.Invalid"/> for a non-positive headcount or when the free beds do not fit it.
/// </remarks>
/// <param name="Count">How many residents move in; at least 1.</param>
public sealed record CheatAddResidents(int Count) : Command;
