namespace Evenmere.Game.Rendering.Overlays;

/// <summary>
/// The overlay surface as plain arrays: one flat-shaded quad per tile (two triangles with their own four corners, in
/// the terrain's winding), at the averaged corner heights plus a small lift, with per-corner colours from the tile's
/// swatch. It stays Godot-free so the geometry is unit-tested; the renderer turns the arrays into a mesh. Corners
/// average their sharing tiles so the sheet follows slopes, while colours stay flat per tile so layer edges stay
/// crisp. Unlit tiles write transparent corners, so only the layer reads.
/// </summary>
public sealed class OverlayMeshBuilder
{
    /// <summary>How far above the averaged ground the sheet rides, in world units.</summary>
    public const float Lift = 0.08f;

    private float[] _vertices = [];
    private float[] _normals = [];
    private float[] _uvs = [];
    private float[] _colours = [];
    private int[] _indices = [];

    /// <summary>Gets the corner positions written by the last rebuild: X, Y, Z triples, world units.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex normals written by the last rebuild: X, Y, Z triples, always straight up.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the UVs written by the last rebuild: U, V pairs spanning the map.</summary>
    public ReadOnlySpan<float> Uvs => _uvs.AsSpan(0, VertexCount * 2);

    /// <summary>Gets the per-vertex colours written by the last rebuild: R, G, B, A quadruples in linear space.</summary>
    public ReadOnlySpan<float> Colours => _colours.AsSpan(0, VertexCount * 4);

    /// <summary>Gets the triangle indices written by the last rebuild.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Gets how many corner vertices the last rebuild wrote.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the last rebuild wrote.</summary>
    public int IndexCount { get; private set; }

    /// <summary>
    /// Rebuilds the sheet geometry for a map: one flat-shaded quad per tile (two triangles with their own four
    /// corners, in the terrain's winding) at the averaged corner heights plus a small lift. Tiles are visited in
    /// ascending (y, x) order, so equal inputs give equal outputs. Colours are left as-is; call
    /// <see cref="Retint"/> before drawing.
    /// </summary>
    /// <param name="elevations">The ground height per tile in world units, row-major.</param>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    public void Rebuild(float[] elevations, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(elevations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (elevations.Length != width * height)
        {
            throw new ArgumentException($"Expected {width * height} elevations for a {width} × {height} map, not {elevations.Length}.", nameof(elevations));
        }

        int tiles = width * height;
        if (_vertices.Length < tiles * 6 * 3)
        {
            _vertices = new float[tiles * 6 * 3];
            _normals = new float[tiles * 6 * 3];
            _uvs = new float[tiles * 6 * 2];
            _colours = new float[tiles * 6 * 4];
            _indices = new int[tiles * 6];
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = (y * width) + x;
                float topLeft = CornerHeight(elevations, width, height, x, y) + Lift;
                float topRight = CornerHeight(elevations, width, height, x + 1, y) + Lift;
                float bottomRight = CornerHeight(elevations, width, height, x + 1, y + 1) + Lift;
                float bottomLeft = CornerHeight(elevations, width, height, x, y + 1) + Lift;

                // Counter-clockwise viewed from above (the terrain's winding), so the sheet faces the camera.
                int v = tile * 6;
                float u0 = (float)x / width;
                float u1 = (float)(x + 1) / width;
                float v0 = (float)y / height;
                float v1 = (float)(y + 1) / height;
                Emit(v + 0, x, topLeft, y, u0, v0);
                Emit(v + 1, x + 1, topRight, y, u1, v0);
                Emit(v + 2, x + 1, bottomRight, y + 1, u1, v1);
                Emit(v + 3, x, topLeft, y, u0, v0);
                Emit(v + 4, x + 1, bottomRight, y + 1, u1, v1);
                Emit(v + 5, x, bottomLeft, y + 1, u0, v1);

                int w = tile * 6;
                _indices[w + 0] = v + 0;
                _indices[w + 1] = v + 1;
                _indices[w + 2] = v + 2;
                _indices[w + 3] = v + 3;
                _indices[w + 4] = v + 4;
                _indices[w + 5] = v + 5;
            }
        }

        VertexCount = tiles * 6;
        IndexCount = tiles * 6;
    }

    /// <summary>
    /// Writes the per-vertex colours for the current geometry: every corner of a tile takes the tile's swatch, so
    /// layer edges stay crisp. Transparent where the layer is off, so only the layer reads.
    /// </summary>
    /// <param name="swatches">The swatch per tile in row-major order, one per tile of the last <see cref="Rebuild"/>.</param>
    public void Retint(OverlaySwatch[] swatches)
    {
        ArgumentNullException.ThrowIfNull(swatches);
        if (swatches.Length * 6 != VertexCount)
        {
            throw new ArgumentException($"Expected {VertexCount / 6} swatches for the built sheet, not {swatches.Length}.", nameof(swatches));
        }

        for (int tile = 0; tile < swatches.Length; tile++)
        {
            OverlaySwatch swatch = swatches[tile];
            for (int corner = 0; corner < 6; corner++)
            {
                int vertex = (tile * 6) + corner;
                _colours[(vertex * 4) + 0] = swatch.R;
                _colours[(vertex * 4) + 1] = swatch.G;
                _colours[(vertex * 4) + 2] = swatch.B;
                _colours[(vertex * 4) + 3] = swatch.A;
            }
        }
    }

    private void Emit(int vertex, float x, float y, float z, float u, float v)
    {
        _vertices[(vertex * 3) + 0] = x;
        _vertices[(vertex * 3) + 1] = y;
        _vertices[(vertex * 3) + 2] = z;
        _normals[(vertex * 3) + 0] = 0f;
        _normals[(vertex * 3) + 1] = 1f;
        _normals[(vertex * 3) + 2] = 0f;
        _uvs[(vertex * 2) + 0] = u;
        _uvs[(vertex * 2) + 1] = v;
    }

    // The average ground height of the tiles sharing a corner (up to four), so flat areas stay flat and slopes
    // become ramps that track the terrain's own averaging.
    private static float CornerHeight(float[] elevations, int width, int height, int cx, int cy)
    {
        float sum = 0f;
        int count = 0;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && y >= 0 && x < width && y < height)
                {
                    sum += elevations[(y * width) + x];
                    count++;
                }
            }
        }

        return count == 0 ? 0f : sum / count;
    }
}
