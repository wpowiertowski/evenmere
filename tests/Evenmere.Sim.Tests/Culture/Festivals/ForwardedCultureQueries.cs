using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>
/// An <see cref="ISimQueries"/> that answers everything from the fake world except culture, which comes from a real
/// <see cref="Systems.Culture.CultureSystem"/>. The population system reads the running festival through this, so the
/// pause test exercises the real culture queries instead of a set fake.
/// </summary>
internal sealed class ForwardedCultureQueries(ISimQueries inner, ICultureQueries culture) : ISimQueries
{
    /// <inheritdoc />
    public IMapQueries Map => inner.Map;

    /// <inheritdoc />
    public IRoadsQueries Roads => inner.Roads;

    /// <inheritdoc />
    public IStorageQueries Storage => inner.Storage;

    /// <inheritdoc />
    public IModifiersQueries Modifiers => inner.Modifiers;

    /// <inheritdoc />
    public IConstructionQueries Construction => inner.Construction;

    /// <inheritdoc />
    public IWorkforceQueries Workforce => inner.Workforce;

    /// <inheritdoc />
    public IProductionQueries Production => inner.Production;

    /// <inheritdoc />
    public ILogisticsQueries Logistics => inner.Logistics;

    /// <inheritdoc />
    public IExplorationQueries Exploration => inner.Exploration;

    /// <inheritdoc />
    public INeedsQueries Needs => inner.Needs;

    /// <inheritdoc />
    public IPopulationQueries Population => inner.Population;

    /// <inheritdoc />
    public IEconomyQueries Economy => inner.Economy;

    /// <inheritdoc />
    public IResearchQueries Research => inner.Research;

    /// <inheritdoc />
    public ICultureQueries Culture => culture;

    /// <inheritdoc />
    public IProgressionQueries Progression => inner.Progression;

    /// <inheritdoc />
    public IHappeningsQueries Happenings => inner.Happenings;

    /// <inheritdoc />
    public IAdvisorsQueries Advisors => inner.Advisors;

    /// <inheritdoc />
    public IStatisticsQueries Statistics => inner.Statistics;

    /// <inheritdoc />
    public IPlacementQueries Placement => inner.Placement;

    /// <inheritdoc />
    public IExplainQueries Explain => inner.Explain;

    /// <inheritdoc />
    public IOverlayQueries Overlay => inner.Overlay;
}
