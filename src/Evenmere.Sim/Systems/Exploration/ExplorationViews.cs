using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>One expedition under way, as the view and the game UI read it. (M2-06.)</summary>
/// <param name="Lodge">The Scout's Lodge that sent the expedition.</param>
/// <param name="TargetRegion">The fogged region it travels to.</param>
/// <param name="DaysLeft">The days left before the region reveals; at least 1 while under way.</param>
public readonly record struct ExpeditionView(EntityId Lodge, ushort TargetRegion, int DaysLeft);

/// <summary>One discovery placed on the island, as the view reads it, with whether its fog has lifted. (M2-06.)</summary>
/// <param name="Region">The region number of the tile the discovery stands on.</param>
/// <param name="Kind">The kind of discovery: natural wonder, ruins or a shipwreck.</param>
/// <param name="X">The column of the feature's representative tile.</param>
/// <param name="Y">The row of the feature's representative tile.</param>
/// <param name="Revealed">Whether the discovery's region is revealed.</param>
/// <param name="Applied">Whether the discovery's effect has applied.</param>
public readonly record struct DiscoveryView(ushort Region, DiscoveryKind Kind, int X, int Y, bool Revealed, bool Applied);
