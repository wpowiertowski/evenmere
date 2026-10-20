using MessagePack;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The progression system's slice of the world state: unlock sets, applied effects, the current era, the points ledger and the milestone counters. It is empty in M0; M1-04 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ProgressionState
{
}
