using Evenmere.Content;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// The old-to-new ID maps a save load remaps with: one <c>old index to new index</c> array per content kind, built from
/// the save header's string tables and the current <see cref="ContentDb"/>. An entry is the new index, or -1 when the
/// save's item is gone from the content. The common case (every map is the identity) skips the remap entirely.
/// </summary>
public sealed class SaveTables
{
    private readonly int[] _goods;
    private readonly int[] _buildings;
    private readonly int[] _recipes;
    private readonly int[] _tiers;
    private readonly int[] _techs;
    private readonly int[] _civics;
    private readonly int[] _traditions;
    private readonly int[] _festivals;
    private readonly int[] _greatWorks;
    private readonly int[] _monuments;
    private readonly int[] _happenings;
    private readonly int[] _eras;
    private readonly int[] _terrains;

    private SaveTables(
        string[] oldGoods, string[] oldBuildings, string[] oldRecipes, string[] oldTiers, string[] oldTechs,
        string[] oldCivics, string[] oldTraditions, string[] oldFestivals, string[] oldGreatWorks,
        string[] oldMonuments, string[] oldHappenings, string[] oldEras, string[] oldTerrains, ContentDb content)
    {
        Content = content;
        OldGoods = oldGoods;
        OldBuildings = oldBuildings;
        OldRecipes = oldRecipes;
        OldTiers = oldTiers;
        OldTechs = oldTechs;
        OldCivics = oldCivics;
        OldTraditions = oldTraditions;
        OldFestivals = oldFestivals;
        OldGreatWorks = oldGreatWorks;
        OldMonuments = oldMonuments;
        OldHappenings = oldHappenings;
        OldEras = oldEras;
        OldTerrains = oldTerrains;

        _goods = BuildMap(oldGoods, content.Goods);
        _buildings = BuildMap(oldBuildings, content.Buildings);
        _recipes = BuildMap(oldRecipes, content.Recipes);
        _tiers = BuildMap(oldTiers, content.Tiers);
        _techs = BuildMap(oldTechs, content.Techs);
        _civics = BuildMap(oldCivics, content.Civics);
        _traditions = BuildMap(oldTraditions, content.Traditions);
        _festivals = BuildMap(oldFestivals, content.Festivals);
        _greatWorks = BuildMap(oldGreatWorks, content.GreatWorks);
        _monuments = BuildMap(oldMonuments, content.Monuments);
        _happenings = BuildMap(oldHappenings, content.Happenings);
        _eras = BuildMap(oldEras, content.Eras);
        _terrains = BuildMap(oldTerrains, content.Terrains);
    }

    /// <summary>Gets the content the maps point into.</summary>
    public ContentDb Content { get; }

    /// <summary>Gets the save's good names in save-ID order.</summary>
    public string[] OldGoods { get; }

    /// <summary>Gets the save's building-type names in save-ID order.</summary>
    public string[] OldBuildings { get; }

    /// <summary>Gets the save's recipe names in save-ID order.</summary>
    public string[] OldRecipes { get; }

    /// <summary>Gets the save's tier names in save-ID order.</summary>
    public string[] OldTiers { get; }

    /// <summary>Gets the save's tech names in save-ID order.</summary>
    public string[] OldTechs { get; }

    /// <summary>Gets the save's civic names in save-ID order.</summary>
    public string[] OldCivics { get; }

    /// <summary>Gets the save's tradition names in save-ID order.</summary>
    public string[] OldTraditions { get; }

    /// <summary>Gets the save's festival names in save-ID order.</summary>
    public string[] OldFestivals { get; }

    /// <summary>Gets the save's great-work-type names in save-ID order.</summary>
    public string[] OldGreatWorks { get; }

    /// <summary>Gets the save's monument names in save-ID order.</summary>
    public string[] OldMonuments { get; }

    /// <summary>Gets the save's happening names in save-ID order.</summary>
    public string[] OldHappenings { get; }

    /// <summary>Gets the save's era names in save-ID order.</summary>
    public string[] OldEras { get; }

    /// <summary>Gets the save's terrain names in save-ID order.</summary>
    public string[] OldTerrains { get; }

    /// <summary>
    /// Gets a value indicating whether the save's tables equal the current content's: same names in the same order for
    /// every kind. The remap is skipped then.
    /// </summary>
    public bool IsIdentity =>
        IsIdentityMap(_goods, Content.Goods.Count) && IsIdentityMap(_buildings, Content.Buildings.Count)
        && IsIdentityMap(_recipes, Content.Recipes.Count) && IsIdentityMap(_tiers, Content.Tiers.Count)
        && IsIdentityMap(_techs, Content.Techs.Count) && IsIdentityMap(_civics, Content.Civics.Count)
        && IsIdentityMap(_traditions, Content.Traditions.Count) && IsIdentityMap(_festivals, Content.Festivals.Count)
        && IsIdentityMap(_greatWorks, Content.GreatWorks.Count) && IsIdentityMap(_monuments, Content.Monuments.Count)
        && IsIdentityMap(_happenings, Content.Happenings.Count) && IsIdentityMap(_eras, Content.Eras.Count)
        && IsIdentityMap(_terrains, Content.Terrains.Count);

