using System.Numerics;

namespace Evenmere.Game.Rendering.Terrain;

/// <summary>
/// Which water a tile holds, as the view classifies it. The simulation owns the real data (<c>TileInfo</c>); this is the
/// view's coarse bucket for heights and colours. Land covers every walkable terrain, including beaches and marshes.
/// </summary>
public enum WaterKind : byte
{
    /// <summary>A land tile, drawn at its elevation height.</summary>
    Land = 0,

    /// <summary>Open sea, drawn as a sea floor below the water plane.</summary>
    Sea = 1,

    /// <summary>A reef shelf, drawn just below the water plane.</summary>
    Reef = 2,

    /// <summary>Deep water, drawn as a darker, deeper sea floor.</summary>
    Deep = 3,

    /// <summary>A river tile, drawn as a lowered bed with a strip mesh on top (built by the water renderer).</summary>
    River = 4,
}

/// <summary>
/// The heights of the rendered surface, shared by the chunk builder, the height lookup for picking and the water plane,
/// so the three can never disagree. All values are world units; one tile is one unit wide.
/// </summary>
public static class TerrainSurface
{
    /// <summary>World units of height per elevation step (docs/design/map-format.md: 0.5).</summary>
    public const float ElevationStep = 0.5f;

    /// <summary>Height of the sea plane.</summary>
    public const float WaterSurfaceY = -0.1f;

    /// <summary>How far below <see cref="WaterSurfaceY"/> the sea and deep-water floor sits.</summary>
    public const float SeaFloorDrop = 0.35f;

    /// <summary>How far below <see cref="WaterSurfaceY"/> the reef shelf sits.</summary>
    public const float ReefShelfDrop = 0.15f;

    /// <summary>
    /// How far below its tile's ground height a riverbed sits. Corner averaging halves the step, so the bed reads
    /// about half of this below the banks.
    /// </summary>
    public const float RiverBedDrop = 0.9f;

    /// <summary>
    /// How far above a carved river corner the flowing strip rides: a thin film over the bed, far below the
    /// banks (the carve in <see cref="EffectiveRiverBed"/> keeps the bed at least <see cref="RiverChannelDepth"/>
    /// below the nearby banks, so the lift always leaves a freeboard of at least the depth minus the lift). The
    /// film is thin on purpose: from street zoom any thicker slab reads as floating panels with side walls where
    /// the banks fall away, while the shared triangulation keeps it safely above the bed everywhere.
    /// </summary>
    public const float RiverStripLift = 0.12f;

    /// <summary>
    /// How far below the lowest nearby land a riverbed is carved (M2-37): a perched channel whose bed stands above
    /// its banks reads as floating panels from street zoom, so the effective bed drops to at least this depth below
    /// the neighbouring banks. Where the banks already stand higher the tile keeps its own lowered bed, so valley
    /// rivers are pixel-identical to before. On asymmetric banks (a hillside: high uphill, low downhill) the cap in
    /// <see cref="RiverCarveCapDepth"/> keeps the wall low instead of plunging to the downhill grade.
    /// </summary>
    public const float RiverChannelDepth = 0.6f;

    /// <summary>
    /// How far below the highest nearby land a riverbed may be carved at most (M2-38): on a hillside the downhill
    /// grade can sit far below the channel, and carving the full <see cref="RiverChannelDepth"/> to it would cut
    /// a tall pale cliff beside the water. The cap keeps the cut a bank, not a cliff; the smoothing below then
    /// lays the ribbon over it. Where the banks agree the cap never binds, so valley rivers are pixel-identical
    /// to before.
    /// </summary>
    public const float RiverCarveCapDepth = 1.0f;

    /// <summary>
    /// How far above the carved bed a bank corner stands at its lowest: where the channel runs above its bank
    /// (a contour stream on a slope, or a sill at a river mouth), the bank rises to this height over the bed, like a
    /// natural levee lip. Valley banks stand far higher and never feel it. Together with
    /// <see cref="RiverBankFreeboard"/> it proves the water always sits visibly in its channel: the bank stands at
    /// least the levee (0.25) over the bed while the water rides one freeboard (0.15) under the bank — and
    /// where the bank stands lower, the water drops to <see cref="RiverWaterAboveBedMin"/> (0.05) over the bed,
    /// which the levee still clears by 0.2. So the bank stands above the water by at least the freeboard
    /// everywhere, the 0.15 the eye needs.
    /// </summary>
    public const float RiverBankLevee = 0.25f;

