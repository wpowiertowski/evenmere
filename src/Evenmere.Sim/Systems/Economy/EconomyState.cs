using MessagePack;

namespace Evenmere.Sim.Systems.Economy;

/// <summary>
/// The economy system's slice of the world state: the coin ledger, taxes and upkeep. It is empty in M0; M3-01 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class EconomyState
{
}
