using MessagePack;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// The exploration system's slice of the world state: revealed regions, expeditions and discoveries. It is empty in M0; M2-06 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ExplorationState
{
}
