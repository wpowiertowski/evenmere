using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Paints one fertility noise patch per crop (all integers, per ADR-0003). A tile is fertile for a crop
/// when its patch is strong there and its terrain suits the crop: grain, flax and hops on plains; apples on
/// plains and hills; grapes and tea on hills; madder and sugar beet on marsh. The same seed always paints
/// the same patches.
/// </summary>
internal static class FertilityPainter
{
    /// <summary>The number of noise octaves in each fertility patch.</summary>
    public const int Octaves = 3;

    /// <summary>A tile whose patch sample reaches this is fertile for the crop, before the terrain check.</summary>
    public const int FertileThreshold = 33000;

    /// <summary>Stream separation between the fertility patches and the other generator fields.</summary>
    public const int FertilityStreamXor = unchecked((int)0x77E4DA91);

    /// <summary>Stream stride between the patch of one crop and the next.</summary>
    public const int CropStreamStride = unchecked((int)0x2B9923C5);

    /// <summary>The number of crops with a fertility patch, matching <see cref="CropKind"/>.</summary>
    private const int CropCount = 8;

    /// <summary>
    /// Paints the fertility bit mask of every tile in place: bit <c>1 &lt;&lt; (int)CropKind</c> is set when
    /// the tile is fertile for that crop. River, sea and reef tiles are never fertile.
    /// </summary>
    /// <param name="map">The heightmap to paint; terrain is read, fertility is written.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="content">The loaded content, which must define the plains, hills and marsh terrains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this painter needs.</exception>
    internal static void Paint(GeneratedHeightmap map, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId marsh = content.IdOf<TerrainId>("marsh");
        int size = map.Size;
        for (int c = 0; c < CropCount; c++)
        {
            int stream = (seed ^ FertilityStreamXor) + (c * CropStreamStride);
            uint bit = 1u << c;
            for (int y = 0; y < size; y++)
            {
                // Sampled at twice the island density, so patches mix across small affinity areas instead of
                // drowning whole regions or missing them: a seed's plains sit in many patches, never one.
                int nyQ16 = (int)(((long)y * IntegerNoise.Unit * 2) / size);
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size) + x;
                    if (!IsAffinity((CropKind)c, map.Terrain[i], plains, hills, marsh))
                    {
                        continue;
                    }

                    int nxQ16 = (int)(((long)x * IntegerNoise.Unit * 2) / size);
                    if (IntegerNoise.Fbm(nxQ16, nyQ16, stream, Octaves) >= FertileThreshold)
                    {
                        map.Fertility[i] |= bit;
                    }
                }
            }
        }
    }

    // The terrain ladder affinities (progression.md): grain, flax and hops on plains; apples on plains and
    // hills; grapes and tea on hills; madder and sugar beet on marsh. Flax is not listed on the card, and
    // follows grain: the starter island grows it on plains.
    private static bool IsAffinity(CropKind crop, TerrainId terrain, TerrainId plains, TerrainId hills, TerrainId marsh)
    {
        return crop switch
        {
            CropKind.Grain or CropKind.Flax or CropKind.Hops => terrain.Equals(plains),
            CropKind.Apples => terrain.Equals(plains) || terrain.Equals(hills),
            CropKind.Grapes or CropKind.Tea => terrain.Equals(hills),
            CropKind.Madder or CropKind.SugarBeet => terrain.Equals(marsh),
            _ => false,
        };
    }
}
