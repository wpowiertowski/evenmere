using MessagePack;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// The modifiers system's slice of the world state: the effective-stat index, which is rebuilt whenever an effect is added or removed. It is empty in M0; M1-04 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ModifiersState
{
}
