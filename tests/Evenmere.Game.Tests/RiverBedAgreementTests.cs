using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map.Generation;
using System.Numerics;

namespace Evenmere.Game.Tests;

/// <summary>M2-38: the ground sampler reads the same shared bank heights as the terrain mesh's land tiles, from the shared
/// rule in <see cref="TerrainSurface"/> — never a second copy. River tiles next door keep their carved bed under a
/// bank wall: mesh corners on river tiles match the carved bed rule, every other mesh corner and every sampler
/// corner match the bank rule. The agreement is checked corner by corner over two whole real islands (the starter
/// and generated seed 1), mesh triangles against sampler corners.</summary>
public sealed class RiverBedAgreementTests
{
    private const float Tight = 1e-4f;

    [Fact]
    public void SmoothedBed_LinearRamp_IsExact()
    {
        // Averaging preserves straight ramps: valley profiles come out pixel-identical.
        var tiles = new ArrayBedTiles(5, 1, [2f, 1.5f, 1f, 0.5f, 0f]);
        TerrainSurface.SmoothedBedForTile(tiles, 2, 0).ShouldBe(1f, Tight);
    }

    [Fact]
    public void SmoothedBed_FlatRun_IsExact()
    {
        var tiles = new ArrayBedTiles(4, 1, [0.4f, 0.4f, 0.4f, 0.4f]);
        TerrainSurface.SmoothedBedForTile(tiles, 1, 0).ShouldBe(0.4f, Tight);
        TerrainSurface.SmoothedBedForTile(tiles, 2, 0).ShouldBe(0.4f, Tight);
    }

    [Fact]
    public void SmoothedBed_Step_RelaxesIntoRamp()
    {
        // A terrace step rounds over its neighbours instead of holding a flat panel and a riser.
        var tiles = new ArrayBedTiles(4, 1, [1.1f, 1.1f, 0.6f, 0.6f]);
        float upper = TerrainSurface.SmoothedBedForTile(tiles, 1, 0);
        float lower = TerrainSurface.SmoothedBedForTile(tiles, 2, 0);
        upper.ShouldBeLessThan(1.1f);
        lower.ShouldBeGreaterThan(0.6f);
        upper.ShouldBeGreaterThan(lower);
    }

    [Fact]
    public void SmoothedBed_IsolatedTile_KeepsOwnBed()
    {
        byte[] kinds = [(byte)WaterKind.Sea, (byte)WaterKind.Sea, (byte)WaterKind.Sea, (byte)WaterKind.Sea, (byte)WaterKind.River, (byte)WaterKind.Sea, (byte)WaterKind.Sea, (byte)WaterKind.Sea, (byte)WaterKind.Sea];
        var tiles = new ArrayBedTiles(3, 3, [0f, 0f, 0f, 0f, 1.4f, 0f, 0f, 0f, 0f], kinds);
        TerrainSurface.SmoothedBedForTile(tiles, 1, 1).ShouldBe(1.4f, Tight);
    }

    [Fact]
    public void SamplerCorners_EqualMeshCorners_OnStarter()
    {
        AssertSamplerEqualsMesh(LoadStarter());
    }

    [Fact]
    public void SamplerCorners_EqualMeshCorners_OnGeneratedSeed1()
    {
        AssertSamplerEqualsMesh(IslandGenerator.Generate(1, 320, TestContent.Db));
    }

    private static void AssertSamplerEqualsMesh(MapDef map)
    {
        int width = map.Width;
        int height = map.Height;
        ContentDb content = TestContent.Db;
        var queries = new MapDefQueries(map);

        byte[] elevations = new byte[width * height];
        byte[] kinds = new byte[width * height];
        for (int i = 0; i < elevations.Length; i++)
        {
            elevations[i] = map.Elevation[i];
            string name = content.Terrains.Names[map.Terrain[i].Value];
            bool isRiver = map.RiverWidth[i] != 0;
            kinds[i] = (byte)TerrainSurface.Classify(name, isRiver);
        }

        float[] beds = new float[width * height];
        TerrainSurface.BakeRiverBeds(new ArrayBedTiles(elevations, kinds, width, height), beds);

        float[] landMesh = new float[(width + 1) * (height + 1)];
        float[] bedMesh = new float[(width + 1) * (height + 1)];
        Array.Fill(landMesh, float.NaN);
        Array.Fill(bedMesh, float.NaN);
        List<string> inconsistencies = new();

        var builder = new TerrainChunkBuilder();
        int window = TerrainChunkBuilder.WindowTiles;
        byte[] windowElevations = new byte[window * window];
        byte[] windowKinds = new byte[window * window];
        byte[] windowTerrains = new byte[window * window];
        float[] windowBeds = new float[window * window];

        for (int chunkY = 0; chunkY < height / TerrainChunkBuilder.ChunkTiles; chunkY++)
        {
            for (int chunkX = 0; chunkX < width / TerrainChunkBuilder.ChunkTiles; chunkX++)
            {
                int firstX = chunkX * TerrainChunkBuilder.ChunkTiles;
                int firstY = chunkY * TerrainChunkBuilder.ChunkTiles;
                for (int wy = 0; wy < window; wy++)
                {
                    for (int wx = 0; wx < window; wx++)
                    {
                        int tileX = firstX + wx - 1;
                        int tileY = firstY + wy - 1;
                        int i = (wy * window) + wx;
                        bool inside = tileX >= 0 && tileY >= 0 && tileX < width && tileY < height;
                        windowElevations[i] = inside ? elevations[(tileY * width) + tileX] : (byte)0;
                        windowKinds[i] = inside ? kinds[(tileY * width) + tileX] : (byte)WaterKind.Sea;
                        windowTerrains[i] = 0;
                        windowBeds[i] = inside ? beds[(tileY * width) + tileX] : 0f;
                    }
                }

                builder.Rebuild(windowElevations, windowTerrains, windowKinds, windowBeds, width, height, firstX - 1, firstY - 1);

                int consumed = ChunkMeshReader.ReadCorners(
                    builder.Vertices, kinds, width, height, firstX, firstY, landMesh, bedMesh, null, inconsistencies);
                if (consumed != builder.VertexCount)
                {
                    inconsistencies.Add($"chunk ({firstX}, {firstY}) consumed {consumed} of {builder.VertexCount} vertices");
                }
            }
        }

        inconsistencies.ShouldBeEmpty("the mesh layout drifted from the reader");

        for (int vy = 0; vy <= height; vy++)
        {
            for (int vx = 0; vx <= width; vx++)
            {
                (float land, float bed) = CornerExpectations(elevations, kinds, beds, width, height, vx, vy);
                int vi = (vy * (width + 1)) + vx;
                float samplerHeight = GroundSampler.CornerHeight(queries, content, vx, vy);
                samplerHeight.ShouldBe(land, Tight, $"sampler corner ({vx}, {vy}) on a {width}x{height} island");
                if (!float.IsNaN(landMesh[vi]))
                {
                    landMesh[vi].ShouldBe(land, Tight, $"land mesh corner ({vx}, {vy}) on a {width}x{height} island");
                }

                if (!float.IsNaN(bedMesh[vi]))
                {
                    bedMesh[vi].ShouldBe(bed, Tight, $"river mesh corner ({vx}, {vy}) on a {width}x{height} island");
                }
            }
        }
    }