    /// <summary>
    /// How far above the carved bed the water surface stays at its lowest: the strip never dips into the bed,
    /// even where the bank clamp below binds.
    /// </summary>
    public const float RiverWaterAboveBedMin = 0.05f;

    /// <summary>
    /// How far below the bank the water surface rides: the strip sits just under the bank edge, so even a
    /// one-tile slot reads as a blue ribbon with a thin ink line on each bank instead of a dark slot. The visible
    /// bank wall then runs only from the bank edge down to the water; below the waterline the wall is hidden
    /// behind the opaque strip anyway. Where the bank stands lower (a sill), the water drops to
    /// <see cref="RiverWaterAboveBedMin"/> over the bed instead, and the levee (<see cref="RiverBankLevee"/>)
    /// keeps the bank above even that — so the bank clears the water by at least this freeboard everywhere,
    /// the 0.15 the eye needs.
    /// </summary>
    public const float RiverBankFreeboard = 0.15f;

    /// <summary>
    /// What the riverbed rules read from a tile grid, without owning any layout: the one shared source for the
    /// terrain mesh, the strip, the height lookup and the ground sampler, so the four can never disagree and the
    /// rule itself is never copied. Implemented as a <see langword="struct"/> over the caller's arrays, so baking
    /// allocates nothing but the caller's own output.
    /// </summary>
    public interface IRiverBedTiles
    {
        /// <summary>Gets the grid width in tiles.</summary>
        int Width { get; }

        /// <summary>Gets the grid height in tiles.</summary>
        int Height { get; }

        /// <summary>Gets whether tile (x, y) carries a river.</summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row.</param>
        /// <returns><see langword="true"/> on river tiles.</returns>
        bool IsRiver(int x, int y);

        /// <summary>Gets whether tile (x, y) is a bank-forming land tile (never sea, reef or deep water).</summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row.</param>
        /// <returns><see langword="true"/> on land tiles (rivers excluded).</returns>
        bool IsLand(int x, int y);

        /// <summary>Gets the base height tile (x, y) contributes: its own lowered bed on rivers, its ground on land.</summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row.</param>
        /// <returns>The height in world units.</returns>
        float BaseHeight(int x, int y);
    }

    /// <summary>
    /// Gets the effective bed one river tile contributes to shared corners: its own lowered bed, deepened when the
    /// nearby banks sit lower, so the channel always drops below its banks and the strip can never ride above them.
    /// The cap keeps hillside cuts low: never deeper than <see cref="RiverCarveCapDepth"/> below the highest
    /// nearby land. Where the banks agree the cap never binds.
    /// </summary>
    /// <param name="ownBed">The tile's own bed (<see cref="MeshTopHeight"/> with <see cref="WaterKind.River"/>).</param>
    /// <param name="nearbyMinLand">The lowest land base height in the tile's neighbourhood, or positive infinity when no land is near.</param>
    /// <param name="nearbyMaxLand">The highest land base height in the tile's neighbourhood, or positive infinity when no land is near.</param>
    /// <returns>The bed height this tile contributes to shared corners.</returns>
    public static float EffectiveRiverBed(float ownBed, float nearbyMinLand, float nearbyMaxLand)
    {
        if (float.IsPositiveInfinity(nearbyMinLand))
        {
            return ownBed;
        }

        float target = Math.Min(ownBed, nearbyMinLand - RiverChannelDepth);
        return Math.Min(ownBed, Math.Max(target, nearbyMaxLand - RiverCarveCapDepth));
    }

