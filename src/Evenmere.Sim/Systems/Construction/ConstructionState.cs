using MessagePack;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The construction system's slice of the world state: construction sites and their progress. It is empty in M0; M1-06 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ConstructionState
{
}
