using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The one placement check for buildings: the <c>PlaceBuilding</c> handler and the ghost preview
/// (<c>IPlacementQueries.Validate</c>, through <c>PlacementQueries</c>) both call <see cref="Validate"/>, so the preview
/// never promises what the command refuses. Every rule is read from the building's <c>placement</c> data and the terrain
/// definitions; no rule names a building.
/// </summary>
/// <remarks>
/// <para>
/// Errors block placement and are all reported, in the order the rules are checked: out of bounds, occupied, terrain not
/// allowed, terrain access locked, slope too steep, not unlocked, unique already built, needs coastline, needs riverside,
/// needs deposit, fertility too low. Warnings allow it and are reported only for a placement that has no error.
/// </para>
/// <para>
/// The reach and yield warnings are estimates made from the same queries the rest of the simulation reads: the roads
/// system answers reach for buildings and for footprints that are not built yet, and Production answers yield for
/// buildings that exist (see <see cref="EstimateYield"/> for the ghost's share).
/// </para>
/// </remarks>
/// <param name="content">The immutable content.</param>
/// <param name="state">Construction's own state, to see upgrades under way (a unique building being upgraded to counts as built).</param>
/// <param name="map">The map queries.</param>
/// <param name="roads">The roads queries.</param>
/// <param name="progression">The progression queries.</param>
internal sealed class BuildingPlacement(
    ContentDb content, ConstructionState state, IMapQueries map, IRoadsQueries roads, IProgressionQueries progression)
    : IBuildingPlacement
{
    /// <summary>The steepest slope class a building may stand on. No building declares its own limit in M1, so this is the default for all of them.</summary>
    public const SlopeClass MaxSlope = SlopeClass.Gentle;

    /// <summary>The area yield, in per-mille, below which placement warns about a low yield.</summary>
    public const int LowYieldBelow = 500;

    /// <summary>Creates the check over what a command handler has at hand.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="state">Construction's state.</param>
    /// <returns>The check, which reads the live queries and so sees every earlier command of the tick.</returns>
    public static BuildingPlacement For(SimContext ctx, ConstructionState state) =>
        new(ctx.Content, state, ctx.Queries.Map, ctx.Queries.Roads, ctx.Queries.Progression);

    /// <summary>Tests whether a building type id belongs to the loaded content.</summary>
    /// <param name="type">The id.</param>
    /// <returns><see langword="true"/> if the content defines the type.</returns>
    public bool IsKnown(BuildingTypeId type) => type.Value < content.Buildings.Count;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The type is not in the content, or the rotation is not 0 to 3. The command handler checks both first.</exception>
    public PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation)
    {
        if (!IsKnown(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The building type is not in the content.");
        }

        if (!rotation.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "A rotation is 0 to 3 quarter turns.");
        }

        BuildingDef def = content.Get(type);
        PlacementSpec spec = def.Placement;
        Footprint size = rotation.Rotate(def.Footprint);
        int area = size.Width * size.Height;

        var offMap = new List<TileCoord>();
        var occupied = new List<TileCoord>();
        var notAllowed = new List<TileCoord>();
        var locked = new List<TileCoord>();
        var tooSteep = new List<TileCoord>();
        var infertile = new List<TileCoord>();
        int coastTiles = 0;
        int depositTiles = 0;
        int fertileTiles = 0;

        // One pass over the footprint, in (Y, X) order so every list is already in the order the findings promise.
        for (int dy = 0; dy < size.Height; dy++)
        {
            for (int dx = 0; dx < size.Width; dx++)
            {
                var at = new TileCoord(x + dx, y + dy);
                if (!at.IsInside(map.Width, map.Height))
                {
                    offMap.Add(at);
                    continue;
                }

                if (!map.BuildingAt(at.X, at.Y).IsNone || roads.RoadAt(at.X, at.Y) is not null)
                {
                    occupied.Add(at);
                }

                TileInfo tile = map.GetTile(at.X, at.Y);
                TerrainDef terrain = content.Get(tile.Terrain);

                // A terrain list, when the building gives one, decides on its own (so a deep-water building can list deep water);
                // without one the building may stand on any buildable terrain. Only a terrain the building may otherwise use
                // can be "locked", so the player is never told to research something that would not help.
                bool allowed = spec.Terrain.IsDefaultOrEmpty ? terrain.Buildable : spec.Terrain.Contains(tile.Terrain);
                if (!allowed)
                {
                    notAllowed.Add(at);
                }
                else if (terrain.Access is { } access && !progression.HasAccess(access))
                {
                    locked.Add(at);
                }

                if (tile.Slope > MaxSlope)
                {
                    tooSteep.Add(at);
                }

                if (tile.Has(TileFeatures.Coast))
                {
                    coastTiles++;
                }

                if (spec.Deposit is { } deposit && tile.Deposit == deposit)
                {
                    depositTiles++;
                }

                if (spec.Fertility is { } fertility)
                {
                    if (tile.IsFertileFor(fertility.Crop))
                    {
                        fertileTiles++;
                    }
                    else
                    {
                        infertile.Add(at);
                    }
                }
            }
        }

        ImmutableArray<TileCoord> whole = Footprint(x, y, size);
        ImmutableArray<PlacementFinding>.Builder errors = ImmutableArray.CreateBuilder<PlacementFinding>();
        AddIfAny(errors, PlacementIssue.OutOfBounds, offMap);
        AddIfAny(errors, PlacementIssue.Occupied, occupied);
        AddIfAny(errors, PlacementIssue.TerrainNotAllowed, notAllowed);
        AddIfAny(errors, PlacementIssue.TerrainAccessLocked, locked);
        AddIfAny(errors, PlacementIssue.SlopeTooSteep, tooSteep);

        if (!progression.IsUnlocked(type))
        {
            errors.Add(new PlacementFinding(PlacementIssue.NotUnlocked, whole));
        }

        if (def.Unique && AlreadyExists(type))
        {
            errors.Add(new PlacementFinding(PlacementIssue.UniqueAlreadyBuilt, whole));
        }

        if (spec.Coastline && coastTiles == 0)
        {
            errors.Add(new PlacementFinding(PlacementIssue.NeedsCoastline, whole));
        }

        if (spec.Riverside && !HasRiverBeside(x, y, size))
        {
            errors.Add(new PlacementFinding(PlacementIssue.NeedsRiverside, whole));
        }

        if (spec.Deposit is not null && depositTiles == 0)
        {
            errors.Add(new PlacementFinding(PlacementIssue.NeedsDeposit, whole));
        }

        // The share is compared as fertile × 100 < min × area, so no division rounds the answer. Off-map tiles are never fertile.
        if (spec.Fertility is { } required && fertileTiles * 100 < required.MinPercent * area)
        {
            errors.Add(new PlacementFinding(PlacementIssue.FertilityTooLow, infertile.Count > 0 ? [.. infertile] : whole));
        }

        if (errors.Count > 0)
        {
            return new PlacementResult(errors.ToImmutable(), []);
        }

        ImmutableArray<PlacementFinding>.Builder warnings = ImmutableArray.CreateBuilder<PlacementFinding>();
        if (spec.RoadAccess)
        {
            var accessTiles = new List<TileCoord>();
            AdjacentRoads(x, y, size, accessTiles);
            if (accessTiles.Count == 0)
            {
                warnings.Add(new PlacementFinding(PlacementIssue.NoRoadAccess, whole));
            }
            else if (!roads.IsFootprintInStorehouseReach(x, y, size.Width, size.Height))
            {
                warnings.Add(new PlacementFinding(PlacementIssue.OutsideStorehouseReach, whole));
            }
        }

        if (spec.AreaYield is { } yield && EstimateYield(yield, type, x, y, size, fertileTiles, depositTiles) < LowYieldBelow)
        {
            warnings.Add(new PlacementFinding(PlacementIssue.LowYield, whole));
        }

        return warnings.Count == 0 ? PlacementResult.Ok : new PlacementResult([], warnings.ToImmutable());
    }

    /// <summary>Lists the tiles of a footprint in ascending (Y, X) order.</summary>
    /// <param name="x">The column of the top-left tile.</param>
    /// <param name="y">The row of the top-left tile.</param>
    /// <param name="size">The footprint after rotation.</param>
    /// <returns>The tiles, off-map ones included.</returns>
    public static ImmutableArray<TileCoord> Footprint(int x, int y, Footprint size)
    {
        ImmutableArray<TileCoord>.Builder tiles = ImmutableArray.CreateBuilder<TileCoord>(size.Width * size.Height);
        for (int dy = 0; dy < size.Height; dy++)
        {
            for (int dx = 0; dx < size.Width; dx++)
            {
                tiles.Add(new TileCoord(x + dx, y + dy));
            }
        }

        return tiles.MoveToImmutable();
    }

    /// <summary>Collects the road tiles that touch a footprint along an edge (its access tiles), in ascending (Y, X) order.</summary>
    /// <param name="x">The column of the top-left tile.</param>
    /// <param name="y">The row of the top-left tile.</param>
    /// <param name="size">The footprint after rotation.</param>
    /// <param name="found">Receives the tiles; it is not cleared first.</param>
    internal void AdjacentRoads(int x, int y, Footprint size, List<TileCoord> found)
    {
        // The ring around the footprint without its four corners: a road that touches only a corner gives no access.
        for (int ty = y - 1; ty <= y + size.Height; ty++)
        {
            for (int tx = x - 1; tx <= x + size.Width; tx++)
            {
                bool outsideColumns = tx < x || tx >= x + size.Width;
                bool outsideRows = ty < y || ty >= y + size.Height;
                if (outsideColumns == outsideRows)
                {
                    // Both outside is a corner and neither outside is the footprint itself.
                    continue;
                }

                if (roads.RoadAt(tx, ty) is not null)
                {
                    found.Add(new TileCoord(tx, ty));
                }
            }
        }
    }

    private static void AddIfAny(ImmutableArray<PlacementFinding>.Builder findings, PlacementIssue issue, List<TileCoord> tiles)
    {
        if (tiles.Count > 0)
        {
            findings.Add(new PlacementFinding(issue, [.. tiles]));
        }
    }

    /// <summary>
    /// Tests whether a building of a type exists, which is what "unique" forbids a second of. Every state counts, because a
    /// site or a paused building is still built as far as a unique building goes; so does an upgrade under way whose target
    /// is the type. A ruin keeps a type id that no longer means anything, so it is skipped.
    /// </summary>
    /// <param name="type">The building type.</param>
    /// <returns><see langword="true"/> if one exists or is being upgraded into.</returns>
    internal bool AlreadyExists(BuildingTypeId type)
    {
        foreach (BuildingInfo building in map.Buildings)
        {
            if (building.Type == type && building.State != BuildingState.Ruin)
            {
                return true;
            }
        }

        foreach (IdTable<SiteRecord>.Entry site in state.Sites)
        {
            if (site.Value.Kind == SiteKind.Upgrade && site.Value.Target == type)
            {
                return true;
            }
        }

        return false;
    }

    // "Beside a river" means a river tile touches the footprint along an edge (or lies under it, for terrain lists that allow it).
    private bool HasRiverBeside(int x, int y, Footprint size)
    {
        for (int ty = y - 1; ty <= y + size.Height; ty++)
        {
            for (int tx = x - 1; tx <= x + size.Width; tx++)
            {
                bool corner = (tx < x || tx >= x + size.Width) && (ty < y || ty >= y + size.Height);
                if (!corner && new TileCoord(tx, ty).IsInside(map.Width, map.Height) && map.GetTile(tx, ty).Has(TileFeatures.River))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Estimates the area yield a gatherer would have at a place, in per-mille and capped at 1000. This duplicates in
    /// simplified form what Production (M1-08) computes for buildings that exist: the share of fertile or deposit tiles under
    /// the footprint, or the number of counted tiles in the footprint widened by the radius over the <c>fullAt</c> count.
    /// An exclusive yield skips tiles inside the window of any building of the same type, which is what a building placed
    /// last (with the highest ID) loses to the lower IDs. Production stays authoritative; this only drives the warning.
    /// </summary>
    private int EstimateYield(AreaYieldSpec spec, BuildingTypeId type, int x, int y, Footprint size, int fertileTiles, int depositTiles)
    {
        int area = size.Width * size.Height;
        switch (spec.Kind)
        {
            case AreaYieldKind.FootprintFertility:
                return fertileTiles * 1000 / area;
            case AreaYieldKind.FootprintDeposit:
                return depositTiles * 1000 / area;
            case AreaYieldKind.Radius:
                break;
            default:
                return 1000;
        }

        var claimed = new List<(int MinX, int MinY, int MaxX, int MaxY)>();
        if (spec.Exclusive)
        {
            foreach (BuildingInfo other in map.Buildings)
            {
                if (other.Type == type && other.State != BuildingState.Ruin)
                {
                    claimed.Add((other.Origin.X - spec.Radius, other.Origin.Y - spec.Radius,
                        other.Origin.X + other.Width - 1 + spec.Radius, other.Origin.Y + other.Height - 1 + spec.Radius));
                }
            }
        }

        int counted = 0;
        int lastX = x + size.Width - 1;
        int lastY = y + size.Height - 1;
        for (int ty = Math.Max(0, y - spec.Radius); ty <= Math.Min(map.Height - 1, lastY + spec.Radius); ty++)
        {
            for (int tx = Math.Max(0, x - spec.Radius); tx <= Math.Min(map.Width - 1, lastX + spec.Radius); tx++)
            {
                bool underFootprint = tx >= x && tx <= lastX && ty >= y && ty <= lastY;
                if (!underFootprint && !IsClaimed(claimed, tx, ty) && spec.Terrain.Contains(map.GetTile(tx, ty).Terrain))
                {
                    counted++;
                }
            }
        }

        return Math.Min(1000, counted * 1000 / spec.FullAt);
    }

    private static bool IsClaimed(List<(int MinX, int MinY, int MaxX, int MaxY)> claimed, int x, int y)
    {
        foreach ((int minX, int minY, int maxX, int maxY) in claimed)
        {
            if (x >= minX && x <= maxX && y >= minY && y <= maxY)
            {
                return true;
            }
        }

        return false;
    }
}