    /// <summary>Builds the maps from a header and the current content.</summary>
    /// <param name="header">The save header with the save's string tables.</param>
    /// <param name="content">The current content.</param>
    /// <returns>The maps.</returns>
    /// <exception cref="InvalidDataException">The header is missing a string table.</exception>
    internal static SaveTables Build(SaveHeader header, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(content);
        if (header.Goods is null || header.Buildings is null || header.Recipes is null || header.Tiers is null
            || header.Techs is null || header.Civics is null || header.Traditions is null || header.Festivals is null
            || header.GreatWorks is null || header.Monuments is null || header.Happenings is null || header.Eras is null
            || header.Terrains is null)
        {
            throw new InvalidDataException("The save's header is missing a string table.");
        }

        return new SaveTables(
            header.Goods, header.Buildings, header.Recipes, header.Tiers, header.Techs, header.Civics,
            header.Traditions, header.Festivals, header.GreatWorks, header.Monuments, header.Happenings,
            header.Eras, header.Terrains, content);
    }

    /// <summary>Maps a save-era good index to its current index, or -1 when the good is gone.</summary>
    /// <param name="old">The save's raw good ID.</param>
    /// <returns>The current raw good ID, or -1.</returns>
    public int MapGood(int old) => Map(_goods, old);

    /// <summary>Maps a save-era building-type index to its current index, or -1 when the type is gone.</summary>
    /// <param name="old">The save's raw building-type ID.</param>
    /// <returns>The current raw building-type ID, or -1.</returns>
    public int MapBuilding(int old) => Map(_buildings, old);

    /// <summary>Maps a save-era recipe index to its current index, or -1 when the recipe is gone.</summary>
    /// <param name="old">The save's raw recipe ID.</param>
    /// <returns>The current raw recipe ID, or -1.</returns>
    public int MapRecipe(int old) => Map(_recipes, old);

    /// <summary>Maps a save-era tier index to its current index, or -1 when the tier is gone.</summary>
    /// <param name="old">The save's raw tier ID.</param>
    /// <returns>The current raw tier ID, or -1.</returns>
    public int MapTier(int old) => Map(_tiers, old);

    /// <summary>Maps a save-era tech index to its current index, or -1 when the tech is gone.</summary>
    /// <param name="old">The save's raw tech ID.</param>
    /// <returns>The current raw tech ID, or -1.</returns>
    public int MapTech(int old) => Map(_techs, old);

    /// <summary>Maps a save-era civic index to its current index, or -1 when the civic is gone.</summary>
    /// <param name="old">The save's raw civic ID.</param>
    /// <returns>The current raw civic ID, or -1.</returns>
    public int MapCivic(int old) => Map(_civics, old);

    /// <summary>Maps a save-era tradition index to its current index, or -1 when the tradition is gone.</summary>
    /// <param name="old">The save's raw tradition ID.</param>
    /// <returns>The current raw tradition ID, or -1.</returns>
    public int MapTradition(int old) => Map(_traditions, old);

    /// <summary>Maps a save-era festival index to its current index, or -1 when the festival is gone.</summary>
    /// <param name="old">The save's raw festival ID.</param>
    /// <returns>The current raw festival ID, or -1.</returns>
    public int MapFestival(int old) => Map(_festivals, old);

    /// <summary>Maps a save-era great-work-type index to its current index, or -1 when the type is gone.</summary>
    /// <param name="old">The save's raw great-work-type ID.</param>
    /// <returns>The current raw great-work-type ID, or -1.</returns>
    public int MapGreatWork(int old) => Map(_greatWorks, old);

    /// <summary>Maps a save-era monument index to its current index, or -1 when the monument is gone.</summary>
    /// <param name="old">The save's raw monument ID.</param>
    /// <returns>The current raw monument ID, or -1.</returns>
    public int MapMonument(int old) => Map(_monuments, old);

    /// <summary>Maps a save-era happening index to its current index, or -1 when the happening is gone.</summary>
    /// <param name="old">The save's raw happening ID.</param>
    /// <returns>The current raw happening ID, or -1.</returns>
    public int MapHappening(int old) => Map(_happenings, old);

    /// <summary>Maps a save-era era index to its current index, or -1 when the era is gone.</summary>
    /// <param name="old">The save's raw era ID.</param>
    /// <returns>The current raw era ID, or -1.</returns>
    public int MapEra(int old) => Map(_eras, old);

    /// <summary>Maps a save-era terrain index to its current index, or -1 when the terrain is gone.</summary>
    /// <param name="old">The save's raw terrain ID.</param>
    /// <returns>The current raw terrain ID, or -1.</returns>
    public int MapTerrain(int old) => Map(_terrains, old);

