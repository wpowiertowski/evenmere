using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content;

/// <summary>
/// All game content, loaded once and immutable (ADR-0004). Every content type is a <see cref="ContentTable{TId, TDef}"/>:
/// an array of definitions indexed by typed ID, plus lookups between string IDs and typed IDs. Integer IDs are assigned
/// from 0 per type in ordinal order of the string IDs. Systems receive the database through <c>SimContext</c> and never
/// read files. Build one with <see cref="ContentLoader"/>.
/// </summary>
public sealed class ContentDb
{
    private readonly Dictionary<Type, object> _tablesById;

    internal ContentDb(
        ulong contentHash,
        ContentTable<GoodId, GoodDef> goods,
        ContentTable<BuildingTypeId, BuildingDef> buildings,
        ContentTable<RecipeId, RecipeDef> recipes,
        ContentTable<TierId, TierDef> tiers,
        ContentTable<TechId, TechDef> techs,
        ContentTable<CivicId, CivicDef> civics,
        ContentTable<TraditionId, TraditionDef> traditions,
        ContentTable<FestivalId, FestivalDef> festivals,
        ContentTable<GreatWorkTypeId, GreatWorkTypeDef> greatWorks,
        ContentTable<MonumentId, MonumentDef> monuments,
        ContentTable<HappeningId, HappeningDef> happenings,
        ContentTable<EraId, EraDef> eras,
        ContentTable<TerrainId, TerrainDef> terrains,
        RulesDef rules,
        RequiredIds required)
    {
        ContentHash = contentHash;
        Goods = goods;
        Buildings = buildings;
        Recipes = recipes;
        Tiers = tiers;
        Techs = techs;
        Civics = civics;
        Traditions = traditions;
        Festivals = festivals;
        GreatWorks = greatWorks;
        Monuments = monuments;
        Happenings = happenings;
        Eras = eras;
        Terrains = terrains;
        Rules = rules;
        Required = required;

        ErasByOrder = [.. eras.Items.Select(static (e, i) => (e.Order, Id: EraId.From(checked((ushort)i)))).OrderBy(static e => e.Order).Select(static e => e.Id)];
        TiersByOrder = [.. tiers.Items.Select(static (t, i) => (t.Order, Id: TierId.From(checked((ushort)i)))).OrderBy(static t => t.Order).Select(static t => t.Id)];

        // Looked up by type only, never iterated.
        _tablesById = new Dictionary<Type, object>
        {
            [typeof(GoodId)] = goods,
            [typeof(BuildingTypeId)] = buildings,
            [typeof(RecipeId)] = recipes,
            [typeof(TierId)] = tiers,
            [typeof(TechId)] = techs,
            [typeof(CivicId)] = civics,
            [typeof(TraditionId)] = traditions,
            [typeof(FestivalId)] = festivals,
            [typeof(GreatWorkTypeId)] = greatWorks,
            [typeof(MonumentId)] = monuments,
            [typeof(HappeningId)] = happenings,
            [typeof(EraId)] = eras,
            [typeof(TerrainId)] = terrains,
        };

        MonumentOfBuilding = BuildMonumentIndex(monuments, buildings.Count);
    }

    /// <summary>
    /// Gets the content hash: XxHash64 over the files under every content root, in load order, except schemas and Markdown (see
    /// <see cref="ContentLoader"/> for the exact byte layout). Saves store it and a mismatch is reported, never fatal.
    /// </summary>
    public ulong ContentHash { get; }

    /// <summary>Gets the goods.</summary>
    public ContentTable<GoodId, GoodDef> Goods { get; }

    /// <summary>Gets the building types.</summary>
    public ContentTable<BuildingTypeId, BuildingDef> Buildings { get; }

    /// <summary>Gets the production recipes.</summary>
    public ContentTable<RecipeId, RecipeDef> Recipes { get; }

    /// <summary>Gets the population tiers.</summary>
    public ContentTable<TierId, TierDef> Tiers { get; }

    /// <summary>Gets the technologies.</summary>
    public ContentTable<TechId, TechDef> Techs { get; }

    /// <summary>Gets the civics.</summary>
    public ContentTable<CivicId, CivicDef> Civics { get; }

    /// <summary>Gets the traditions.</summary>
    public ContentTable<TraditionId, TraditionDef> Traditions { get; }

    /// <summary>Gets the festivals.</summary>
    public ContentTable<FestivalId, FestivalDef> Festivals { get; }

    /// <summary>Gets the great-work types.</summary>
    public ContentTable<GreatWorkTypeId, GreatWorkTypeDef> GreatWorks { get; }

    /// <summary>Gets the monuments and wonders.</summary>
    public ContentTable<MonumentId, MonumentDef> Monuments { get; }

    /// <summary>Gets the happenings.</summary>
    public ContentTable<HappeningId, HappeningDef> Happenings { get; }

