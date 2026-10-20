namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// The size of the map in tiles, and the maths that goes with it: the mapping between tile coordinates and world
/// coordinates, and clamping of the camera's focus point. It holds no Godot types so it can be unit tested on its own.
/// </summary>
/// <remarks>
/// The world is laid out with one unit per tile. Tile (x, y) covers the world square [x, x + 1] × [y, y + 1] on the
/// X and Z axes, so its centre is world (x + 0.5, 0, y + 0.5). Ground level is Y = 0.
/// </remarks>
public readonly record struct MapBounds
{
    /// <summary>The edge length of a tile in world units.</summary>
    public const float WorldUnitsPerTile = 1f;

    /// <summary>Initializes a new instance of the <see cref="MapBounds"/> struct.</summary>
    /// <param name="widthTiles">The map width in tiles (X); values below 1 are raised to 1.</param>
    /// <param name="heightTiles">The map height in tiles (the Y of a tile coordinate, which is world Z); values below 1 are raised to 1.</param>
    public MapBounds(int widthTiles, int heightTiles)
    {
        WidthTiles = Math.Max(1, widthTiles);
        HeightTiles = Math.Max(1, heightTiles);
    }

    /// <summary>Gets the 128 × 128 tile island the vertical slice starts with.</summary>
    public static MapBounds Default => new(128, 128);

    /// <summary>Gets the map width in tiles.</summary>
    public int WidthTiles { get; }

    /// <summary>Gets the map height in tiles.</summary>
    public int HeightTiles { get; }

    /// <summary>Gets the map width in world units.</summary>
    public float WorldWidth => WidthTiles * WorldUnitsPerTile;

    /// <summary>Gets the map depth (world Z) in world units.</summary>
    public float WorldDepth => HeightTiles * WorldUnitsPerTile;

    /// <summary>Gets the world X of the tile's centre.</summary>
    /// <param name="tile">The tile coordinate on the X axis.</param>
    /// <returns>The world X coordinate.</returns>
    public static float TileCenter(int tile) => (tile + 0.5f) * WorldUnitsPerTile;

    /// <summary>Gets the tile that holds a world coordinate along one axis. It is not clamped to the map.</summary>
    /// <param name="world">The world coordinate (X or Z).</param>
    /// <returns>The tile index on that axis.</returns>
    public static int TileOf(float world) => (int)MathF.Floor(world / WorldUnitsPerTile);

    /// <summary>Gets a value indicating whether the tile lies on the map.</summary>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y.</param>
    /// <returns><see langword="true"/> when 0 &lt;= x &lt; width and 0 &lt;= y &lt; height.</returns>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < WidthTiles && y < HeightTiles;

    /// <summary>Gets the world position of the middle of the map.</summary>
    /// <returns>World X and Z.</returns>
    public (float X, float Z) Center() => (WorldWidth * 0.5f, WorldDepth * 0.5f);

    /// <summary>Clamps a focus point (world X and Z) to the map, optionally allowing it to stray past the edge.</summary>
    /// <param name="x">The world X.</param>
    /// <param name="z">The world Z.</param>
    /// <param name="margin">How far past the map edge the focus may go, in world units; negative values are treated as zero.</param>
    /// <returns>The clamped world X and Z.</returns>
    public (float X, float Z) ClampFocus(float x, float z, float margin)
    {
        float m = Math.Max(0f, margin);
        return (Math.Clamp(x, 0f - m, WorldWidth + m), Math.Clamp(z, 0f - m, WorldDepth + m));
    }
}
