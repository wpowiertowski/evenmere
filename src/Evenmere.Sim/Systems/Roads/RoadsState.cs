using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The roads system's slice of the world state: the road layer, one <see cref="ushort"/> per tile. A tile holds 0 when it
/// has no road, and otherwise the road building type's <c>BuildingTypeId.Value</c> plus one, so the type with raw ID 0 can
/// still be told apart from "no road".
/// <para>
/// Only the layer is saved. The distance fields behind storehouse reach and service coverage, and the per-building lookups
/// derived from them, are pure functions of the layer, the building registry and the content, so they are rebuilt after a
/// load (lazily, on first use) instead of being saved. That keeps saves small, keeps the state hash independent of how the
/// fields happen to be maintained, and means a content change that alters a reach or a road cost can never leave a stale
/// field in an old save.
/// </para>
/// <para>
/// The layer is sized from the map on the first road command. Until then <see cref="Width"/> and <see cref="Height"/> are
/// zero and <see cref="Layer"/> is empty, which is also what an island without roads reads as.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </para>
/// </summary>
[MessagePackObject]
public sealed class RoadsState
{
    /// <summary>Gets or sets the width in tiles of the layer; 0 until the layer has been sized.</summary>
    [Key(0)]
    public int Width { get; set; }

    /// <summary>Gets or sets the height in tiles of the layer; 0 until the layer has been sized.</summary>
    [Key(1)]
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the road on each tile, indexed by <c>Y × Width + X</c>: 0 for none, otherwise the road type's
    /// <c>BuildingTypeId.Value</c> plus one. Empty until the layer has been sized, otherwise exactly <c>Width × Height</c> long.
    /// </summary>
    [Key(2)]
    [ContentValued(ContentKind.Building)]
    public ushort[] Layer { get; set; } = [];
}
