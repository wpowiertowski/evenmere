using MessagePack;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// The culture system's slice of the world state: the civic target and queue, adopted civics, traditions, festivals and great works. It is empty in M0; M1-11 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class CultureState
{
}
