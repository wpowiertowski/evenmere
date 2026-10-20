using MessagePack;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// The production system's slice of the world state: producers, their recipe progress and their input and output buffers. It is empty in M0; M1-08 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ProductionState
{
}
