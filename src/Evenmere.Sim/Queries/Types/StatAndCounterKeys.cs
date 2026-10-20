using Evenmere.Core.Ids;
using MessagePack;

namespace Evenmere.Sim.Queries.Types;

/// <summary>Which series of daily samples the statistics keep. The values are part of the save format; add members at the end.</summary>
public enum StatKind : byte
{
    /// <summary>The island stock of a good at the end of the day, in milli-units.</summary>
    Stock = 0,

    /// <summary>The amount of a good produced during the day, in milli-units.</summary>
    Produced = 1,

    /// <summary>The amount of a good consumed during the day, in milli-units.</summary>
    Consumed = 2,

    /// <summary>The demand for a good during the day, in milli-units.</summary>
    Demand = 3,

    /// <summary>The residents of a tier at the end of the day, as a whole count.</summary>
    Residents = 4,

    /// <summary>The knowledge points credited during the day, in milli-units.</summary>
    KnowledgePerDay = 5,

    /// <summary>The culture points credited during the day, in milli-units.</summary>
    CulturePerDay = 6,

    /// <summary>The transport jobs active at the end of the day, as a whole count.</summary>
    ActiveJobs = 7,
}

/// <summary>Names one daily series for <c>IStatisticsQueries.Series</c>: a kind, and the good or tier it is about where that applies.</summary>
/// <param name="Kind">The series kind.</param>
/// <param name="Subject">The raw ID of the good (for the goods series) or tier (for <see cref="StatKind.Residents"/>); 0 for series without a subject.</param>
[MessagePackObject]
public readonly record struct StatKey([property: Key(0)] StatKind Kind, [property: Key(1)] int Subject)
{
    /// <summary>Gets the key of a good's end-of-day stock.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static StatKey Stock(GoodId good) => new(StatKind.Stock, good.Value);

    /// <summary>Gets the key of the amount of a good produced per day.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static StatKey Produced(GoodId good) => new(StatKind.Produced, good.Value);

    /// <summary>Gets the key of the amount of a good consumed per day.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static StatKey Consumed(GoodId good) => new(StatKind.Consumed, good.Value);

    /// <summary>Gets the key of a good's daily demand.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static StatKey Demand(GoodId good) => new(StatKind.Demand, good.Value);

    /// <summary>Gets the key of a tier's end-of-day residents.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The key.</returns>
    public static StatKey Residents(TierId tier) => new(StatKind.Residents, tier.Value);

    /// <summary>Gets the key of the knowledge credited per day.</summary>
    public static StatKey KnowledgePerDay => new(StatKind.KnowledgePerDay, 0);

    /// <summary>Gets the key of the culture credited per day.</summary>
    public static StatKey CulturePerDay => new(StatKind.CulturePerDay, 0);

    /// <summary>Gets the key of the number of active transport jobs at the end of the day.</summary>
    public static StatKey ActiveJobs => new(StatKind.ActiveJobs, 0);
}

/// <summary>Which milestone counter a <see cref="CounterKey"/> names. The values are part of the save format; add members at the end.</summary>
public enum CounterKind : byte
{
    /// <summary>The cumulative amount of a good produced, in milli-units.</summary>
    Produced = 0,

    /// <summary>The cumulative amount of a good consumed by residents, in milli-units.</summary>
    Consumed = 1,

    /// <summary>The cumulative number of buildings of a type that were completed.</summary>
    Built = 2,

    /// <summary>The cumulative number of road tiles laid (of any road type).</summary>
    RoadTilesLaid = 3,
}

/// <summary>
/// Names one monotonic milestone counter, the kind that conditions such as inspirations read: a kind, and the good or
/// building type it is about where that applies.
/// </summary>
/// <param name="Kind">The counter kind.</param>
/// <param name="Subject">The raw ID of the good (<see cref="CounterKind.Produced"/>, <see cref="CounterKind.Consumed"/>) or building type (<see cref="CounterKind.Built"/>); 0 for <see cref="CounterKind.RoadTilesLaid"/>.</param>
[MessagePackObject]
public readonly record struct CounterKey([property: Key(0)] CounterKind Kind, [property: Key(1)] int Subject)
{
    /// <summary>Gets the key of the cumulative production of a good.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static CounterKey Produced(GoodId good) => new(CounterKind.Produced, good.Value);

    /// <summary>Gets the key of the cumulative consumption of a good.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The key.</returns>
    public static CounterKey Consumed(GoodId good) => new(CounterKind.Consumed, good.Value);

    /// <summary>Gets the key of the number of buildings of a type completed so far.</summary>
    /// <param name="type">The building type.</param>
    /// <returns>The key.</returns>
    public static CounterKey Built(BuildingTypeId type) => new(CounterKind.Built, type.Value);

    /// <summary>Gets the key of the number of road tiles laid so far.</summary>
    public static CounterKey RoadTilesLaid => new(CounterKind.RoadTilesLaid, 0);
}
