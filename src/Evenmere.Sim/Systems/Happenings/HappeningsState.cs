using MessagePack;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>
/// The happenings system's slice of the world state: pending and recent happenings. It is empty in M0; M2-12 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class HappeningsState
{
}
