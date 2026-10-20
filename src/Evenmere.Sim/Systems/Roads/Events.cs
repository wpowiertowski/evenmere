using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>Roads were laid or removed. The view redraws the road strips inside the bounding box.</summary>
/// <param name="Tick">The tick during which the roads changed.</param>
/// <param name="Min">The top-left corner of the smallest rectangle that holds every changed tile, inclusive.</param>
/// <param name="Max">The bottom-right corner of that rectangle, inclusive.</param>
public sealed record RoadsChanged(long Tick, TileCoord Min, TileCoord Max) : SimEvent(Tick);