    /// <summary>
    /// Bakes the smoothed bed every river tile contributes to shared corners, over a whole grid at once: the
    /// effective bed (<see cref="EffectiveRiverBed"/>) averaged with the river tiles around it, so the water runs
    /// downhill as one continuous ribbon instead of flat panels with steps between them. Averaging preserves flat
    /// runs and straight ramps exactly, so valley rivers come out pixel-identical; only kinks and steps relax.
    /// Off-grid and non-river tiles read 0 and are ignored by the corner rules.
    /// </summary>
    /// <param name="tiles">The tile grid.</param>
    /// <param name="smoothedOut">Receives the smoothed bed per tile; sized at least width × height.</param>
    public static void BakeRiverBeds<T>(T tiles, float[] smoothedOut)
        where T : struct, IRiverBedTiles
    {
        for (int y = 0; y < tiles.Height; y++)
        {
            for (int x = 0; x < tiles.Width; x++)
            {
                smoothedOut[(y * tiles.Width) + x] = tiles.IsRiver(x, y) ? SmoothedBedForTile(tiles, x, y) : 0f;
            }
        }
    }

    /// <summary>
    /// Gets one river tile's smoothed bed: its effective bed averaged with the effective beds of the river tiles
    /// around it. Shares the bake above tile by tile, for corners sampled on demand.
    /// </summary>
    /// <param name="tiles">The tile grid.</param>
    /// <param name="x">The river tile's column.</param>
    /// <param name="y">The river tile's row.</param>
    /// <returns>The smoothed bed in world units.</returns>
    public static float SmoothedBedForTile<T>(T tiles, int x, int y)
        where T : struct, IRiverBedTiles
    {
        float sum = 0f;
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= tiles.Width || ny >= tiles.Height || !tiles.IsRiver(nx, ny))
                {
                    continue;
                }

