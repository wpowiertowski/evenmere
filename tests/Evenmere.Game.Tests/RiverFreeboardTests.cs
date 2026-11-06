using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Game.Rendering.Water;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Game.Tests;

/// <summary>M2-38: the river must read as water lying <i>in</i> a channel, not as a slab floating above the land. The
/// acceptance gate builds the real land mesh (<see cref="TerrainChunkBuilder"/>) and the real water strip
/// (<see cref="RiverStripBuilder"/>) for the starter island and generated seeds 1 and 7, then checks every edge
/// shared between a river tile and a land tile: the land mesh at both endpoints and the midpoint must stand at
/// least <see cref="RequiredFreeboard"/> above the water surface there. It also checks that no water skirt face is
/// visible: a skirt's top edge must never rise above the adjacent land. Zero violations on all three islands.</summary>
public sealed class RiverFreeboardTests
{
    /// <summary>How far the bank must stand above the water at every shared edge, in world units.</summary>
    private const float RequiredFreeboard = 0.15f;

    private const float Slack = 1e-3f;

    [Fact]
    public void Freeboard_OnStarter_HoldsOnEveryBankEdge()
    {
        AssertFreeboard(LoadStarter(), nameof(LoadStarter));
    }

    [Fact]
    public void Freeboard_OnGeneratedSeed1_HoldsOnEveryBankEdge()
    {
        AssertFreeboard(IslandGenerator.Generate(1, 320, TestContent.Db), "seed 1");
    }

    [Fact]
    public void Freeboard_OnGeneratedSeed7_HoldsOnEveryBankEdge()
    {
        AssertFreeboard(IslandGenerator.Generate(7, 320, TestContent.Db), "seed 7");
    }

    private static void AssertFreeboard(MapDef map, string name)
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

        // The real land mesh, vertex by vertex, split by which tile type drew it: banks keep their own height,
        // river tiles draw the carved bed, and bank walls span the two. The layout is tiles in ascending (y, x)
        // order with six vertices each, each river tile's walls (six vertices per walled edge) right after its
        // triangles, so the scan consumes exactly what the builder emitted and ends on its vertex count.
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

        // Every bank wall spans exactly bank to bed: its two heights at each endpoint are the land mesh height
        // (or the bed fallback where no bank shares the vertex) and the channel bed, in either order — where the
        // bed sits above the bank (a sill at a river mouth) the wall stands upside-down but covers the same gap.
        foreach (ChunkMeshReader.WallFace wall in walls)
        {
            int ai = (wall.Ay * (width + 1)) + wall.Ax;
            int bi = (wall.By * (width + 1)) + wall.Bx;
            CheckWallEnd(wall.Ax, wall.Ay, wall.TopA, wall.BottomA, landH[ai], bedH[ai], inconsistencies);
            CheckWallEnd(wall.Bx, wall.By, wall.TopB, wall.BottomB, landH[bi], bedH[bi], inconsistencies);
        }

        // The real water strip, vertex by vertex.
        var strip = new RiverStripBuilder();
        strip.Rebuild(elevations, kinds, beds, width, height);
        float[] waterH = new float[(width + 1) * (height + 1)];
        Array.Fill(waterH, float.NaN);
        for (int v = 0; v < strip.VertexCount; v++)
        {
            // Skirt feet share the bank edge's (x, z) below the waterline; only the up-facing surface counts.
            if (Math.Abs(strip.Normals[(v * 3) + 1]) < 0.5f)
            {
                continue;
            }

            float x = strip.Vertices[(v * 3) + 0];
            float z = strip.Vertices[(v * 3) + 2];
            if (Math.Abs(x - MathF.Round(x)) > 1e-4f || Math.Abs(z - MathF.Round(z)) > 1e-4f)
            {
                continue;
            }

            int vi = ((int)MathF.Round(z) * (width + 1)) + (int)MathF.Round(x);
            float h = strip.Vertices[(v * 3) + 1];
            if (float.IsNaN(waterH[vi]))
            {
                waterH[vi] = h;
            }
            else if (Math.Abs(waterH[vi] - h) > 1e-4f)
            {
                inconsistencies.Add($"water corner ({x}, {z}) drew {waterH[vi]} then {h}");
            }
        }

        // Every shared river-land edge: land above water by the freeboard at both endpoints and the midpoint.
        List<string> violations = new();
        int edges = 0;
        float worst = 0f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (kinds[(y * width) + x] != (byte)WaterKind.River)
                {
                    continue;
                }

