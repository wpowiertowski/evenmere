namespace Evenmere.Sim.Queries;

/// <summary>The query facade: it holds each system's query object and hands them out through <see cref="ISimQueries"/>.</summary>
internal sealed class SimQueries : ISimQueries
{
    /// <inheritdoc />
    public required IMapQueries Map { get; init; }

    /// <inheritdoc />
    public required IRoadsQueries Roads { get; init; }

    /// <inheritdoc />
    public required IStorageQueries Storage { get; init; }

    /// <inheritdoc />
    public required IModifiersQueries Modifiers { get; init; }

    /// <inheritdoc />
    public required IConstructionQueries Construction { get; init; }

    /// <inheritdoc />
    public required IWorkforceQueries Workforce { get; init; }

    /// <inheritdoc />
    public required IProductionQueries Production { get; init; }

    /// <inheritdoc />
    public required ILogisticsQueries Logistics { get; init; }

    /// <inheritdoc />
    public required IExplorationQueries Exploration { get; init; }

    /// <inheritdoc />
    public required INeedsQueries Needs { get; init; }

    /// <inheritdoc />
    public required IPopulationQueries Population { get; init; }

    /// <inheritdoc />
    public required IEconomyQueries Economy { get; init; }

    /// <inheritdoc />
    public required IResearchQueries Research { get; init; }

    /// <inheritdoc />
    public required ICultureQueries Culture { get; init; }

    /// <inheritdoc />
    public required IProgressionQueries Progression { get; init; }

    /// <inheritdoc />
    public required IHappeningsQueries Happenings { get; init; }

    /// <inheritdoc />
    public required IAdvisorsQueries Advisors { get; init; }

    /// <inheritdoc />
    public required IStatisticsQueries Statistics { get; init; }

    /// <inheritdoc />
    public required IPlacementQueries Placement { get; init; }

    /// <inheritdoc />
    public required IExplainQueries Explain { get; init; }
}
