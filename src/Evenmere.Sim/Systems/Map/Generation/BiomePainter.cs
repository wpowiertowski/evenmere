using Evenmere.Content;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Paints terrain from elevation bands plus a moisture noise field (both integers, per ADR-0003).
/// The bands follow the progression ladder: the sea and beaches ring the island, hills rise through
/// highlands to peaks, marsh sits on low wet land, and forest grows where it is wet enough. Rivers,
/// fertility and deposits are M2-02's and are left zeroed by the generator entry point.
/// </summary>
internal static class BiomePainter
{
    /// <summary>The number of noise octaves in the moisture field.</summary>
    public const int MoistureOctaves = 4;

    /// <summary>Elevation 1 is the beach ring; 0 is the sea.</summary>
    public const int BeachElevation = 1;

    /// <summary>Elevations at or above this are hills.</summary>
    public const int HillsElevation = 8;

    /// <summary>Elevations at or above this are highlands.</summary>
    public const int HighlandsElevation = 11;

    /// <summary>Elevations at or above this are peaks, which are never buildable.</summary>
    public const int PeaksElevation = 14;

    /// <summary>The highest elevation that can still be marsh.</summary>
    public const int MarshElevation = 3;

    /// <summary>The per-mille moisture at or above which low land is marsh.</summary>
    public const int MarshMoisturePerMille = 600;

    /// <summary>The per-mille moisture at or above which land is forest.</summary>
    public const int ForestMoisturePerMille = 540;

    /// <summary>Stream separation between the elevation and moisture fields of one seed.</summary>
    public const int MoistureStreamXor = unchecked((int)0x9E3779B9);

    /// <summary>
    /// Paints one terrain per tile from the elevation grid and a seeded moisture field.
    /// </summary>
    /// <param name="size">The width and height in tiles.</param>
    /// <param name="elevation">The <c>size × size</c> elevation grid from <see cref="HeightmapBuilder"/>.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="content">The loaded content, which must define every terrain this painter names.</param>
    /// <returns><c>size × size</c> terrain IDs, rows from the top edge.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="elevation"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this painter needs.</exception>
    public static TerrainId[] Paint(int size, byte[] elevation, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId beach = content.IdOf<TerrainId>("beach");
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId forest = content.IdOf<TerrainId>("forest");
        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId marsh = content.IdOf<TerrainId>("marsh");
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        TerrainId peaks = content.IdOf<TerrainId>("peaks");

        int moistureSeed = seed ^ MoistureStreamXor;
        var terrain = new TerrainId[size * size];
        for (int y = 0; y < size; y++)
        {
            int nyQ16 = (int)(((long)y * IntegerNoise.Unit) / size);
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                int e = elevation[i];
                if (e == 0)
                {
                    terrain[i] = sea;
                }
                else if (e == BeachElevation)
                {
                    terrain[i] = beach;
                }
                else if (e >= PeaksElevation)
                {
                    terrain[i] = peaks;
                }
                else if (e >= HighlandsElevation)
                {
                    terrain[i] = highlands;
                }
                else if (e >= HillsElevation)
                {
                    terrain[i] = hills;
                }
                else
                {
                    int nxQ16 = (int)(((long)x * IntegerNoise.Unit) / size);
                    int moisture = IntegerNoise.Fbm(nxQ16, nyQ16, moistureSeed, MoistureOctaves);
                    int moisturePerMille = (moisture * 1000) / 65535;
                    if (e <= MarshElevation && moisturePerMille >= MarshMoisturePerMille)
                    {
                        terrain[i] = marsh;
                    }
                    else if (moisturePerMille >= ForestMoisturePerMille)
                    {
                        terrain[i] = forest;
                    }
                    else
                    {
                        terrain[i] = plains;
                    }
                }
            }
        }

        return terrain;
    }
}
