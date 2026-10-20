using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using MessagePack;

namespace Evenmere.Sim.Queries.Types;

/// <summary>The kind of value an <see cref="ExplainArg"/> holds, which decides how the view formats it. The values are part of the golden test files; add members at the end.</summary>
public enum ExplainArgKind : byte
{
    /// <summary>A whole number, such as a count, a number of days or a number of workers.</summary>
    Number = 0,

    /// <summary>An <see cref="Amount"/> in milli-units, such as a stock or a rate per day.</summary>
    Amount = 1,

    /// <summary>A <see cref="Ratio"/> in per-mille, such as a fulfilment or a productivity.</summary>
    Ratio = 2,

    /// <summary>A <see cref="GoodId"/>; the view shows the good's name.</summary>
    Good = 3,

    /// <summary>A <see cref="BuildingTypeId"/>; the view shows the building type's name.</summary>
    BuildingType = 4,

    /// <summary>A <see cref="TierId"/>; the view shows the tier's name.</summary>
    Tier = 5,

    /// <summary>A <see cref="RecipeId"/>; the view shows the recipe's name.</summary>
    Recipe = 6,

    /// <summary>A <see cref="TechId"/>; the view shows the tech's name.</summary>
    Tech = 7,

    /// <summary>A <see cref="CivicId"/>; the view shows the civic's name.</summary>
    Civic = 8,

    /// <summary>An <see cref="EntityId"/>; the view shows it as a link to the entity.</summary>
    Entity = 9,

    /// <summary>A <see cref="ServiceKind"/>; the view shows the service's name.</summary>
    Service = 10,

    /// <summary>A <see cref="Season"/>; the view shows the season's name.</summary>
    Season = 11,

    /// <summary>A <see cref="StallReason"/>; the view shows the reason's text.</summary>
    Stall = 12,
}

