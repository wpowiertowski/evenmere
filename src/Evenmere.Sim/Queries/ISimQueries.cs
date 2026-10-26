namespace Evenmere.Sim.Queries;

/// <summary>
/// The read-only view of the simulation: one query interface per system, plus placement and explain queries that span
/// systems. The view and the tools read state only through these, and only between ticks (ADR-0006). The M1 methods of
/// the interfaces were frozen by M1-00 (changed only by ADR); the exploration, economy, happenings and modifiers
/// interfaces stay empty until a later milestone needs them.
/// </summary>
public interface ISimQueries
{
    /// <summary>Gets the map queries.</summary>
    IMapQueries Map { get; }

    /// <summary>Gets the road queries.</summary>
    IRoadsQueries Roads { get; }

    /// <summary>Gets the island storage queries.</summary>
    IStorageQueries Storage { get; }

    /// <summary>Gets the modifier queries.</summary>
    IModifiersQueries Modifiers { get; }

    /// <summary>Gets the construction queries.</summary>
    IConstructionQueries Construction { get; }

    /// <summary>Gets the workforce queries.</summary>
    IWorkforceQueries Workforce { get; }

    /// <summary>Gets the production queries.</summary>
    IProductionQueries Production { get; }

    /// <summary>Gets the logistics queries.</summary>
    ILogisticsQueries Logistics { get; }

    /// <summary>Gets the exploration queries.</summary>
    IExplorationQueries Exploration { get; }

    /// <summary>Gets the needs queries.</summary>
    INeedsQueries Needs { get; }

    /// <summary>Gets the population queries.</summary>
    IPopulationQueries Population { get; }

    /// <summary>Gets the economy queries.</summary>
    IEconomyQueries Economy { get; }

    /// <summary>Gets the research queries.</summary>
    IResearchQueries Research { get; }

    /// <summary>Gets the culture queries.</summary>
    ICultureQueries Culture { get; }

    /// <summary>Gets the progression queries.</summary>
    IProgressionQueries Progression { get; }

    /// <summary>Gets the happenings queries.</summary>
    IHappeningsQueries Happenings { get; }

    /// <summary>Gets the advisor queries.</summary>
    IAdvisorsQueries Advisors { get; }

    /// <summary>Gets the statistics queries.</summary>
    IStatisticsQueries Statistics { get; }

    /// <summary>Gets the placement queries, which answer "can I build this here?" across the map, roads and progression.</summary>
    IPlacementQueries Placement { get; }

    /// <summary>Gets the explain queries, which trace a shortage or a stall back to its cause across systems.</summary>
    IExplainQueries Explain { get; }

    /// <summary>Gets the overlay queries, which produce the per-tile layers the view tints (M2-20, per ADR-0017).</summary>
    IOverlayQueries Overlay { get; }
}
