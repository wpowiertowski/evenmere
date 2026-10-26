using System.Collections.Immutable;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// Why a building or road piece cannot be placed (errors, which block placement) or may be placed with a caveat
/// (warnings, which do not). The codes are shared by <c>IPlacementQueries</c> and the command handlers, which run the same
/// check. The values are part of scenario and log files; add members at the end of their group.
/// </summary>
public enum PlacementIssue : byte
{
    /// <summary>Not a real issue; the default value.</summary>
    None = 0,

    /// <summary>Error: part of the footprint lies off the map.</summary>
    OutOfBounds = 1,

    /// <summary>Error: part of the footprint is taken by another building (or, for a building, by a road).</summary>
    Occupied = 2,

    /// <summary>Error: the terrain under part of the footprint is not allowed for this building (or is not buildable).</summary>
    TerrainNotAllowed = 3,

    /// <summary>Error: the terrain is allowed in principle but research has not opened it yet (rivers, marsh, highlands, ...).</summary>
    TerrainAccessLocked = 4,

    /// <summary>Error: the ground under the footprint is too steep.</summary>
    SlopeTooSteep = 5,

    /// <summary>Error: the building or road type has not been unlocked.</summary>
    NotUnlocked = 6,

    /// <summary>Error: a unique building of this type already exists.</summary>
    UniqueAlreadyBuilt = 7,

    /// <summary>Error: the building must stand on the coast.</summary>
    NeedsCoastline = 8,

    /// <summary>Error: the building must stand over a particular mineral deposit.</summary>
    NeedsDeposit = 9,

    /// <summary>Error: too little of the footprint is fertile for the building's crop.</summary>
    FertilityTooLow = 10,

    /// <summary>Error: the building must stand beside a river.</summary>
    NeedsRiverside = 11,

    /// <summary>Error: the river crossing is longer than the bridge type's span.</summary>
    BridgeTooLong = 12,

    /// <summary>Error: the river crossing bends; a bridge must be one straight run.</summary>
    BridgeNotStraight = 13,

    /// <summary>Error: part of the footprint lies in fogged land; send an expedition first. (M2-06.)</summary>
    Fogged = 14,

    /// <summary>Warning: no road touches the footprint, so the building cannot work until one is built.</summary>
    NoRoadAccess = 100,

    /// <summary>Warning: no storehouse reaches the footprint, so the building cannot join the island's goods pool.</summary>
    OutsideStorehouseReach = 101,

    /// <summary>Warning: the building's yield here would be low (little forest, water, fertile ground or deposit).</summary>
    LowYield = 102,
}

/// <summary>One issue found by a placement check, with the tiles it concerns so the ghost can tint them.</summary>
/// <param name="Issue">The issue code.</param>
/// <param name="Tiles">The affected tiles, in ascending (Y, X) order; the whole footprint when the issue concerns the building as a whole.</param>
public sealed record PlacementFinding(PlacementIssue Issue, ImmutableArray<TileCoord> Tiles)
{
    /// <inheritdoc />
    public bool Equals(PlacementFinding? other) =>
        other is not null && Issue == other.Issue && ValueArrays.Equal(Tiles, other.Tiles);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((int)Issue, ValueArrays.Hash(Tiles));
}

/// <summary>
/// The outcome of a placement check. It has errors and warnings, each with its affected tiles. The command handler and the
/// ghost preview obtain it from the same code, so the preview never promises what the command then refuses. Two results
/// with the same findings are equal.
/// </summary>
/// <param name="Errors">The issues that block placement, in the order the rules are checked; empty when placement is allowed.</param>
/// <param name="Warnings">The issues that allow placement; empty when there are none.</param>
public sealed record PlacementResult(ImmutableArray<PlacementFinding> Errors, ImmutableArray<PlacementFinding> Warnings)
{
    /// <summary>Gets the result of a placement with no errors and no warnings.</summary>
    public static PlacementResult Ok { get; } = new([], []);

    /// <summary>Gets a value indicating whether placement is allowed, that is whether there are no errors.</summary>
    public bool CanPlace => Errors.IsDefaultOrEmpty;

    /// <summary>Tests whether the result contains an issue, as an error or as a warning.</summary>
    /// <param name="issue">The issue code.</param>
    /// <returns><see langword="true"/> if some finding has that code.</returns>
    public bool Has(PlacementIssue issue) => Contains(Errors, issue) || Contains(Warnings, issue);

    /// <inheritdoc />
    public bool Equals(PlacementResult? other) =>
        other is not null && ValueArrays.Equal(Errors, other.Errors) && ValueArrays.Equal(Warnings, other.Warnings);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ValueArrays.Hash(Errors), ValueArrays.Hash(Warnings));

    private static bool Contains(ImmutableArray<PlacementFinding> findings, PlacementIssue issue)
    {
        foreach (PlacementFinding finding in findings.AsSpan())
        {
            if (finding.Issue == issue)
            {
                return true;
            }
        }

        return false;
    }
}
