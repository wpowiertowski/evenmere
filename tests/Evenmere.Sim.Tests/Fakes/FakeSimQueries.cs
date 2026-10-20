using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// The whole <see cref="ISimQueries"/> facade made of fakes. Each property is the concrete fake, so a test arranges its
/// world through it (<c>queries.Map.AddBuilding(…)</c>, <c>queries.Roads.SetReach(…)</c>) and the system under test reads
/// the same objects through the interface.
/// </summary>
internal sealed class FakeSimQueries(int goodCount = 16) : ISimQueries
{
    /// <summary>Gets the map fake.</summary>
    public FakeMapQueries Map { get; } = new();

    /// <summary>Gets the roads fake.</summary>
    public FakeRoadsQueries Roads { get; } = new();

    /// <summary>Gets the storage fake, which is also the write service (see <see cref="FakeServices.Storage"/>).</summary>
    public FakeStorage Storage { get; } = new(goodCount);

    /// <summary>Gets the modifiers fake.</summary>
    public FakeModifiersQueries Modifiers { get; } = new();

    /// <summary>Gets the construction fake.</summary>
    public FakeConstructionQueries Construction { get; } = new();

    /// <summary>Gets the workforce fake.</summary>
    public FakeWorkforceQueries Workforce { get; } = new();

    /// <summary>Gets the production fake.</summary>
    public FakeProductionQueries Production { get; } = new();

    /// <summary>Gets the logistics fake.</summary>
    public FakeLogisticsQueries Logistics { get; } = new();

    /// <summary>Gets the exploration fake.</summary>
    public FakeExplorationQueries Exploration { get; } = new();

    /// <summary>Gets the needs fake.</summary>
    public FakeNeedsQueries Needs { get; } = new();

    /// <summary>Gets the population fake.</summary>
    public FakePopulationQueries Population { get; } = new();

    /// <summary>Gets the economy fake.</summary>
    public FakeEconomyQueries Economy { get; } = new();

    /// <summary>Gets the research fake.</summary>
    public FakeResearchQueries Research { get; } = new();

    /// <summary>Gets the culture fake.</summary>
    public FakeCultureQueries Culture { get; } = new();

    /// <summary>Gets the progression fake.</summary>
    public FakeProgressionQueries Progression { get; } = new();

    /// <summary>Gets the happenings fake.</summary>
    public FakeHappeningsQueries Happenings { get; } = new();

    /// <summary>Gets the advisors fake.</summary>
    public FakeAdvisorsQueries Advisors { get; } = new();

    /// <summary>Gets the statistics fake.</summary>
    public FakeStatisticsQueries Statistics { get; } = new();

    /// <summary>Gets the placement fake.</summary>
    public FakePlacementQueries Placement { get; } = new();

    /// <summary>Gets the explain fake.</summary>
    public FakeExplainQueries Explain { get; } = new();

    IMapQueries ISimQueries.Map => Map;

    IRoadsQueries ISimQueries.Roads => Roads;

    IStorageQueries ISimQueries.Storage => Storage;

    IModifiersQueries ISimQueries.Modifiers => Modifiers;

    IConstructionQueries ISimQueries.Construction => Construction;

    IWorkforceQueries ISimQueries.Workforce => Workforce;

    IProductionQueries ISimQueries.Production => Production;

    ILogisticsQueries ISimQueries.Logistics => Logistics;

    IExplorationQueries ISimQueries.Exploration => Exploration;

    INeedsQueries ISimQueries.Needs => Needs;

    IPopulationQueries ISimQueries.Population => Population;

    IEconomyQueries ISimQueries.Economy => Economy;

    IResearchQueries ISimQueries.Research => Research;

    ICultureQueries ISimQueries.Culture => Culture;

    IProgressionQueries ISimQueries.Progression => Progression;

    IHappeningsQueries ISimQueries.Happenings => Happenings;

    IAdvisorsQueries ISimQueries.Advisors => Advisors;

    IStatisticsQueries ISimQueries.Statistics => Statistics;

    IPlacementQueries ISimQueries.Placement => Placement;

    IExplainQueries ISimQueries.Explain => Explain;
}
