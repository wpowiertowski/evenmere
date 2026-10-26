using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>A fogged region's fog lifted because an expedition returned (or a cheat, a migration or a new game revealed it). (M2-06.)</summary>
/// <param name="Tick">The tick during which the region was revealed.</param>
/// <param name="Region">The region number whose fog lifted.</param>
public sealed record RegionRevealed(long Tick, ushort Region) : SimEvent(Tick);

/// <summary>An expedition returned to a region holding a placed feature: a natural wonder, ruins or a shipwreck. (M2-06.)</summary>
/// <param name="Tick">The tick during which the discovery was revealed.</param>
/// <param name="Region">The region number of the discovery.</param>
/// <param name="Kind">The kind of discovery.</param>
/// <param name="X">The column of the feature's representative tile.</param>
/// <param name="Y">The row of the feature's representative tile.</param>
public sealed record DiscoveryRevealed(long Tick, ushort Region, Evenmere.Content.Definitions.DiscoveryKind Kind, int X, int Y) : SimEvent(Tick);
