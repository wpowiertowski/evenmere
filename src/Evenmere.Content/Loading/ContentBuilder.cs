using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Loading;

/// <summary>
/// Phase 2 of loading: interns every ID, then reads each item's JSON into a typed definition, resolving references
/// to typed IDs as it goes. Returns a <see cref="ContentDb"/> only if no error was reported anywhere, including in
/// phase 1.
/// </summary>
internal static class ContentBuilder
{
    public static ContentDb? Build(RawContent raw, List<ContentDiagnostic> diagnostics)
    {
        var tables = ResolveTables.Create(raw, diagnostics);

        // Eras first: every other item's "era" field is checked against the era numbers they define.
        EraDef?[] eras = Convert(raw, ContentType.Eras, tables.Eras, tables, diagnostics, static (o, id, _) => DefReaders.ReadEra(o, id));
        CheckUniqueOrders(raw, ContentType.Eras, tables.Eras, eras, static e => e.Order, diagnostics);
        foreach (EraDef? era in eras)
        {
            if (era is not null)
            {
                tables.EraOrders.Add(era.Order);
            }
        }

        GoodDef?[] goods = Convert(raw, ContentType.Goods, tables.Goods, tables, diagnostics, DefReaders.ReadGood);
        BuildingDef?[] buildings = Convert(raw, ContentType.Buildings, tables.Buildings, tables, diagnostics, static (o, id, _) => DefReaders.ReadBuilding(o, id));
        RecipeDef?[] recipes = Convert(raw, ContentType.Recipes, tables.Recipes, tables, diagnostics, static (o, id, _) => DefReaders.ReadRecipe(o, id));
        TierDef?[] tiers = Convert(raw, ContentType.Tiers, tables.Tiers, tables, diagnostics, static (o, id, _) => DefReaders.ReadTier(o, id));
        CheckUniqueOrders(raw, ContentType.Tiers, tables.Tiers, tiers, static t => t.Order, diagnostics);
        TechDef?[] techs = Convert(raw, ContentType.Techs, tables.Techs, tables, diagnostics, static (o, id, _) => DefReaders.ReadTech(o, id));
        CivicDef?[] civics = Convert(raw, ContentType.Civics, tables.Civics, tables, diagnostics, static (o, id, _) => DefReaders.ReadCivic(o, id));
        TraditionDef?[] traditions = Convert(raw, ContentType.Traditions, tables.Traditions, tables, diagnostics, static (o, id, _) => DefReaders.ReadTradition(o, id));
        FestivalDef?[] festivals = Convert(raw, ContentType.Festivals, tables.Festivals, tables, diagnostics, static (o, id, _) => DefReaders.ReadFestival(o, id));
        GreatWorkTypeDef?[] greatWorks = Convert(raw, ContentType.GreatWorks, tables.GreatWorks, tables, diagnostics, static (o, id, _) => DefReaders.ReadGreatWorkType(o, id));
        MonumentDef?[] monuments = Convert(raw, ContentType.Monuments, tables.Monuments, tables, diagnostics, static (o, id, _) => DefReaders.ReadMonument(o, id));
        HappeningDef?[] happenings = Convert(raw, ContentType.Happenings, tables.Happenings, tables, diagnostics, static (o, id, _) => DefReaders.ReadHappening(o, id));
        TerrainDef?[] terrains = Convert(raw, ContentType.Terrains, tables.Terrains, tables, diagnostics, static (o, id, _) => DefReaders.ReadTerrain(o, id));
        RulesDef? rules = ReadRules(raw, tables, diagnostics);

        RequiredIds? required = ResolveRequired(tables, diagnostics);

        if (diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error) || rules is null || required is null)
        {
            return null;
        }