/// <summary>
/// One typed value in an explanation line, such as the stock of a good or the name of a stalled building's recipe. The
/// view formats it by <see cref="Kind"/>; the simulation never produces English. Create one with <see cref="Of(Amount)"/> and
/// its overloads, and read it back with the <c>As…</c> methods.
/// </summary>
/// <param name="Kind">What the value is.</param>
/// <param name="Value">The raw value: milli-units, per-mille, a count, or an ID's raw value, as the kind says.</param>
public readonly record struct ExplainArg(ExplainArgKind Kind, long Value)
{
    /// <summary>Creates a whole-number argument.</summary>
    /// <param name="number">The number, such as a count or a number of days.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(int number) => new(ExplainArgKind.Number, number);

    /// <summary>Creates an amount argument.</summary>
    /// <param name="amount">The amount, in milli-units.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(Amount amount) => new(ExplainArgKind.Amount, amount.Milli);

    /// <summary>Creates a ratio argument.</summary>
    /// <param name="ratio">The ratio, in per-mille.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(Ratio ratio) => new(ExplainArgKind.Ratio, ratio.PerMille);

    /// <summary>Creates a good argument.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(GoodId good) => new(ExplainArgKind.Good, good.Value);

    /// <summary>Creates a building-type argument.</summary>
    /// <param name="type">The building type.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(BuildingTypeId type) => new(ExplainArgKind.BuildingType, type.Value);

    /// <summary>Creates a tier argument.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(TierId tier) => new(ExplainArgKind.Tier, tier.Value);

    /// <summary>Creates a recipe argument.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(RecipeId recipe) => new(ExplainArgKind.Recipe, recipe.Value);

    /// <summary>Creates a tech argument.</summary>
    /// <param name="tech">The tech.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(TechId tech) => new(ExplainArgKind.Tech, tech.Value);

    /// <summary>Creates a civic argument.</summary>
    /// <param name="civic">The civic.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(CivicId civic) => new(ExplainArgKind.Civic, civic.Value);

    /// <summary>Creates an entity argument.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(EntityId entity) => new(ExplainArgKind.Entity, entity.Value);

    /// <summary>Creates a service argument.</summary>
    /// <param name="service">The service.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(ServiceKind service) => new(ExplainArgKind.Service, (long)service);

    /// <summary>Creates a season argument.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(Season season) => new(ExplainArgKind.Season, (long)season);

    /// <summary>Creates a stall-reason argument.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The argument.</returns>
    public static ExplainArg Of(StallReason reason) => new(ExplainArgKind.Stall, (long)reason);

    /// <summary>Reads a <see cref="ExplainArgKind.Number"/> argument.</summary>
    /// <returns>The number.</returns>
    public int AsNumber() => checked((int)Value);

    /// <summary>Reads an <see cref="ExplainArgKind.Amount"/> argument.</summary>
    /// <returns>The amount.</returns>
    public Amount AsAmount() => Amount.FromMilli(Value);

    /// <summary>Reads a <see cref="ExplainArgKind.Ratio"/> argument.</summary>
    /// <returns>The ratio.</returns>
    public Ratio AsRatio() => Ratio.FromPerMille(checked((int)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Good"/> argument.</summary>
    /// <returns>The good.</returns>
    public GoodId AsGood() => new(checked((ushort)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.BuildingType"/> argument.</summary>
    /// <returns>The building type.</returns>
    public BuildingTypeId AsBuildingType() => new(checked((ushort)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Tier"/> argument.</summary>
    /// <returns>The tier.</returns>
    public TierId AsTier() => new(checked((ushort)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Recipe"/> argument.</summary>
    /// <returns>The recipe.</returns>
    public RecipeId AsRecipe() => new(checked((ushort)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Tech"/> argument.</summary>
    /// <returns>The tech.</returns>
    public TechId AsTech() => new(checked((ushort)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Civic"/> argument.</summary>
    /// <returns>The civic.</returns>
    public CivicId AsCivic() => new(checked((ushort)Value));

    /// <summary>Reads an <see cref="ExplainArgKind.Entity"/> argument.</summary>
    /// <returns>The entity.</returns>
    public EntityId AsEntity() => new(checked((int)Value));

    /// <summary>Reads a <see cref="ExplainArgKind.Service"/> argument.</summary>
    /// <returns>The service.</returns>
    public ServiceKind AsService() => (ServiceKind)Value;

    /// <summary>Reads a <see cref="ExplainArgKind.Season"/> argument.</summary>
    /// <returns>The season.</returns>
    public Season AsSeason() => (Season)Value;

    /// <summary>Reads a <see cref="ExplainArgKind.Stall"/> argument.</summary>
    /// <returns>The stall reason.</returns>
    public StallReason AsStall() => (StallReason)Value;
}

/// <summary>What a <see cref="SubjectLink"/> points at.</summary>
public enum SubjectKind : byte
{
    /// <summary>Nothing; the default value.</summary>
    None = 0,

    /// <summary>One building or house, by entity ID; the view can focus the camera on it or open its inspector.</summary>
    Building = 1,

    /// <summary>One good; the view can open the good's explanation.</summary>
    Good = 2,

    /// <summary>One population tier.</summary>
    Tier = 3,

    /// <summary>A building type, for lines about all buildings of a type.</summary>
    BuildingType = 4,
}

/// <summary>A link from an explanation line (or an advice note) to the thing it is about, so the view can make it clickable.</summary>
/// <param name="Kind">What the link points at.</param>
/// <param name="Value">The raw ID value; read it with the <c>As…</c> methods.</param>
[MessagePackObject]
public readonly record struct SubjectLink([property: Key(0)] SubjectKind Kind, [property: Key(1)] int Value)
{
    /// <summary>Gets the link that points at nothing.</summary>
    public static SubjectLink None => default;

    /// <summary>Creates a link to a building or house.</summary>
    /// <param name="building">The building's entity ID.</param>
    /// <returns>The link.</returns>
    public static SubjectLink ForBuilding(EntityId building) => new(SubjectKind.Building, building.Value);

    /// <summary>Creates a link to a good.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The link.</returns>
    public static SubjectLink ForGood(GoodId good) => new(SubjectKind.Good, good.Value);

    /// <summary>Creates a link to a tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The link.</returns>
    public static SubjectLink ForTier(TierId tier) => new(SubjectKind.Tier, tier.Value);

    /// <summary>Creates a link to a building type.</summary>
    /// <param name="type">The building type.</param>
    /// <returns>The link.</returns>
    public static SubjectLink ForBuildingType(BuildingTypeId type) => new(SubjectKind.BuildingType, type.Value);

    /// <summary>Reads the link as a building; valid when <see cref="Kind"/> is <see cref="SubjectKind.Building"/>.</summary>
    /// <returns>The building's entity ID.</returns>
    public EntityId AsBuilding() => new(Value);

    /// <summary>Reads the link as a good; valid when <see cref="Kind"/> is <see cref="SubjectKind.Good"/>.</summary>
    /// <returns>The good.</returns>
    public GoodId AsGood() => new(checked((ushort)Value));

    /// <summary>Reads the link as a tier; valid when <see cref="Kind"/> is <see cref="SubjectKind.Tier"/>.</summary>
    /// <returns>The tier.</returns>
    public TierId AsTier() => new(checked((ushort)Value));

    /// <summary>Reads the link as a building type; valid when <see cref="Kind"/> is <see cref="SubjectKind.BuildingType"/>.</summary>
    /// <returns>The building type.</returns>
    public BuildingTypeId AsBuildingType() => new(checked((ushort)Value));
}

/// <summary>
/// One line of an explanation: a locale key, the typed values the key's text mentions (in order, as <c>{0}</c>, <c>{1}</c>
/// and so on), links to the things it is about, and the lines that explain it further. It carries no English text; the
/// view looks the key up in the locale table. Two lines with the same contents are equal.
/// </summary>
/// <param name="Key">The locale key, such as <c>explain.good.supply</c>.</param>
/// <param name="Args">The values the text mentions, in order; empty when the text has none.</param>
/// <param name="Subjects">Links to what the line is about; empty when it has none.</param>
/// <param name="Children">The lines that explain this one, such as the causes of a shortage; empty for a leaf.</param>
public sealed record ExplanationLine(
    string Key,
    ImmutableArray<ExplainArg> Args,
    ImmutableArray<SubjectLink> Subjects,
    ImmutableArray<ExplanationLine> Children)
{
    /// <summary>Creates a line with arguments and no links or children.</summary>
    /// <param name="key">The locale key.</param>
    /// <param name="args">The values the text mentions, in order.</param>
    /// <returns>The line.</returns>
    public static ExplanationLine Leaf(string key, params ReadOnlySpan<ExplainArg> args) => new(key, [.. args], [], []);

    /// <summary>Returns this line with children, replacing any it has.</summary>
    /// <param name="children">The lines that explain this one.</param>
    /// <returns>The new line.</returns>
    public ExplanationLine WithChildren(params ReadOnlySpan<ExplanationLine> children) => this with { Children = [.. children] };

    /// <summary>Returns this line with links to its subjects, replacing any it has.</summary>
    /// <param name="subjects">What the line is about.</param>
    /// <returns>The new line.</returns>
    public ExplanationLine WithSubjects(params ReadOnlySpan<SubjectLink> subjects) => this with { Subjects = [.. subjects] };

    /// <inheritdoc />
    public bool Equals(ExplanationLine? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && ValueArrays.Equal(Args, other.Args)
        && ValueArrays.Equal(Subjects, other.Subjects)
        && ValueArrays.Equal(Children, other.Children);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Key, ValueArrays.Hash(Args), ValueArrays.Hash(Subjects), ValueArrays.Hash(Children));
}

/// <summary>
/// A structured answer to "why is this the way it is": a tree of lines with values and links, rooted at one headline line
/// (<c>IExplainQueries</c>). Two explanations with the same lines are equal, so tests can compare whole trees.
/// </summary>
/// <param name="Root">The headline line; its children hold the causes.</param>
public sealed record Explanation(ExplanationLine Root);
