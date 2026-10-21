using Evenmere.Game.Controls;
using Evenmere.Game.Data.Visuals.Terrain;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Terrain;

/// <summary>
/// Draws the island from <c>IMapQueries</c>: one <c>ArrayMesh</c> per 32 × 32 chunk, built by
/// <see cref="TerrainChunkBuilder"/> and tinted from <see cref="TerrainPalette"/>. The initial build of every chunk
/// happens at once on load; afterwards at most <see cref="MaxRebuildsPerFrame"/> dirty chunks are rebuilt per frame
/// from <c>SimHost</c>'s drained set, with a queue spreading bursts over frames. It also installs the rendered surface
/// height on the camera rig, so picking lands on the visible ground.
/// </summary>
public sealed partial class TerrainRenderer : Node3D
{
    /// <summary>How many dirty chunks one frame rebuilds at most; the rest wait in the queue.</summary>
    public const int MaxRebuildsPerFrame = 4;

    /// <summary>How many drained chunks are taken from the host per frame (more than are rebuilt, so bursts queue up).</summary>
    public const int MaxTakesPerFrame = 8;

    private static readonly string _palettePath = "res://data/visuals/terrain/terrain_palette.tres";
    private static readonly string _shaderPath = "res://rendering/terrain/terrain.gdshader";

    private readonly TerrainChunkBuilder _builder = new();
    private readonly Queue<ChunkCoord> _queue = new();
    private readonly ChunkCoord[] _takeScratch = new ChunkCoord[MaxTakesPerFrame];
    private readonly byte[] _elevations = new byte[TerrainChunkBuilder.WindowTiles * TerrainChunkBuilder.WindowTiles];
    private readonly byte[] _terrains = new byte[TerrainChunkBuilder.WindowTiles * TerrainChunkBuilder.WindowTiles];
    private readonly byte[] _kinds = new byte[TerrainChunkBuilder.WindowTiles * TerrainChunkBuilder.WindowTiles];

    private SimHost? _host;
    private CameraRig? _rig;
    private ShaderMaterial? _material;
    private Color[] _coloursByTerrain = [];
    private MeshInstance3D?[,] _chunks = new MeshInstance3D?[0, 0];
    private bool[] _queued = [];
    private bool _initialised;
    private bool _gridVisible;

    /// <summary>Gets how many chunks have been (re)built since load, for the dirty-rebuild log evidence.</summary>
    public int TotalRebuilt { get; private set; }

    /// <summary>Gets whether the tile-grid overlay is on. It is off by default.</summary>
    public bool GridVisible => _gridVisible;

    /// <inheritdoc />
    public override void _Ready()
    {
        Node? scene = GetTree().CurrentScene;
        _host = scene?.GetNodeOrNull<SimHost>("SimHost");
        _rig = scene?.GetNodeOrNull<CameraRig>("CameraRig");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_initialised)
        {
            if (_host is { IsRunning: true })
            {
                Initialise();
            }

            return;
        }

        if (_host is null)
        {
            return;
        }

        int taken = _host.DequeueDirtyChunks(_takeScratch.AsSpan());
        for (int i = 0; i < taken; i++)
        {
            Enqueue(_takeScratch[i]);
        }

        int rebuilt = 0;
        while (rebuilt < MaxRebuildsPerFrame && _queue.Count > 0)
        {
            ChunkCoord chunk = _queue.Dequeue();
            _queued[(chunk.Y * ChunksX()) + chunk.X] = false;
            RebuildChunk(chunk.X, chunk.Y);
            rebuilt++;
        }

