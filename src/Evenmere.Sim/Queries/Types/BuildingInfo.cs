using Evenmere.Core.Ids;

namespace Evenmere.Sim.Queries.Types;

/// <summary>Where a building is in its life. The values are part of the save format; add members at the end.</summary>
public enum BuildingState : byte
{
    /// <summary>A construction site: materials are being drawn and nothing else works yet.</summary>
    Site = 0,

    /// <summary>A finished building that is working.</summary>
    Active = 1,

    /// <summary>A finished building that the player has paused; it produces nothing but still counts as built.</summary>
    Paused = 2,

    /// <summary>What is left of a building whose type no longer exists in the content (set when a save is loaded).</summary>
    Ruin = 3,
}

/// <summary>A copy of one building's registry entry. Houses are buildings too, so a house is also described here.</summary>
/// <param name="Id">The building's entity ID.</param>
/// <param name="Type">The building type.</param>
/// <param name="Origin">The footprint's top-left corner, that is its smallest column and row.</param>
/// <param name="Rotation">The orientation.</param>
/// <param name="Width">The footprint width in tiles after rotation.</param>
/// <param name="Height">The footprint height in tiles after rotation.</param>
/// <param name="State">The building's state.</param>
/// <param name="Stage">
/// The construction stage: for a <see cref="BuildingState.Site"/>, how many of its build steps are complete (0 up to the
/// number of steps the construction rules define, 10 in M1); 0 for a building that is not a site.
/// </param>
public readonly record struct BuildingInfo(
    EntityId Id,
    BuildingTypeId Type,
    TileCoord Origin,
    Rotation Rotation,
    int Width,
    int Height,
    BuildingState State,
    int Stage)
{
    /// <summary>Gets a value indicating whether the building is finished and not a ruin: <see cref="BuildingState.Active"/> or <see cref="BuildingState.Paused"/>.</summary>
    public bool IsComplete => State is BuildingState.Active or BuildingState.Paused;

    /// <summary>Tests whether a tile lies inside the building's footprint.</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <returns><see langword="true"/> if the tile is one of the building's <c>Width × Height</c> tiles.</returns>
    public bool Covers(int x, int y) =>
        x >= Origin.X && x < Origin.X + Width && y >= Origin.Y && y < Origin.Y + Height;
}
