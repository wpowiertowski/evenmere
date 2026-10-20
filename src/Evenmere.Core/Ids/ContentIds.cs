// Content IDs are one concrete `readonly record struct XxxId(ushort Value)` per kind, not a generic
// `ContentId<TTag>` behind global using aliases. An alias is visible only in the project that declares
// it, so it would not cross the Core -> Content -> Sim -> game assembly boundaries (ADR-0002) without
// editing every consuming csproj. Concrete types are real public types everywhere, and a GoodId can
// never be passed where a RecipeId is expected. Generic code uses the shared IContentId<TSelf> interface.

namespace Evenmere.Core.Ids;

/// <summary>Identifies a good. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct GoodId(ushort Value) : IContentId<GoodId>
{
    /// <inheritdoc />
    public static GoodId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"GoodId({Value})";
}

/// <summary>Identifies a type of building. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct BuildingTypeId(ushort Value) : IContentId<BuildingTypeId>
{
    /// <inheritdoc />
    public static BuildingTypeId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"BuildingTypeId({Value})";
}

/// <summary>Identifies a production recipe. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct RecipeId(ushort Value) : IContentId<RecipeId>
{
    /// <inheritdoc />
    public static RecipeId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"RecipeId({Value})";
}

/// <summary>Identifies a population tier. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct TierId(ushort Value) : IContentId<TierId>
{
    /// <inheritdoc />
    public static TierId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"TierId({Value})";
}

/// <summary>Identifies a technology. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct TechId(ushort Value) : IContentId<TechId>
{
    /// <inheritdoc />
    public static TechId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"TechId({Value})";
}

/// <summary>Identifies a civic in the culture tree. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct CivicId(ushort Value) : IContentId<CivicId>
{
    /// <inheritdoc />
    public static CivicId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"CivicId({Value})";
}

/// <summary>Identifies a tradition in the culture tree. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct TraditionId(ushort Value) : IContentId<TraditionId>
{
    /// <inheritdoc />
    public static TraditionId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"TraditionId({Value})";
}

/// <summary>Identifies a festival. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct FestivalId(ushort Value) : IContentId<FestivalId>
{
    /// <inheritdoc />
    public static FestivalId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"FestivalId({Value})";
}

/// <summary>Identifies a type of great work. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct GreatWorkTypeId(ushort Value) : IContentId<GreatWorkTypeId>
{
    /// <inheritdoc />
    public static GreatWorkTypeId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"GreatWorkTypeId({Value})";
}

/// <summary>Identifies a monument. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct MonumentId(ushort Value) : IContentId<MonumentId>
{
    /// <inheritdoc />
    public static MonumentId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"MonumentId({Value})";
}

/// <summary>Identifies a happening (a gentle world event). The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct HappeningId(ushort Value) : IContentId<HappeningId>
{
    /// <inheritdoc />
    public static HappeningId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"HappeningId({Value})";
}

/// <summary>Identifies an era. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct EraId(ushort Value) : IContentId<EraId>
{
    /// <inheritdoc />
    public static EraId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"EraId({Value})";
}

/// <summary>Identifies a terrain type. The raw value is a dense index assigned by the content loader.</summary>
/// <param name="Value">The raw index.</param>
public readonly record struct TerrainId(ushort Value) : IContentId<TerrainId>
{
    /// <inheritdoc />
    public static TerrainId From(ushort value) => new(value);

    /// <inheritdoc />
    public override string ToString() => $"TerrainId({Value})";
}
