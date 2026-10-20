using MessagePack;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// The map system's slice of the world state: the tile grid, terrain layers, the building registry and the dirty-chunk tracker. It is empty in M0; M1-01 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class MapState
{
}