        if (rebuilt > 0)
        {
            GD.Print($"[Evenmere] Terrain: rebuilt {rebuilt} chunk(s) (total {TotalRebuilt}, queued {_queue.Count})");
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ViewGridToggle))
        {
            SetGridVisible(!_gridVisible);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows or hides the tile-grid overlay.</summary>
    /// <param name="visible"><see langword="true"/> to show the grid.</param>
    public void SetGridVisible(bool visible)
    {
        _gridVisible = visible;
        if (_material is not null)
        {
            _material.SetShaderParameter("show_grid", visible);
        }
    }

    /// <summary>
    /// Gets the visible surface height of a tile in world units: the water plane over water, the flowing strip over
    /// rivers, and the corner average (the faceted centre height) over land.
    /// </summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <returns>The height the camera focus and picking use.</returns>
    public float GetSurfaceHeight(int x, int y)
    {
        if (_host is not { IsRunning: true })
        {
            return 0f;
        }

        IMapQueries queried = _host.Query.Map;
        if (x < 0 || y < 0 || x >= queried.Width || y >= queried.Height)
        {
            return 0f;
        }

        TileInfo tile = queried.GetTile(x, y);
        string name = _host.Content.Terrains.Names[tile.Terrain.Value];
        WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
        if (kind is WaterKind.Sea or WaterKind.Deep or WaterKind.Reef)
        {
            return TerrainSurface.WaterSurfaceY;
        }

        float h00 = CornerHeight(queried, x, y);
        float h10 = CornerHeight(queried, x + 1, y);
        float h01 = CornerHeight(queried, x, y + 1);
        float h11 = CornerHeight(queried, x + 1, y + 1);
        if (kind == WaterKind.River)
        {
            // The strip rides the carved corners plus the lift, so the centre matches the visible water.
            return ((h00 + h10 + h01 + h11) * 0.25f) + TerrainSurface.RiverStripLift;
        }

        return (h00 + h10 + h01 + h11) * 0.25f;
    }

    // The height of the tile corner shared by the tiles around vertex (vx, vy): the carved river bed when a
    // neighbour is a river tile, else the average of the neighbours' base heights. Land contributes its ground,
    // water its bed, rivers their lowered bed.
    private float CornerHeight(IMapQueries map, int vx, int vy)
    {
        float h0 = 0f;
        float h1 = 0f;
        float h2 = 0f;
        float h3 = 0f;
        int riverMask = 0;
        int count = 0;
        int bit = 1;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int tileX = vx + dx;
                int tileY = vy + dy;
                if (tileX >= 0 && tileY >= 0 && tileX < map.Width && tileY < map.Height)
                {
                    TileInfo tile = map.GetTile(tileX, tileY);
                    string name = _host?.Content.Terrains.Names[tile.Terrain.Value] ?? string.Empty;
                    WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
                    float baseHeight = TerrainSurface.MeshTopHeight(tile.Elevation, kind);
                    if (bit == 1)
                    {
                        h0 = baseHeight;
                    }
                    else if (bit == 2)
                    {
                        h1 = baseHeight;
                    }
                    else if (bit == 4)
                    {
                        h2 = baseHeight;
                    }
                    else
                    {
                        h3 = baseHeight;
                    }

                    if (kind == WaterKind.River)
                    {
                        riverMask |= bit;
                    }

                    count++;
                }

                bit <<= 1;
            }
        }

        return TerrainSurface.ResolveCorner(h0, h1, h2, h3, riverMask, count);
    }

    private void Initialise()
    {
        if (_host is null || _rig is null)
        {
            return;
        }

        IMapQueries map = _host.Query.Map;
        if (map.Width <= 0 || map.Height <= 0)
        {
            return;
        }

        TerrainPalette? palette = GD.Load<TerrainPalette>(_palettePath);
        if (palette is null)
        {
            GD.PushError($"[Evenmere] Terrain: missing palette '{_palettePath}'.");
            return;
        }

        Shader? shader = GD.Load<Shader>(_shaderPath);
        if (shader is null)
        {
            GD.PushError($"[Evenmere] Terrain: missing shader '{_shaderPath}'.");
            return;
        }

        _coloursByTerrain = new Color[_host.Content.Terrains.Count];
        for (int i = 0; i < _coloursByTerrain.Length; i++)
        {
            _coloursByTerrain[i] = palette.ColourOf(_host.Content.Terrains.Names[i]);
        }

        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("show_grid", _gridVisible);

        int chunksX = (map.Width + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        int chunksY = (map.Height + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        _chunks = new MeshInstance3D?[chunksX, chunksY];
        _queued = new bool[chunksX * chunksY];

        float maxGround = 0f;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                maxGround = Math.Max(maxGround, TerrainSurface.GroundHeight(map.GetTile(x, y).Elevation));
            }
        }

        _rig.SetMapBounds(map.Width, map.Height);
        // Corner averages never exceed the highest ground, so ground plus one unit of margin covers every facet.
        _rig.MaxTerrainHeight = maxGround + 1f;
        _rig.HeightAt = GetSurfaceHeight;

        int built = 0;
        for (int cy = 0; cy < chunksY; cy++)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                RebuildChunk(cx, cy);
                built++;
            }
        }

        _initialised = true;
        GD.Print($"[Evenmere] Terrain: built {built} chunk(s) at once on load ({map.Width}x{map.Height} tiles)");

        // The fresh map marks every chunk dirty; the initial build above already drew them, so drop the marks instead
        // of rebuilding everything a second time through the queue.
        int dropped = 0;
        int drained;
        while ((drained = _host.DequeueDirtyChunks(_takeScratch.AsSpan())) > 0)
        {
            dropped += drained;
        }

        GD.Print($"[Evenmere] Terrain: dropped {dropped} dirty mark(s) covered by the initial build");
    }

    private int ChunksX() => _chunks.GetLength(0);

    private void Enqueue(ChunkCoord chunk)
    {
        if (chunk.X < 0 || chunk.Y < 0 || chunk.X >= _chunks.GetLength(0) || chunk.Y >= _chunks.GetLength(1))
        {
            return;
        }

        int index = (chunk.Y * ChunksX()) + chunk.X;
        if (_queued[index])
        {
            return;
        }

        _queued[index] = true;
        _queue.Enqueue(chunk);
    }

    private void RebuildChunk(int chunkX, int chunkY)
    {
        if (_host is null || _material is null)
        {
            return;
        }

        IMapQueries chunkMap = _host.Query.Map;
        int size = TerrainChunkBuilder.ChunkTiles;
        int window = TerrainChunkBuilder.WindowTiles;
        int firstX = chunkX * size;
        int firstY = chunkY * size;

        // One window with a one-tile halo, so the skirts read the true neighbour heights across chunk borders.
        for (int wy = 0; wy < window; wy++)
        {
            for (int wx = 0; wx < window; wx++)
            {
                int tileX = firstX + wx - 1;
                int tileY = firstY + wy - 1;
                int i = (wy * window) + wx;
                if (tileX < 0 || tileY < 0 || tileX >= chunkMap.Width || tileY >= chunkMap.Height)
                {
                    _elevations[i] = 0;
                    _terrains[i] = 0;
                    _kinds[i] = (byte)WaterKind.Sea;
                    continue;
                }

                TileInfo tile = chunkMap.GetTile(tileX, tileY);
                string name = _host.Content.Terrains.Names[tile.Terrain.Value];
                _elevations[i] = (byte)tile.Elevation;
                _terrains[i] = (byte)tile.Terrain.Value;
                _kinds[i] = (byte)TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
            }
        }

        _builder.Rebuild(_elevations, _terrains, _kinds, chunkMap.Width, chunkMap.Height, firstX - 1, firstY - 1);

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);

        var vertices = new Vector3[_builder.VertexCount];
        ReadOnlySpan<float> positions = _builder.Vertices;
        for (int i = 0; i < _builder.VertexCount; i++)
        {
            vertices[i] = new Vector3(positions[(i * 3) + 0], positions[(i * 3) + 1], positions[(i * 3) + 2]);
        }

        var normals = new Vector3[_builder.VertexCount];
        ReadOnlySpan<float> normalData = _builder.Normals;
        for (int i = 0; i < _builder.VertexCount; i++)
        {
            normals[i] = new Vector3(normalData[(i * 3) + 0], normalData[(i * 3) + 1], normalData[(i * 3) + 2]);
        }

        var colours = new Color[_builder.VertexCount];
        ReadOnlySpan<byte> colourData = _builder.ColourIndices;
        for (int i = 0; i < _builder.VertexCount; i++)
        {
            colours[i] = _coloursByTerrain[colourData[i]];
        }

        int[] indices = new int[_builder.IndexCount];
        ReadOnlySpan<int> indexData = _builder.Indices;
        for (int i = 0; i < _builder.IndexCount; i++)
        {
            indices[i] = indexData[i];
        }

        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // Chunk meshes live in world space (tile (x, y) covers [x, x + 1] × [y, y + 1]), so the node stays at the origin.
        MeshInstance3D instance = _chunks[chunkX, chunkY] ?? CreateChunkNode(chunkX, chunkY);
        instance.Mesh = mesh;
        TotalRebuilt++;
    }

    private MeshInstance3D CreateChunkNode(int chunkX, int chunkY)
    {
        var instance = new MeshInstance3D { Name = $"Chunk_{chunkX}_{chunkY}" };
        if (_material is not null)
        {
            instance.MaterialOverride = _material;
        }

        AddChild(instance);
        _chunks[chunkX, chunkY] = instance;
        return instance;
    }
}