    // The two corner values the shared rule gives at one vertex: the bank height (what land tiles draw and the
    // sampler reads) and the carved bed (what river tiles draw).
    private static (float Land, float Bed) CornerExpectations(byte[] elevations, byte[] kinds, float[] beds, int width, int height, int vx, int vy)
    {
        float h0 = 0f;
        float h1 = 0f;
        float h2 = 0f;
        float h3 = 0f;
        int riverMask = 0;
        int existMask = 0;
        int bit = 1;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int tileX = vx + dx;
                int tileY = vy + dy;
                if (tileX >= 0 && tileY >= 0 && tileX < width && tileY < height)
                {
                    int i = (tileY * width) + tileX;
                    float baseHeight = kinds[i] == (byte)WaterKind.River
                        ? beds[i]
                        : TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)kinds[i]);
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

                    existMask |= bit;
                    if (kinds[i] == (byte)WaterKind.River)
                    {
                        riverMask |= bit;
                    }
                }

                bit <<= 1;
            }
        }

        return (
            TerrainSurface.LandCornerHeight(h0, h1, h2, h3, existMask, riverMask),
            TerrainSurface.ResolveCorner(h0, h1, h2, h3, riverMask, BitOperations.PopCount((uint)existMask)));
    }

    private static MapDef LoadStarter()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                MapLoadResult result = MapLoader.Load(
                    [Path.Combine(directory.FullName, "content")],
                    "starter",
                    TestContent.Db);
                return result.GetMapOrThrow();
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }

    // The tile grid as the shared riverbed rule reads it, over raw byte layers. Raw mode returns the given
    // beds verbatim (smoothing unit tests, where no banks apply); mesh mode resolves mesh-top base heights.
    private struct ArrayBedTiles : TerrainSurface.IRiverBedTiles
    {
        private readonly byte[] _elevations;
        private readonly byte[] _kinds;
        private readonly float[]? _beds;

        public ArrayBedTiles(int width, int height, float[] beds)
        {
            Width = width;
            Height = height;
            _elevations = [];
            _kinds = [];
            _beds = beds;
        }

        public ArrayBedTiles(int width, int height, float[] beds, byte[] kinds)
        {
            Width = width;
            Height = height;
            _elevations = [];
            _kinds = kinds;
            _beds = beds;
        }

        public ArrayBedTiles(byte[] elevations, byte[] kinds, int width, int height)
        {
            Width = width;
            Height = height;
            _elevations = elevations;
            _kinds = kinds;
            _beds = null;
        }

        public int Width { get; }

        public int Height { get; }

        public bool IsRiver(int x, int y) => _kinds.Length != 0
            ? (WaterKind)_kinds[(y * Width) + x] == WaterKind.River
            : _beds is not null;

        public bool IsLand(int x, int y) => _beds is null && (WaterKind)_kinds[(y * Width) + x] == WaterKind.Land;

        public float BaseHeight(int x, int y)
        {
            if (_beds is not null)
            {
                return _beds[(y * Width) + x];
            }

            return TerrainSurface.MeshTopHeight(_elevations[(y * Width) + x], (WaterKind)_kinds[(y * Width) + x]);
        }
    }

    // The map queries over a loaded MapDef, for the sampler side of the agreement.
    private sealed class MapDefQueries(MapDef map) : IMapQueries
    {
        public int Width => map.Width;

        public int Height => map.Height;

        public TileInfo GetTile(int x, int y)
        {
            int i = (y * map.Width) + x;
            return new TileInfo(
                map.Terrain[i],
                map.Elevation[i],
                SlopeClass.Flat,
                map.RiverWidth[i] != 0 ? TileFeatures.River : TileFeatures.None,
                (RiverWidthClass)map.RiverWidth[i],
                map.Region[i],
                null,
                0u);
        }

        public EntityId BuildingAt(int x, int y) => EntityId.None;

        public bool TryGetBuilding(EntityId id, out BuildingInfo building)
        {
            building = default;
            return false;
        }

        public BuildingSequence Buildings => default;

        public BuildingSequence ActiveBuildings => default;

        public int DrainDirtyChunks(Span<ChunkCoord> destination) => 0;
    }
}
