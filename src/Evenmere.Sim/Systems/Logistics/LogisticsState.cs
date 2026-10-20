using MessagePack;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// The logistics system's slice of the world state: storehouse carts and transport jobs. It is empty in M0; M1-09 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class LogisticsState
{
}
