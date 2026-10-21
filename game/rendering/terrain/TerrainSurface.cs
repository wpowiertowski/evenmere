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
    /// How far above a carved river corner the flowing strip sits: above the flat bed by construction, and below
    /// the banks. (A strip below the dipped bank corners would vanish under the averaged bed, so the strip rides
    /// just over the bed instead.)
    /// </summary>
    public const float RiverStripLift = 0.25f;

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
    /// Resolves one tile corner shared by up to four tiles. A corner touched by at least one river tile is carved to
    /// the river bed: the lowest bed height among the sharing river tiles, so river tiles are flat, banks slope
    /// down over one tile, and mouths cut a channel into the sea. Otherwise the corner averages the sharing base
    /// heights, so flat areas stay flat and slopes become facets. A corner past the map edge is skipped by the caller.
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
