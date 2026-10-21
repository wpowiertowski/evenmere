using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Tools.Balance;

/// <summary>
/// Where the placement search wants to build: the footprint's top-left tile after <see cref="Rotation"/>,
/// with the validation the search ran for it.
/// </summary>
/// <param name="X">The column of the footprint's top-left tile.</param>
/// <param name="Y">The row of the footprint's top-left tile.</param>
/// <param name="Rotation">The orientation; a quarter or three-quarter turn swaps width and height.</param>
/// <param name="Validation">The placement check for this spot.</param>
public sealed record PlacementCandidate(int X, int Y, Rotation Rotation, PlacementResult Validation);

/// <summary>
/// The bot's placement search: a spiral outward from an anchor rectangle, validated through
/// <c>IPlacementQueries.Validate</c>. Candidates run in deterministic order (rings of ascending Chebyshev
/// distance, then rows, then columns, then rotations 0 to 3) with no randomness. The first spot with no
/// errors and no warnings wins. Otherwise the first spot with no errors and no <c>LowYield</c> warning wins:
/// road warnings clear once the bot connects the building, but a poor yield lasts forever. The first spot with
/// no errors wins only when every spot has a low yield.
/// </summary>
public static class PlacementSearch
{
    /// <summary>Gets the default search radius in tiles when the caller names none.</summary>
    public const int DefaultMaxRadius = 30;

    /// <summary>
    /// Searches for a place to build.
    /// </summary>
    /// <param name="simulation">The simulation, read between ticks.</param>
    /// <param name="type">The building type to place.</param>
    /// <param name="anchorX">The column of the anchor rectangle's top-left tile; the spiral starts here.</param>
    /// <param name="anchorY">The row of the anchor rectangle's top-left tile.</param>
    /// <param name="baseWidth">The footprint width before rotation.</param>
    /// <param name="baseHeight">The footprint height before rotation.</param>
    /// <param name="maxRadius">How far from the anchor to look, in tiles.</param>
    /// <param name="isExcluded">Skips a candidate origin and rotation when it returns <see langword="true"/> (for spots the bot already rejected); <see langword="null"/> excludes nothing.</param>
    /// <returns>The best candidate, or <see langword="null"/> when every spot within radius has an error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRadius"/> is negative.</exception>
    public static PlacementCandidate? Find(
        ISimulation simulation,
        BuildingTypeId type,
        int anchorX,
        int anchorY,
        int baseWidth,
        int baseHeight,
        int maxRadius = DefaultMaxRadius,
        Func<int, int, Rotation, bool>? isExcluded = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRadius);

        int mapWidth = simulation.Query.Map.Width;
        int mapHeight = simulation.Query.Map.Height;
        PlacementCandidate? firstKeptYield = null;
        PlacementCandidate? firstPlaceable = null;
        for (int ring = 0; ring <= maxRadius; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    int x = anchorX + dx;
                    int y = anchorY + dy;
                    for (int r = 0; r < 4; r++)
                    {
                        var rotation = (Rotation)r;
                        int width = rotation.SwapsAxes() ? baseHeight : baseWidth;
                        int height = rotation.SwapsAxes() ? baseWidth : baseHeight;
                        if (x < 0 || y < 0 || x + width > mapWidth || y + height > mapHeight)
                        {
                            continue;
                        }

                        if (isExcluded is not null && isExcluded(x, y, rotation))
                        {
                            continue;
                        }

                        PlacementResult validation = simulation.Query.Placement.Validate(type, x, y, rotation);
                        if (!validation.CanPlace)
                        {
                            continue;
                        }

                        var candidate = new PlacementCandidate(x, y, rotation, validation);
                        if (validation.Warnings.IsDefaultOrEmpty)
                        {
                            return candidate;
                        }

                        if (!validation.Has(PlacementIssue.LowYield))
                        {
                            firstKeptYield ??= candidate;
                        }
                        else
                        {
                            firstPlaceable ??= candidate;
                        }
                    }
                }
            }
        }

        return firstKeptYield ?? firstPlaceable;
    }

    /// <summary>
    /// Lists placeable spots in spiral order, for callers that score candidates themselves (such as service
    /// coverage). The order and the validation are the same as <see cref="Find"/>: rings of ascending Chebyshev
    /// distance, then rows, columns and rotations, skipping out-of-bounds and excluded spots.
    /// </summary>
    /// <param name="simulation">The simulation, read between ticks.</param>
    /// <param name="type">The building type to place.</param>
    /// <param name="anchorX">The column of the anchor rectangle's top-left tile; the spiral starts here.</param>
    /// <param name="anchorY">The row of the anchor rectangle's top-left tile.</param>
    /// <param name="baseWidth">The footprint width before rotation.</param>
    /// <param name="baseHeight">The footprint height before rotation.</param>
    /// <param name="maxRadius">How far from the anchor to look, in tiles.</param>
    /// <param name="maxCount">How many candidates to collect at most; positive.</param>
    /// <param name="isExcluded">Skips a candidate origin and rotation when it returns <see langword="true"/>; <see langword="null"/> excludes nothing.</param>
    /// <returns>Up to <paramref name="maxCount"/> placeable candidates in spiral order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRadius"/> is negative.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is not positive.</exception>
    public static List<PlacementCandidate> FindAll(
        ISimulation simulation,
        BuildingTypeId type,
        int anchorX,
        int anchorY,
        int baseWidth,
        int baseHeight,
        int maxRadius,
        int maxCount,
        Func<int, int, Rotation, bool>? isExcluded = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        List<PlacementCandidate> found = [];
        int mapWidth = simulation.Query.Map.Width;
        int mapHeight = simulation.Query.Map.Height;
        for (int ring = 0; ring <= maxRadius && found.Count < maxCount; ring++)
        {
            for (int dy = -ring; dy <= ring && found.Count < maxCount; dy++)
            {
                for (int dx = -ring; dx <= ring && found.Count < maxCount; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    int x = anchorX + dx;
                    int y = anchorY + dy;
                    for (int r = 0; r < 4 && found.Count < maxCount; r++)
                    {
                        var rotation = (Rotation)r;
                        int width = rotation.SwapsAxes() ? baseHeight : baseWidth;
                        int height = rotation.SwapsAxes() ? baseWidth : baseHeight;
                        if (x < 0 || y < 0 || x + width > mapWidth || y + height > mapHeight)
                        {
                            continue;
                        }

                        if (isExcluded is not null && isExcluded(x, y, rotation))
                        {
                            continue;
                        }

                        PlacementResult validation = simulation.Query.Placement.Validate(type, x, y, rotation);
                        if (validation.CanPlace)
                        {
                            found.Add(new PlacementCandidate(x, y, rotation, validation));
                        }
                    }
                }
            }
        }

        return found;
    }
}