        return new ContentDb(
            raw.ContentHash,
            Table(tables.Goods, goods),
            Table(tables.Buildings, buildings),
            Table(tables.Recipes, recipes),
            Table(tables.Tiers, tiers),
            Table(tables.Techs, techs),
            Table(tables.Civics, civics),
            Table(tables.Traditions, traditions),
            Table(tables.Festivals, festivals),
            Table(tables.GreatWorks, greatWorks),
            Table(tables.Monuments, monuments),
            Table(tables.Happenings, happenings),
            Table(tables.Eras, eras),
            Table(tables.Terrains, terrains),
            rules,
            required);
    }

    private static TDef?[] Convert<TId, TDef>(
        RawContent raw,
        ContentType type,
        InternTable<TId> table,
        ResolveTables tables,
        List<ContentDiagnostic> sink,
        Func<ObjectView, TId, string, TDef> read)
        where TId : struct, IContentId<TId>
        where TDef : class
    {
        var result = new TDef?[table.Names.Length];
        foreach (RawItem item in raw.OfType(type))
        {
            // A file that failed in phase 1 has no JSON and was already reported; its ID is still interned.
            if (item.Json is not { } json || !table.TryGet(item.Id, out TId id))
            {
                continue;
            }

            var reader = new ItemReader(item.File, sink, tables);
            using (ObjectView? o = reader.OpenObject(json, string.Empty))
            {
                if (o is null)
                {
                    continue;
                }

                string declared = o.ReqString("id");
                if (declared.Length > 0 && !string.Equals(declared, item.Id, StringComparison.Ordinal))
                {
                    reader.Error("/id", $"'id' is '{declared}' but the file name says '{item.Id}'; they must match");
                }

                result[id.Value] = read(o, id, item.Id);
            }

            // Disposing the view reports unknown properties, so the error count is final only here.
            if (reader.ErrorCount > 0)
            {
                result[id.Value] = null;
            }
        }

        return result;
    }

    private static RulesDef? ReadRules(RawContent raw, ResolveTables tables, List<ContentDiagnostic> sink)
    {
        RawItem? item = raw.OfType(ContentType.Rules).FirstOrDefault();
        if (item is null)
        {
            sink.Add(new ContentDiagnostic(
                DiagnosticCodes.Evl001,
                DiagnosticSeverity.Error,
                $"{ContentType.Rules.DirectoryName()}/{ContentTypes.RulesId}.json",
                string.Empty,
                "the game rules file is required"));
            return null;
        }

        if (item.Json is not { } json)
        {
            return null;
        }

        var reader = new ItemReader(item.File, sink, tables);
        RulesDef? rules = null;
        using (ObjectView? o = reader.OpenObject(json, string.Empty))
        {
            if (o is not null)
            {
                rules = DefReaders.ReadRules(o);
            }
        }

        return reader.ErrorCount > 0 ? null : rules;
    }

    private static void CheckUniqueOrders<TId, TDef>(
        RawContent raw,
        ContentType type,
        InternTable<TId> table,
        TDef?[] defs,
        Func<TDef, int> orderOf,
        List<ContentDiagnostic> sink)
        where TId : struct, IContentId<TId>
        where TDef : class
    {
        var firstWith = new Dictionary<int, string>();
        foreach (RawItem item in raw.OfType(type))
        {
            if (!table.TryGet(item.Id, out TId id) || defs[id.Value] is not { } def)
            {
                continue;
            }

            int order = orderOf(def);
            if (firstWith.TryGetValue(order, out string? other))
            {
                sink.Add(new ContentDiagnostic(
                    DiagnosticCodes.Evl001,
                    DiagnosticSeverity.Error,
                    item.File,
                    "/order",
                    $"{type.SingularName()} order {order} is already used by '{other}'; orders must be unique"));
            }
            else
            {
                firstWith.Add(order, item.Id);
            }
        }
    }

    private static RequiredIds? ResolveRequired(ResolveTables tables, List<ContentDiagnostic> sink)
    {
        bool ok = true;
        BuildingTypeId Building(string name)
        {
            if (tables.Buildings.TryGet(name, out BuildingTypeId id))
            {
                return id;
            }

            ok = false;
            sink.Add(MissingRequired(ContentType.Buildings, name));
            return default;
        }

        BuildingTypeId foundersHall = Building(RequiredIds.FoundersHallName);
        BuildingTypeId settlerHouse = Building(RequiredIds.SettlerHouseName);
        BuildingTypeId dirtRoad = Building(RequiredIds.DirtRoadName);
        BuildingTypeId storehouse = Building(RequiredIds.StorehouseName);

        if (!tables.Tiers.TryGet(RequiredIds.SettlersName, out TierId settlers))
        {
            ok = false;
            sink.Add(MissingRequired(ContentType.Tiers, RequiredIds.SettlersName));
        }

        return ok ? new RequiredIds(foundersHall, settlerHouse, dirtRoad, storehouse, settlers) : null;
    }

    private static ContentDiagnostic MissingRequired(ContentType type, string name) =>
        new(
            DiagnosticCodes.Evl002,
            DiagnosticSeverity.Error,
            $"{type.DirectoryName()}/{name}.json",
            string.Empty,
            $"the required {type.SingularName()} '{name}' is not defined");

    private static ContentTable<TId, TDef> Table<TId, TDef>(InternTable<TId> ids, TDef?[] defs)
        where TId : struct, IContentId<TId>
        where TDef : class
    {
        ImmutableArray<TDef>.Builder items = ImmutableArray.CreateBuilder<TDef>(defs.Length);
        foreach (TDef? def in defs)
        {
            // Build runs this only when no error was reported, and then every item converted.
            items.Add(def ?? throw new InvalidOperationException("A definition is missing although loading reported no error."));
        }

        return new ContentTable<TId, TDef>(ids.Names, items.MoveToImmutable());
    }
}
