using System.Collections.Immutable;

namespace Evenmere.Content.Maps;

/// <summary>
/// The fixed parts of the map format that content cannot express (docs/design/map-format.md): which terrains the engine
/// derives features from, and the default legends. Terrain IDs are content, so this table is the one place the map format
/// names them; the simulation reads only the resulting <see cref="MapTerrainFeatures"/>.
/// </summary>
public static class MapTerrainRules
{
    /// <summary>The feature bits a terrain can carry: <c>River</c>, <c>Beach</c>, <c>DeepWater</c>, <c>Reef</c> and <c>Marsh</c>. <c>Coast</c> is derived and <see cref="MapTerrainFeatures.Water"/> is not stored.</summary>
    public const MapTerrainFeatures StoredFeatureMask =
        MapTerrainFeatures.River | MapTerrainFeatures.Beach | MapTerrainFeatures.DeepWater | MapTerrainFeatures.Reef | MapTerrainFeatures.Marsh;

    /// <summary>Gets the default terrain legend, used when <c>map.json</c> has no <c>legend</c>: one character and the terrain ID it stands for.</summary>
    public static ImmutableArray<(char Symbol, string Terrain)> DefaultLegend { get; } =
    [
        ('.', "plains"),
        ('T', "forest"),
        ('^', "hills"),
        ('_', "beach"),
        (',', "marsh"),
        ('f', "fen"),
        ('M', "highlands"),
        ('A', "peaks"),
        ('=', "river"),
        ('~', "sea"),
        ('w', "deep_water"),
        ('o', "reef"),
    ];

    /// <summary>Gets the default deposit legend, used when <c>map.json</c> has no <c>depositLegend</c>: one character and the <c>DepositKind</c> name it stands for.</summary>
    public static ImmutableArray<(char Symbol, string Deposit)> DefaultDepositLegend { get; } =
    [
        ('s', "stone"),
        ('c', "clay"),
        ('i', "iron"),
        ('k', "coal"),
        ('u', "copper"),
    ];

    /// <summary>Gets what the map format knows about a terrain.</summary>
    /// <param name="terrainId">The terrain's string ID, such as <c>deep_water</c>.</param>
    /// <returns>The features; <see cref="MapTerrainFeatures.None"/> for a terrain with no special meaning.</returns>
    public static MapTerrainFeatures FeaturesOf(string terrainId)
    {
        ArgumentNullException.ThrowIfNull(terrainId);
        return terrainId switch
        {
            "sea" => MapTerrainFeatures.Water,
            "deep_water" => MapTerrainFeatures.Water | MapTerrainFeatures.DeepWater,
            "reef" => MapTerrainFeatures.Water | MapTerrainFeatures.Reef,
            "river" => MapTerrainFeatures.River,
            "beach" => MapTerrainFeatures.Beach,
            "marsh" => MapTerrainFeatures.Marsh,
            _ => MapTerrainFeatures.None,
        };
    }
}
