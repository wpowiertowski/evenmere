using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Production;

/// <summary>The tiles a building's footprint covers, as an inclusive rectangle.</summary>
/// <param name="MinX">The leftmost column.</param>
/// <param name="MinY">The top row.</param>
/// <param name="MaxX">The rightmost column.</param>
/// <param name="MaxY">The bottom row.</param>
internal readonly record struct TileRect(int MinX, int MinY, int MaxX, int MaxY)
{
    /// <summary>Gets the rectangle of a building's footprint.</summary>
    /// <param name="building">The building.</param>
    /// <returns>The rectangle.</returns>
    public static TileRect Of(in BuildingInfo building) =>
        new(building.Origin.X, building.Origin.Y, building.Origin.X + building.Width - 1, building.Origin.Y + building.Height - 1);

    /// <summary>Gets the squared Euclidean distance, in tiles, from a tile to the nearest tile of the rectangle (0 inside it).</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <returns>The squared distance.</returns>
    public int DistanceSquaredTo(int x, int y)
    {
        int dx = x < MinX ? MinX - x : (x > MaxX ? x - MaxX : 0);
        int dy = y < MinY ? MinY - y : (y > MaxY ? y - MaxY : 0);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Gets the Chebyshev gap between two rectangles: the larger of the empty columns and the empty rows between them (0 if they touch or overlap).</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The gap in tiles.</returns>
    public int GapTo(TileRect other)
    {
        int gapX = Math.Max(0, Math.Max(MinX - other.MaxX, other.MinX - MaxX));
        int gapY = Math.Max(0, Math.Max(MinY - other.MaxY, other.MinY - MaxY));
        return Math.Max(gapX, gapY);
    }
}

/// <summary>
/// Reads the map around a producer to answer the two questions that only change when buildings come and go: how much
/// its surroundings yield, and which of its site bonuses hold. The results are cached in the producer's record and
/// refreshed from the building lifecycle (ADR-0011: terrain is static in M1 and roads do not affect yield).
/// </summary>
/// <remarks>
/// <para>The rules, which are part of the game's behaviour:</para>
/// <list type="bullet">
/// <item><b>Radius.</b> A tile is within a building's radius when its Euclidean distance to the nearest tile of the
/// footprint is at most the radius (<c>dx² + dy² ≤ r²</c>, so the area is the footprint with rounded corners). Only the
/// terrain of the tile counts, whatever stands on it. The yield is the number of matching tiles over <c>fullAt</c>,
/// capped at 1000 per-mille.</item>
/// <item><b>Exclusive claims.</b> With <c>exclusive</c>, a tile is claimed by every finished building of the same type that
/// has a lower entity ID and the tile in its own radius, and a claimed tile does not count. So of two overlapping fishing
/// huts the older keeps all its water and the newer gets only what the older cannot reach. The result depends only on
/// IDs and the map, never on the order buildings were activated in.</item>
/// <item><b>Footprint yields.</b> The share of footprint tiles that are fertile for the crop, or lie over the deposit,
/// as per-mille of the whole footprint.</item>
/// <item><b>Terrain site bonus.</b> It holds when <em>every</em> footprint tile has one of the listed terrains (tiles may
/// mix the listed terrains; a single tile of anything else voids it).</item>
/// <item><b>Near site bonus.</b> It holds when a finished building (not a site, not the building itself) that the selector
/// picks has a footprint tile within the radius of the producer, measured as for the area radius.</item>
/// </list>
/// </remarks>
internal sealed class SiteEvaluator(ContentDb content)
{
    // Reused by every exclusive-yield computation. It is cleared before each use and never read across calls, so it carries no state.
    private readonly List<TileRect> _claimants = [];

    /// <summary>Computes how much a producer's surroundings yield.</summary>
    /// <param name="map">The map.</param>
    /// <param name="building">The producer.</param>
    /// <param name="def">The producer's definition.</param>
    /// <returns>The yield in per-mille, at most 1000; 1000 for a building with no area yield.</returns>
    public Ratio AreaYield(IMapQueries map, in BuildingInfo building, BuildingDef def)
    {
        AreaYieldSpec? spec = def.Placement.AreaYield;
        if (spec is null)
        {
            return Ratio.One;
        }

        return spec.Kind switch
        {
            AreaYieldKind.Radius => RadiusYield(map, building, spec),
            AreaYieldKind.FootprintFertility when def.Placement.Fertility is { } fertility =>
                FootprintYield(map, building, fertility.Crop, null),
            AreaYieldKind.FootprintDeposit when def.Placement.Deposit is { } deposit =>
                FootprintYield(map, building, null, deposit),
            _ => Ratio.One,
        };
    }

    /// <summary>Sums the productivity site bonuses of a producer that hold where it stands.</summary>
    /// <param name="map">The map.</param>
    /// <param name="building">The producer.</param>
    /// <param name="def">The producer's definition.</param>
    /// <returns>The total in percent.</returns>
    public int SiteBonusPercent(IMapQueries map, in BuildingInfo building, BuildingDef def)
    {
        int total = 0;
        foreach (SiteBonusSpec bonus in def.SiteBonuses)
        {
            if (bonus.Stat == Stat.Productivity && Holds(map, building, bonus.When))
            {
                total += bonus.Percent;
            }
        }

        return total;
    }

    /// <summary>Tests whether a change to another building can alter a producer's area yield (only exclusive claims depend on other buildings).</summary>
    /// <param name="def">The producer's definition.</param>
    /// <param name="producer">The producer.</param>
    /// <param name="changed">The building that was activated, removed or retyped.</param>
    /// <param name="previousType">For a retyped building, the type it had before; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the yield should be computed again.</returns>
    public static bool AreaYieldDependsOn(BuildingDef def, in BuildingInfo producer, in BuildingInfo changed, BuildingTypeId? previousType)
    {
        if (def.Placement.AreaYield is not { Kind: AreaYieldKind.Radius, Exclusive: true } spec)
        {
            return false;
        }

        // Claims only pass between buildings of one type, and only when their radii can overlap.
        bool sameType = producer.Type == changed.Type || (previousType is { } previous && producer.Type == previous);
        return sameType && TileRect.Of(producer).GapTo(TileRect.Of(changed)) <= 2 * spec.Radius;
    }

    /// <summary>Tests whether a change to another building can alter a producer's site bonus (only <c>near</c> conditions depend on other buildings).</summary>
    /// <param name="def">The producer's definition.</param>
    /// <param name="producer">The producer.</param>
    /// <param name="changed">The building that was activated, removed or retyped.</param>
    /// <returns><see langword="true"/> if the bonus should be computed again.</returns>
    public static bool SiteBonusDependsOn(BuildingDef def, in BuildingInfo producer, in BuildingInfo changed)
    {
        foreach (SiteBonusSpec bonus in def.SiteBonuses)
        {
            if (bonus.When is NearSite near && TileRect.Of(producer).GapTo(TileRect.Of(changed)) <= near.Radius)
            {
                return true;
            }
        }

        return false;
    }

    private Ratio RadiusYield(IMapQueries map, in BuildingInfo building, AreaYieldSpec spec)
    {
        var self = TileRect.Of(building);
        int radius = spec.Radius;
        int radiusSquared = radius * radius;

        _claimants.Clear();
        if (spec.Exclusive)
        {
            // Buildings come in ascending ID order, so everything before this one has a lower ID.
            foreach (BuildingInfo other in map.ActiveBuildings)
            {
                if (other.Id >= building.Id)
                {
                    break;
                }

                var rect = TileRect.Of(other);
                if (other.Type == building.Type && self.GapTo(rect) <= 2 * radius)
                {
                    _claimants.Add(rect);
                }
            }
        }

        int minX = Math.Max(0, self.MinX - radius);
        int maxX = Math.Min(map.Width - 1, self.MaxX + radius);
        int minY = Math.Max(0, self.MinY - radius);
        int maxY = Math.Min(map.Height - 1, self.MaxY + radius);

        int counted = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (self.DistanceSquaredTo(x, y) > radiusSquared
                    || !spec.Terrain.Contains(map.GetTile(x, y).Terrain)
                    || IsClaimed(x, y, radiusSquared))
                {
                    continue;
                }

                counted++;
            }
        }

        return Share(counted, Math.Max(1, spec.FullAt));
    }

    private bool IsClaimed(int x, int y, int radiusSquared)
    {
        foreach (TileRect claimant in _claimants)
        {
            if (claimant.DistanceSquaredTo(x, y) <= radiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    private static Ratio FootprintYield(IMapQueries map, in BuildingInfo building, CropKind? crop, DepositKind? deposit)
    {
        var rect = TileRect.Of(building);
        int matching = 0;
        for (int y = rect.MinY; y <= rect.MaxY; y++)
        {
            for (int x = rect.MinX; x <= rect.MaxX; x++)
            {
                TileInfo tile = map.GetTile(x, y);
                if ((crop is { } c && tile.IsFertileFor(c)) || (deposit is { } d && tile.Deposit == d))
                {
                    matching++;
                }
            }
        }

        return Share(matching, building.Width * building.Height);
    }

    // Per-mille of `whole`, rounded down and capped at 1000.
    private static Ratio Share(int part, int whole) =>
        Ratio.FromPerMille((int)Math.Min(ProductionMath.One, (long)part * ProductionMath.One / whole));

    private bool Holds(IMapQueries map, in BuildingInfo building, SiteCondition condition) => condition switch
    {
        TerrainSite site => OnlyOnTerrain(map, building, site.Terrain),
        NearSite near => IsNear(map, building, near),
        _ => false,
    };

    private static bool OnlyOnTerrain(IMapQueries map, in BuildingInfo building, ImmutableArray<TerrainId> terrains)
    {
        var rect = TileRect.Of(building);
        for (int y = rect.MinY; y <= rect.MaxY; y++)
        {
            for (int x = rect.MinX; x <= rect.MaxX; x++)
            {
                if (!terrains.Contains(map.GetTile(x, y).Terrain))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsNear(IMapQueries map, in BuildingInfo building, NearSite near)
    {
        var self = TileRect.Of(building);
        int radiusSquared = near.Radius * near.Radius;
        int minX = Math.Max(0, self.MinX - near.Radius);
        int maxX = Math.Min(map.Width - 1, self.MaxX + near.Radius);
        int minY = Math.Max(0, self.MinY - near.Radius);
        int maxY = Math.Min(map.Height - 1, self.MaxY + near.Radius);

        EntityId lastSeen = EntityId.None;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (self.DistanceSquaredTo(x, y) > radiusSquared)
                {
                    continue;
                }

                EntityId id = map.BuildingAt(x, y);

                // A building covers several tiles in a row, so skip the one just judged.
                if (id.IsNone || id == building.Id || id == lastSeen)
                {
                    continue;
                }

                lastSeen = id;
                if (map.TryGetBuilding(id, out BuildingInfo other) && other.IsComplete && Selects(near.Selector, other))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool Selects(BuildingSelector selector, in BuildingInfo other)
    {
        BuildingDef def = content.Get(other.Type);
        return selector switch
        {
            BuildingTypeSelector type => other.Type == type.Building,
            BuildingTagSelector tag => def.Tags.Contains(tag.Tag),
            BuildingCategorySelector category => def.Category == category.Category,
            _ => false,
        };
    }
}
