using MessagePack;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// The population system's slice of the world state: houses, their tiers and occupancy, and the resident counts. It is empty in M0; M1-10 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class PopulationState
{
}