    /// <summary>Gets the good map: <c>map[old] = new</c>, or -1 for a dropped good.</summary>
    internal int[] GoodMap => _goods;

    /// <summary>Gets the building-type map.</summary>
    internal int[] BuildingMap => _buildings;

    /// <summary>Gets the recipe map.</summary>
    internal int[] RecipeMap => _recipes;

    /// <summary>Gets the tier map.</summary>
    internal int[] TierMap => _tiers;

    /// <summary>Gets the tech map.</summary>
    internal int[] TechMap => _techs;

    /// <summary>Gets the civic map.</summary>
    internal int[] CivicMap => _civics;

    /// <summary>Gets the tradition map.</summary>
    internal int[] TraditionMap => _traditions;

    /// <summary>Gets the festival map.</summary>
    internal int[] FestivalMap => _festivals;

    /// <summary>Gets the great-work-type map.</summary>
    internal int[] GreatWorkMap => _greatWorks;

    /// <summary>Gets the monument map.</summary>
    internal int[] MonumentMap => _monuments;

    /// <summary>Gets the happening map.</summary>
    internal int[] HappeningMap => _happenings;

    /// <summary>Gets the era map.</summary>
    internal int[] EraMap => _eras;

    /// <summary>Gets the terrain map.</summary>
    internal int[] TerrainMap => _terrains;

    private static int[] BuildMap<TId, TDef>(string[] oldNames, ContentTable<TId, TDef> table)
        where TId : struct, IContentId<TId>
        where TDef : class
    {
        int[] map = new int[oldNames.Length];
        for (int i = 0; i < oldNames.Length; i++)
        {
            map[i] = table.TryGet(oldNames[i], out TId id) ? id.Value : -1;
        }

        return map;
    }

    // A map is the identity when it covers every current item and every old index points at itself: a removed item
    // fails on a -1, an added one on the length, and a reordered one on a shifted entry.
    private static bool IsIdentityMap(int[] map, int newCount)
    {
        if (map.Length != newCount)
        {
            return false;
        }

        for (int i = 0; i < map.Length; i++)
        {
            if (map[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    private static int Map(int[] map, int old) => (uint)old < (uint)map.Length ? map[old] : -1;

    /// <summary>Gets the save's name for a good index, or a placeholder when the index is out of range.</summary>
    /// <param name="old">The save's raw good ID.</param>
    /// <returns>The name.</returns>
    public string OldGoodName(int old) => OldName(OldGoods, old);

    /// <summary>Gets the save's name for a building-type index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw building-type ID.</param>
    /// <returns>The name.</returns>
    public string OldBuildingName(int old) => OldName(OldBuildings, old);

    /// <summary>Gets the save's name for a recipe index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw recipe ID.</param>
    /// <returns>The name.</returns>
    public string OldRecipeName(int old) => OldName(OldRecipes, old);

    /// <summary>Gets the save's name for a tier index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw tier ID.</param>
    /// <returns>The name.</returns>
    public string OldTierName(int old) => OldName(OldTiers, old);

    /// <summary>Gets the save's name for a tech index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw tech ID.</param>
    /// <returns>The name.</returns>
    public string OldTechName(int old) => OldName(OldTechs, old);

    /// <summary>Gets the save's name for a civic index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw civic ID.</param>
    /// <returns>The name.</returns>
    public string OldCivicName(int old) => OldName(OldCivics, old);

    /// <summary>Gets the save's name for a tradition index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw tradition ID.</param>
    /// <returns>The name.</returns>
    public string OldTraditionName(int old) => OldName(OldTraditions, old);

    /// <summary>Gets the save's name for a festival index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw festival ID.</param>
    /// <returns>The name.</returns>
    public string OldFestivalName(int old) => OldName(OldFestivals, old);

    /// <summary>Gets the save's name for a great-work-type index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw great-work-type ID.</param>
    /// <returns>The name.</returns>
    public string OldGreatWorkName(int old) => OldName(OldGreatWorks, old);

    /// <summary>Gets the save's name for a monument index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw monument ID.</param>
    /// <returns>The name.</returns>
    public string OldMonumentName(int old) => OldName(OldMonuments, old);

    /// <summary>Gets the save's name for a happening index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw happening ID.</param>
    /// <returns>The name.</returns>
    public string OldHappeningName(int old) => OldName(OldHappenings, old);

    /// <summary>Gets the save's name for an era index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw era ID.</param>
    /// <returns>The name.</returns>
    public string OldEraName(int old) => OldName(OldEras, old);

    /// <summary>Gets the save's name for a terrain index, or a placeholder when out of range.</summary>
    /// <param name="old">The save's raw terrain ID.</param>
    /// <returns>The name.</returns>
    public string OldTerrainName(int old) => OldName(OldTerrains, old);

    private static string OldName(string[] names, int old) =>
        (uint)old < (uint)names.Length ? names[old] : $"<unknown {old}>";
}