                CheckEdge(x, y, x + 1, y, x, y - 1, width, height, kinds, landH, waterH, violations, ref edges, ref worst);
                CheckEdge(x + 1, y, x + 1, y + 1, x + 1, y, width, height, kinds, landH, waterH, violations, ref edges, ref worst);
                CheckEdge(x, y + 1, x + 1, y + 1, x, y + 1, width, height, kinds, landH, waterH, violations, ref edges, ref worst);
                CheckEdge(x, y, x, y + 1, x - 1, y, width, height, kinds, landH, waterH, violations, ref edges, ref worst);
            }
        }

        // No visible skirt: every vertex of a steep water face must sit at or below the adjacent bank.
        int skirtFaces = 0;
        for (int t = 0; t < strip.IndexCount; t += 3)
        {
            int a = strip.Indices[t];
            int b = strip.Indices[t + 1];
            int c = strip.Indices[t + 2];
            float nx = strip.Normals[(a * 3) + 0];
            float ny = strip.Normals[(a * 3) + 1];
            if (Math.Abs(ny) >= 0.5f)
            {
                continue;
            }

            skirtFaces++;
            foreach (int v in new[] { a, b, c })
            {
                int vx = (int)MathF.Round(strip.Vertices[(v * 3) + 0]);
                int vz = (int)MathF.Round(strip.Vertices[(v * 3) + 2]);
                float vy = strip.Vertices[(v * 3) + 1];
                int vi = (vz * (width + 1)) + vx;
                if (vi < 0 || vi >= landH.Length || float.IsNaN(landH[vi]))
                {
                    continue;
                }

                if (vy > landH[vi] + Slack)
                {
                    violations.Add($"skirt vertex at ({vx}, {vz}) rises to {vy:F3}, above bank {landH[vi]:F3}");
                }
            }
        }

        inconsistencies.ShouldBeEmpty($"mesh corners disagree on {name}");
        violations.ShouldBeEmpty(
            $"freeboard breaks on {name}: {violations.Count} violation(s) over {edges} bank edge(s), worst deficit {worst:F3} (need {RequiredFreeboard}), {skirtFaces} steep water face(s). First: {string.Join("; ", violations.Take(8))}");
    }

    private static void CheckEdge(
        int ax, int ay, int bx, int by, int nx, int ny,
        int width, int height, byte[] kinds,
        float[] landH, float[] waterH,
        List<string> violations, ref int edges, ref float worst)
    {
        // Only true banks count: where the river meets open water the sea plane covers the joint, not a bank.
        if (nx < 0 || ny < 0 || nx >= width || ny >= height || kinds[(ny * width) + nx] != (byte)WaterKind.Land)
        {
            return;
        }

        edges++;
        int ai = (ay * (width + 1)) + ax;
        int bi = (by * (width + 1)) + bx;
        if (float.IsNaN(landH[ai]) || float.IsNaN(landH[bi]) || float.IsNaN(waterH[ai]) || float.IsNaN(waterH[bi]))
        {
            violations.Add($"bank edge ({ax}, {ay})-({bx}, {by}) misses mesh heights");
            return;
        }

        float landMid = (landH[ai] + landH[bi]) * 0.5f;
        float waterMid = (waterH[ai] + waterH[bi]) * 0.5f;
        CheckPoint(ax, ay, landH[ai], waterH[ai], violations, ref worst);
        CheckPoint(bx, by, landH[bi], waterH[bi], violations, ref worst);
        if (landMid < waterMid + RequiredFreeboard - Slack)
        {
            float deficit = (waterMid + RequiredFreeboard) - landMid;
            worst = Math.Max(worst, deficit);
            violations.Add($"bank edge ({ax}, {ay})-({bx}, {by}) midpoint: land {landMid:F3} vs water {waterMid:F3}");
        }
    }

    private static void CheckPoint(int vx, int vy, float land, float water, List<string> violations, ref float worst)
    {
        if (land < water + RequiredFreeboard - Slack)
        {
            float deficit = (water + RequiredFreeboard) - land;
            worst = Math.Max(worst, deficit);
            violations.Add($"bank vertex ({vx}, {vy}): land {land:F3} vs water {water:F3}");
        }
    }

    private static void CheckWallEnd(int vx, int vy, float high, float low, float land, float bed, List<string> inconsistencies)
    {
        float bank = float.IsNaN(land) ? bed : land;
        float wantHigh = Math.Max(bank, bed);
        float wantLow = Math.Min(bank, bed);
        float gotHigh = Math.Max(high, low);
        float gotLow = Math.Min(high, low);
        if (Math.Abs(gotHigh - wantHigh) > 1e-4f || Math.Abs(gotLow - wantLow) > 1e-4f)
        {
            inconsistencies.Add($"wall end ({vx}, {vy}) spans {gotLow:F3}/{gotHigh:F3} vs bank {bank:F3} bed {bed:F3}");
        }
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
