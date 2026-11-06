using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Game.Rendering.Water;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Game.Tests;

/// <summary>M2-38 round 2: the fog drape must clear the new channel geometry. For every land and river tile corner on
/// seed 1 (a superset of the fogged tiles), the shared drape height (<see cref="TerrainSurface.FogCornerHeight"/>
/// with no lift) stands at or above the highest surface the terrain mesh draws there — bank corners, carved bed
/// corners, and bank wall endpoints alike — so no wall top or levee lip in a fogged region can show through the
/// haze the renderer lifts above it.</summary>
public sealed class FogDrapeTests
{
    [Fact]
    public void FogDrape_OnStarter_ClearsEveryMeshCorner()
    {
        AssertDrapeClearsMesh(LoadStarter(), nameof(LoadStarter));
    }

    [Fact]
    public void FogDrape_OnGeneratedSeed1_ClearsEveryMeshCorner()
    {
        AssertDrapeClearsMesh(IslandGenerator.Generate(1, 320, TestContent.Db), "seed 1");
    }

    private static void AssertDrapeClearsMesh(MapDef map, string name)
    {
        int width = map.Width;
        int height = map.Height;
        ContentDb content = TestContent.Db;

        byte[] elevations = new byte[width * height];
        byte[] kinds = new byte[width * height];
        for (int i = 0; i < elevations.Length; i++)
        {
            elevations[i] = map.Elevation[i];
            string terrainName = content.Terrains.Names[map.Terrain[i].Value];
            kinds[i] = (byte)TerrainSurface.Classify(terrainName, map.RiverWidth[i] != 0);
        }

        float[] beds = new float[width * height];
        TerrainSurface.BakeRiverBeds(new RawBedTiles(elevations, kinds, width, height), beds);

        // The real mesh maxima, as in the freeboard gate: banks, beds and wall spans per corner.
        float[] landH = new float[(width + 1) * (height + 1)];
        float[] bedH = new float[(width + 1) * (height + 1)];
        Array.Fill(landH, float.NaN);
        Array.Fill(bedH, float.NaN);
        List<string> inconsistencies = new();
        List<ChunkMeshReader.WallFace> walls = new();
        var builder = new TerrainChunkBuilder();
        int window = TerrainChunkBuilder.WindowTiles;
        byte[] windowElevations = new byte[window * window];
        byte[] windowTerrains = new byte[window * window];
        byte[] windowKinds = new byte[window * window];
        float[] windowBeds = new float[window * window];
        for (int chunkY = 0; chunkY * TerrainChunkBuilder.ChunkTiles < height; chunkY++)
        {
            for (int chunkX = 0; chunkX * TerrainChunkBuilder.ChunkTiles < width; chunkX++)
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
                        windowTerrains[i] = 0;
                        windowKinds[i] = inside ? kinds[(tileY * width) + tileX] : (byte)WaterKind.Sea;
                        windowBeds[i] = inside ? beds[(tileY * width) + tileX] : 0f;
                    }
                }

                builder.Rebuild(windowElevations, windowTerrains, windowKinds, windowBeds, width, height, firstX - 1, firstY - 1);

                int consumed = ChunkMeshReader.ReadCorners(
                    builder.Vertices, kinds, width, height, firstX, firstY, landH, bedH, walls, inconsistencies);
                if (consumed != builder.VertexCount)
                {
                    inconsistencies.Add($"chunk ({firstX}, {firstY}) consumed {consumed} of {builder.VertexCount} vertices");
                }
            }
        }

        inconsistencies.ShouldBeEmpty($"mesh layout drifted on {name}");

        // The drape at zero lift must already clear the mesh; the renderer only lifts it higher.
        List<string> violations = new();
        for (int vy = 0; vy <= height; vy++)
        {
            for (int vx = 0; vx <= width; vx++)
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

                int vi = (vy * (width + 1)) + vx;
                float meshMax = float.NegativeInfinity;
                if (!float.IsNaN(landH[vi]))
                {
                    meshMax = Math.Max(meshMax, landH[vi]);
                }

                if (!float.IsNaN(bedH[vi]))
                {
                    meshMax = Math.Max(meshMax, bedH[vi]);
                }

                if (float.IsNegativeInfinity(meshMax))
                {
                    continue;
                }

                float drape = TerrainSurface.FogCornerHeight(h0, h1, h2, h3, existMask, riverMask, 0f);
                if (drape < meshMax - 1e-4f)
                {
                    violations.Add($"corner ({vx}, {vy}): drape {drape:F3} below mesh {meshMax:F3}");
                }
            }
        }

        violations.ShouldBeEmpty($"fog drape breaks on {name}: {violations.Count} corner(s). First: {string.Join("; ", violations.Take(8))}");
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

    // The tile grid as the shared riverbed rule reads it, over raw byte layers.
    private readonly struct RawBedTiles(byte[] elevations, byte[] kinds, int width, int height) : TerrainSurface.IRiverBedTiles
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public bool IsRiver(int x, int y) => (WaterKind)kinds[(y * Width) + x] == WaterKind.River;

        public bool IsLand(int x, int y) => (WaterKind)kinds[(y * Width) + x] == WaterKind.Land;

        public float BaseHeight(int x, int y) => TerrainSurface.MeshTopHeight(elevations[(y * Width) + x], (WaterKind)kinds[(y * Width) + x]);
    }
}
