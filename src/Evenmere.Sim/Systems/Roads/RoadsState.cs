using MessagePack;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The roads system's slice of the world state: the road layer and the integer distance fields behind storehouse reach and service coverage. It is empty in M0; M1-05 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class RoadsState
{
}