    /// <summary>Gets the eras.</summary>
    public ContentTable<EraId, EraDef> Eras { get; }

    /// <summary>Gets the terrains.</summary>
    public ContentTable<TerrainId, TerrainDef> Terrains { get; }

    /// <summary>Gets the game-wide tunables.</summary>
    public RulesDef Rules { get; }

    /// <summary>Gets the IDs the engine cannot start without.</summary>
    public RequiredIds Required { get; }

    /// <summary>Gets the eras in chronological order (ascending <see cref="EraDef.Order"/>), because era IDs are in string order.</summary>
    public ImmutableArray<EraId> ErasByOrder { get; }

    /// <summary>Gets the tiers from the lowest to the highest (ascending <see cref="TierDef.Order"/>), because tier IDs are in string order.</summary>
    public ImmutableArray<TierId> TiersByOrder { get; }

    private ImmutableArray<MonumentId?> MonumentOfBuilding { get; }

    /// <summary>Gets a good's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public GoodDef Get(GoodId id) => Goods.Get(id);

    /// <summary>Gets a building type's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public BuildingDef Get(BuildingTypeId id) => Buildings.Get(id);

    /// <summary>Gets a recipe's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public RecipeDef Get(RecipeId id) => Recipes.Get(id);

    /// <summary>Gets a tier's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public TierDef Get(TierId id) => Tiers.Get(id);

    /// <summary>Gets a tech's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public TechDef Get(TechId id) => Techs.Get(id);

    /// <summary>Gets a civic's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public CivicDef Get(CivicId id) => Civics.Get(id);

    /// <summary>Gets a tradition's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public TraditionDef Get(TraditionId id) => Traditions.Get(id);

    /// <summary>Gets a festival's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public FestivalDef Get(FestivalId id) => Festivals.Get(id);

    /// <summary>Gets a great-work type's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public GreatWorkTypeDef Get(GreatWorkTypeId id) => GreatWorks.Get(id);

    /// <summary>Gets a monument's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public MonumentDef Get(MonumentId id) => Monuments.Get(id);

    /// <summary>Gets a happening's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public HappeningDef Get(HappeningId id) => Happenings.Get(id);

    /// <summary>Gets an era's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public EraDef Get(EraId id) => Eras.Get(id);

    /// <summary>Gets a terrain's definition.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    public TerrainDef Get(TerrainId id) => Terrains.Get(id);

    /// <summary>Finds the ID of a named item of any type; the type is inferred from the <see langword="out"/> variable.</summary>
    /// <typeparam name="TId">The content ID type.</typeparam>
    /// <param name="name">The string ID.</param>
    /// <param name="id">The ID, when found.</param>
    /// <returns><see langword="true"/> if an item of that type has that name.</returns>
    public bool TryGet<TId>(string name, out TId id)
        where TId : struct, IContentId<TId> => TableFor<TId>().TryGet(name, out id);

    /// <summary>Gets the ID of a named item.</summary>
    /// <typeparam name="TId">The content ID type.</typeparam>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    /// <exception cref="KeyNotFoundException">No item of that type has that name.</exception>
    public TId IdOf<TId>(string name)
        where TId : struct, IContentId<TId> => TableFor<TId>().IdOf(name);

    /// <summary>Gets the string ID of an item.</summary>
    /// <typeparam name="TId">The content ID type.</typeparam>
    /// <param name="id">The ID.</param>
    /// <returns>The string ID.</returns>
    public string NameOf<TId>(TId id)
        where TId : struct, IContentId<TId> => TableFor<TId>().NameOf(id);

    /// <summary>Finds the monument or wonder that is built as a building.</summary>
    /// <param name="building">The building type.</param>
    /// <param name="monument">The monument's ID, when the building is one.</param>
    /// <returns><see langword="true"/> if a <see cref="MonumentDef"/> names the building.</returns>
    public bool TryGetMonumentOf(BuildingTypeId building, out MonumentId monument)
    {
        MonumentId? found = building.Value < MonumentOfBuilding.Length ? MonumentOfBuilding[building.Value] : null;
        monument = found.GetValueOrDefault();
        return found.HasValue;
    }

    private static ImmutableArray<MonumentId?> BuildMonumentIndex(ContentTable<MonumentId, MonumentDef> monuments, int buildingCount)
    {
        var index = new MonumentId?[buildingCount];
        for (int i = 0; i < monuments.Count; i++)
        {
            // If two monuments name one building, the lowest monument ID wins; the linter reports the clash.
            ref MonumentId? slot = ref index[monuments.Items[i].Building.Value];
            slot ??= MonumentId.From(checked((ushort)i));
        }

        return [.. index];
    }

    private IContentTable<TId> TableFor<TId>()
        where TId : struct, IContentId<TId> =>
        _tablesById.TryGetValue(typeof(TId), out object? table)
            ? (IContentTable<TId>)table
            : throw new ArgumentException($"{typeof(TId).Name} is not a content ID type.");
}