                sum += EffectiveBedOf(tiles, nx, ny);
                count++;
            }
        }

        return count == 0 ? 0f : sum / count;
    }

    // One tile's effective bed from the shared rule: its own bed deepened to the neighbourhood banks.
    private static float EffectiveBedOf<T>(T tiles, int x, int y)
        where T : struct, IRiverBedTiles
    {
        float ownBed = tiles.BaseHeight(x, y);
        float landMin = float.PositiveInfinity;
        float landMax = float.NegativeInfinity;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= tiles.Width || ny >= tiles.Height || !tiles.IsLand(nx, ny))
                {
                    continue;
                }

                float ground = tiles.BaseHeight(nx, ny);
                landMin = Math.Min(landMin, ground);
                landMax = Math.Max(landMax, ground);
            }
        }

        return EffectiveRiverBed(ownBed, landMin, landMax);
    }

    /// <summary>
    /// Gets the strip height at one shared corner: the bank height minus the freeboard, so the water rides just
    /// under the bank edge (M2-38 round 3: a thin film far below the banks hid down the slot and read as dark
    /// ink diamonds at distance). Clamped to stay above the bed, so the strip never dips into the channel floor.
    /// The corners stay shared, so the water runs unbroken from source to mouth.
    /// </summary>
    /// <param name="bedCorner">The carved corner height (the riverbed surface there).</param>
    /// <param name="landHeight">The land mesh height at the corner (<see cref="LandCornerHeight"/>), or positive infinity when no bank shares it.</param>
    /// <returns>The strip height at the corner.</returns>
    public static float StripCornerHeight(float bedCorner, float landHeight)
    {
        if (float.IsPositiveInfinity(landHeight))
        {
            return bedCorner + RiverStripLift;
        }

        // Just under the bank; the levee keeps degenerate banks above even the bed minimum, so the gate holds.
        return Math.Max(landHeight - RiverBankFreeboard, bedCorner + RiverWaterAboveBedMin);
    }

    /// <summary>
    /// Gets the fog drape height at one tile corner: the highest surface the terrain mesh draws there (the bank
    /// or the carved bed, whichever stands taller), plus the caller's lift — so the haze always rides above the
    /// visible mesh, including bank walls and levee lips. It reads the same <see cref="LandCornerHeight"/> bank
    /// rule and <see cref="ResolveCorner"/> bed rule as the chunk mesh, never its own copy.
    /// </summary>
    /// <param name="first">The base height (<see cref="MeshTopHeight"/>) of the (−x, −y) sharing tile.</param>
    /// <param name="second">The base height of the (+x, −y) sharing tile.</param>
    /// <param name="third">The base height of the (−x, +y) sharing tile.</param>
    /// <param name="fourth">The base height of the (+x, +y) sharing tile.</param>
    /// <param name="existMask">Bit <c>i</c> set when sharing tile <c>i</c> exists (is on the map), in the same order.</param>
    /// <param name="riverMask">Bit <c>i</c> set when sharing tile <c>i</c> is a river tile, in the same order.</param>
    /// <param name="lift">How far the haze rides above the surface.</param>
    /// <returns>The drape height at the corner, in world units.</returns>
    public static float FogCornerHeight(float first, float second, float third, float fourth, int existMask, int riverMask, float lift)
    {
        float bank = LandCornerHeight(first, second, third, fourth, existMask, riverMask);
        float bed = ResolveCorner(first, second, third, fourth, riverMask, BitOperations.PopCount((uint)existMask));
        return Math.Max(bank, bed) + lift;
    }

    /// <summary>Gets the ground height of an elevation step count.</summary>
    /// <param name="elevation">The elevation, 0 to 15.</param>
    /// <returns>The height in world units.</returns>
    public static float GroundHeight(int elevation) => elevation * ElevationStep;

    /// <summary>Gets the base height one tile contributes to its corners (ground, sea floor, reef shelf or riverbed).</summary>
    /// <param name="elevation">The elevation, 0 to 15.</param>
    /// <param name="kind">The water kind.</param>
    /// <returns>The mesh top in world units.</returns>
    public static float MeshTopHeight(int elevation, WaterKind kind) => kind switch
    {
        WaterKind.Sea => WaterSurfaceY - SeaFloorDrop,
        WaterKind.Deep => WaterSurfaceY - SeaFloorDrop,
        WaterKind.Reef => WaterSurfaceY - ReefShelfDrop,
        WaterKind.River => GroundHeight(elevation) - RiverBedDrop,
        _ => GroundHeight(elevation),
    };

    /// <summary>
    /// Resolves one tile corner for a non-river tile: the average of the sharing non-river base heights, raised to
    /// a levee (<see cref="RiverBankLevee"/>) over the carved bed where a river shares the corner, so a bank
    /// keeps its own height at the river edge — and where the channel runs above its bank (a contour stream on a
    /// slope), the bank still stands visibly above the water instead of drowning under the strip. (M2-38: shared
    /// carved corners put the land mesh below the water surface, so the strip read as a floating slab.) River tiles
    /// next door resolve through <see cref="ResolveCorner"/> (the carved bed), and a bank wall in the chunk mesh
    /// spans the gap between the two heights. When no sharing tile is a bank (a vertex fully inside a river), there
    /// is no bank to stand on, so it falls back to <see cref="ResolveCorner"/> (the carved bed), which is defined
    /// whenever a river shares it.
    /// </summary>
    /// <param name="first">The base height (<see cref="MeshTopHeight"/>) of the (−x, −y) sharing tile.</param>
    /// <param name="second">The base height of the (+x, −y) sharing tile.</param>
    /// <param name="third">The base height of the (−x, +y) sharing tile.</param>
    /// <param name="fourth">The base height of the (+x, +y) sharing tile.</param>
    /// <param name="existMask">Bit <c>i</c> set when sharing tile <c>i</c> exists (is on the map), in the same order.</param>
    /// <param name="riverMask">Bit <c>i</c> set when sharing tile <c>i</c> is a river tile, in the same order.</param>
    /// <returns>The bank corner height in world units.</returns>
    public static float LandCornerHeight(float first, float second, float third, float fourth, int existMask, int riverMask)
    {
        float sum = 0f;
        int count = 0;
        if ((existMask & 1) != 0 && (riverMask & 1) == 0)
        {
            sum += first;
            count++;
        }

        if ((existMask & 2) != 0 && (riverMask & 2) == 0)
        {
            sum += second;
            count++;
        }

        if ((existMask & 4) != 0 && (riverMask & 4) == 0)
        {
            sum += third;
            count++;
        }

        if ((existMask & 8) != 0 && (riverMask & 8) == 0)
        {
            sum += fourth;
            count++;
        }

        if (count == 0)
        {
            return ResolveCorner(first, second, third, fourth, riverMask, BitOperations.PopCount((uint)existMask));
        }

        float bank = sum / count;
        if ((riverMask & existMask) != 0)
        {
            // A river shares the corner: the levee keeps the bank above the channel it borders.
            float bed = ResolveCorner(first, second, third, fourth, riverMask, BitOperations.PopCount((uint)existMask));
            bank = Math.Max(bank, bed + RiverBankLevee);
        }

        return bank;
    }

    /// <summary>
    /// Resolves one tile corner for a river tile (the channel surface): the lowest bed height among the sharing
    /// river tiles, so the channel runs flat and continuous, and mouths cut into the sea floor. Non-river tiles
    /// resolve through <see cref="LandCornerHeight"/> instead, so the two surfaces meet at a bank wall rather
    /// than a shared carved corner. A corner past the map edge is skipped by the caller.
    /// </summary>
    /// <param name="first">The base height (<see cref="MeshTopHeight"/>) of the (−x, −y) sharing tile.</param>
    /// <param name="second">The base height of the (+x, −y) sharing tile.</param>
    /// <param name="third">The base height of the (−x, +y) sharing tile.</param>
    /// <param name="fourth">The base height of the (+x, +y) sharing tile.</param>
    /// <param name="riverMask">Bit <c>i</c> set when sharing tile <c>i</c> is a river tile, in the same order.</param>
    /// <param name="count">How many of the four tiles exist (1 to 4); the rest are ignored.</param>
    /// <returns>The corner height in world units.</returns>
    public static float ResolveCorner(float first, float second, float third, float fourth, int riverMask, int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        if (riverMask != 0)
        {
            float bed = float.MaxValue;
            if ((riverMask & 1) != 0)
            {
                bed = Math.Min(bed, first);
            }

            if ((riverMask & 2) != 0)
            {
                bed = Math.Min(bed, second);
            }

            if ((riverMask & 4) != 0)
            {
                bed = Math.Min(bed, third);
            }

            if ((riverMask & 8) != 0)
            {
                bed = Math.Min(bed, fourth);
            }

            return bed;
        }

        return (first + second + third + fourth) / count;
    }

    /// <summary>
    /// Gets the normal of the triangle (A, B, C) wound clockwise viewed from above (Godot front faces): the
    /// right-hand normal of (C−A)×(B−A), flipped to point up on principle and normalised, or straight up when the
    /// triangle is degenerate.
    /// </summary>
    public static void FaceNormal(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        out float nx, out float ny, out float nz)
    {
        float e1x = bx - ax;
        float e1y = by - ay;
        float e1z = bz - az;
        float e2x = cx - ax;
        float e2y = cy - ay;
        float e2z = cz - az;

        nx = (e2y * e1z) - (e2z * e1y);
        ny = (e2z * e1x) - (e2x * e1z);
        nz = (e2x * e1y) - (e2y * e1x);
        if (ny < 0f)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }

        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length > 1e-9f)
        {
            nx /= length;
            ny /= length;
            nz /= length;
        }
        else
        {
            nx = 0f;
            ny = 1f;
            nz = 0f;
        }
    }

    /// <summary>Classifies a tile from its content terrain name and river flag.</summary>
    /// <param name="terrainName">The content terrain name, such as <c>plains</c>.</param>
    /// <param name="isRiver">Whether the tile carries the river feature.</param>
    /// <returns>The view's water bucket.</returns>
    public static WaterKind Classify(string terrainName, bool isRiver)
    {
        if (isRiver || terrainName == "river")
        {
            return WaterKind.River;
        }

        return terrainName switch
        {
            "sea" => WaterKind.Sea,
            "deep_water" => WaterKind.Deep,
            "reef" => WaterKind.Reef,
            _ => WaterKind.Land,
        };
    }
}
