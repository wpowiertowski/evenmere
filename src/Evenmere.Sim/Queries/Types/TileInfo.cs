using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// How steep a tile is, derived on map load from the largest elevation difference between the tile and its four
/// neighbours (elevation steps are 0.5 world units each). Placement rules compare classes, never raw differences.
/// </summary>
public enum SlopeClass : byte
{
    /// <summary>The largest difference to a neighbour is 0 steps.</summary>
    Flat = 0,

    /// <summary>The largest difference is 1 step.</summary>
    Gentle = 1,

    /// <summary>The largest difference is 2 steps.</summary>
    Moderate = 2,

    /// <summary>The largest difference is 3 steps or more.</summary>
    Steep = 3,
}

/// <summary>Terrain features of a tile that are not captured by its terrain type. The values are part of the map format; add members at the end.</summary>
[Flags]
public enum TileFeatures : byte
{
    /// <summary>No feature.</summary>
    None = 0,

    /// <summary>A river flows over the tile.</summary>
    River = 1,

    /// <summary>A land tile next to the sea (derived on load).</summary>
    Coast = 2,

    /// <summary>A beach tile.</summary>
    Beach = 4,

    /// <summary>Deep water, usable only after Harbour Works.</summary>
    DeepWater = 8,

    /// <summary>A reef tile, usable only after the Diving Bell.</summary>
    Reef = 16,

    /// <summary>A marsh tile, usable only after Drainage.</summary>
    Marsh = 32,
}

/// <summary>How wide the river is where it crosses a tile; bridges are limited by it.</summary>
public enum RiverWidthClass : byte
{
    /// <summary>The tile has no river.</summary>
    None = 0,

    /// <summary>A river up to 3 tiles wide, crossed by Bridge-building.</summary>
    Narrow = 1,

    /// <summary>A river up to 8 tiles wide, crossed by Stone Bridges.</summary>
    Wide = 2,
}

/// <summary>
/// Everything the view and the systems may ask about one tile of the map. It is a copy: changing the map later never
/// changes a value already returned.
/// </summary>
/// <param name="Terrain">The terrain type.</param>
/// <param name="Elevation">The elevation in steps of 0.5 world units, from 0 to 15.</param>
/// <param name="Slope">The slope class derived from the elevation differences to the four neighbours.</param>
/// <param name="Features">The terrain features of the tile.</param>
/// <param name="RiverWidth">The river width class; <see cref="RiverWidthClass.None"/> unless <see cref="TileFeatures.River"/> is set.</param>
/// <param name="Region">The region number the tile belongs to (a base-36 digit in the authored map); regions group the island for fog and exploration.</param>
/// <param name="Deposit">The mineral deposit under the tile, if any.</param>
/// <param name="FertilityMask">One bit per crop, set when the tile is fertile for it; the bit of a crop is <c>1 &lt;&lt; (int)CropKind</c> (see <see cref="FertilityBit"/>).</param>
public readonly record struct TileInfo(
    TerrainId Terrain,
    int Elevation,
    SlopeClass Slope,
    TileFeatures Features,
    RiverWidthClass RiverWidth,
    ushort Region,
    DepositKind? Deposit,
    uint FertilityMask)
{
    /// <summary>Gets the bit a crop occupies in <see cref="FertilityMask"/>.</summary>
    /// <param name="crop">The crop.</param>
    /// <returns>A mask with exactly one bit set.</returns>
    public static uint FertilityBit(CropKind crop) => 1u << (int)crop;

    /// <summary>Tests whether the tile has every one of the given features.</summary>
    /// <param name="features">The features to look for.</param>
    /// <returns><see langword="true"/> if all of them are set.</returns>
    public bool Has(TileFeatures features) => (Features & features) == features;

    /// <summary>Tests whether the tile is fertile for a crop.</summary>
    /// <param name="crop">The crop.</param>
    /// <returns><see langword="true"/> if the crop's bit is set in <see cref="FertilityMask"/>.</returns>
    public bool IsFertileFor(CropKind crop) => (FertilityMask & FertilityBit(crop)) != 0;
}
