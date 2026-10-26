using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>A settable stand-in for <see cref="ILogisticsQueries"/>.</summary>
internal sealed class FakeLogisticsQueries : ILogisticsQueries
{
    private readonly List<TransportJobView> _jobs = [];
    private readonly Dictionary<EntityId, int> _freeCarts = [];

    /// <summary>Adds an active job; the jobs are reported in the order they were added, so add them in job-ID order.</summary>
    /// <param name="job">The job.</param>
    /// <returns>This fake.</returns>
    public FakeLogisticsQueries AddJob(TransportJobView job)
    {
        _jobs.Add(job);
        return this;
    }

    /// <summary>Sets how many carts a storehouse has idle.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <param name="carts">The number of free carts.</param>
    /// <returns>This fake.</returns>
    public FakeLogisticsQueries SetFreeCarts(EntityId storehouse, int carts)
    {
        _freeCarts[storehouse] = carts;
        return this;
    }

    /// <inheritdoc />
    public ReadOnlySpan<TransportJobView> ActiveJobs() => CollectionsMarshal.AsSpan(_jobs);

    /// <inheritdoc />
    public int FreeCarts(EntityId storehouse) => _freeCarts.GetValueOrDefault(storehouse);
}

/// <summary>A settable stand-in for <see cref="INeedsQueries"/>. A good with no fulfilment set is fully supplied.</summary>
internal sealed class FakeNeedsQueries : INeedsQueries
{
    private readonly Dictionary<GoodId, Ratio> _fulfilment = [];
    private readonly Dictionary<GoodId, Amount> _demandYesterday = [];
    private readonly Dictionary<EntityId, HouseNeedsInfo> _houses = [];

    /// <summary>Sets a good's fulfilment.</summary>
    /// <param name="good">The good.</param>
    /// <param name="fulfilment">The fulfilment in per-mille.</param>
    /// <returns>This fake.</returns>
    public FakeNeedsQueries SetFulfilment(GoodId good, Ratio fulfilment)
    {
        _fulfilment[good] = fulfilment;
        return this;
    }

    /// <summary>Sets what the houses wanted yesterday.</summary>
    /// <param name="good">The good.</param>
    /// <param name="demand">The demand in milli-units.</param>
    /// <returns>This fake.</returns>
    public FakeNeedsQueries SetDemandYesterday(GoodId good, Amount demand)
    {
        _demandYesterday[good] = demand;
        return this;
    }

    /// <summary>Sets what a house reports.</summary>
    /// <param name="needs">The house's needs; its house ID is the key.</param>
    /// <returns>This fake.</returns>
    public FakeNeedsQueries SetHouseNeeds(HouseNeedsInfo needs)
    {
        _houses[needs.House] = needs;
        return this;
    }

    /// <inheritdoc />
    public Ratio Fulfilment(GoodId good) => _fulfilment.TryGetValue(good, out Ratio fulfilment) ? fulfilment : Ratio.One;

    /// <inheritdoc />
    public Amount DemandYesterday(GoodId good) => _demandYesterday.GetValueOrDefault(good);

    /// <inheritdoc />
    public bool TryGetHouseNeeds(EntityId house, out HouseNeedsInfo needs)
    {
        if (_houses.TryGetValue(house, out HouseNeedsInfo? found))
        {
            needs = found;
            return true;
        }

        needs = null!;
        return false;
    }
}

/// <summary>A settable stand-in for <see cref="IPopulationQueries"/>. <see cref="TotalResidents"/> is the sum over the tiers unless it is set.</summary>
internal sealed class FakePopulationQueries : IPopulationQueries
{
    private readonly Dictionary<TierId, int> _residents = [];
    private readonly Dictionary<EntityId, HouseInfo> _houses = [];
    private int? _total;

    /// <inheritdoc />
    public int TotalResidents
    {
        get => _total ?? _residents.Values.Sum();
        set => _total = value;
    }

    /// <summary>Sets a tier's residents.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="residents">The number of residents.</param>
    /// <returns>This fake.</returns>
    public FakePopulationQueries SetResidents(TierId tier, int residents)
    {
        _residents[tier] = residents;
        return this;
    }

    /// <summary>Sets what a house reports.</summary>
    /// <param name="house">The house's state; its ID is the key.</param>
    /// <returns>This fake.</returns>
    public FakePopulationQueries SetHouse(HouseInfo house)
    {
        _houses[house.Id] = house;
        return this;
    }

    /// <inheritdoc />
    public int Residents(TierId tier) => _residents.GetValueOrDefault(tier);

    /// <inheritdoc />
    public bool TryGetHouse(EntityId house, out HouseInfo info) => _houses.TryGetValue(house, out info);
}

/// <summary>A settable stand-in for <see cref="IConstructionQueries"/>.</summary>
internal sealed class FakeConstructionQueries : IConstructionQueries
{
    private readonly Dictionary<EntityId, SiteInfo> _sites = [];
    private readonly Dictionary<EntityId, BuildingTypeId> _upgradeTargets = [];

    /// <summary>Sets what a site reports.</summary>
    /// <param name="site">The site's progress; its building ID is the key.</param>
    /// <returns>This fake.</returns>
    public FakeConstructionQueries SetSite(SiteInfo site)
    {
        _sites[site.Building] = site;
        return this;
    }

    /// <summary>Sets the type an in-place upgrade site is building toward.</summary>
    /// <param name="building">The building being upgraded.</param>
    /// <param name="target">The type the building becomes.</param>
    /// <returns>This fake.</returns>
    public FakeConstructionQueries SetUpgradeTarget(EntityId building, BuildingTypeId target)
    {
        _upgradeTargets[building] = target;
        return this;
    }

    /// <summary>Clears the upgrade target of a building, as if its upgrade completed or was cancelled.</summary>
    /// <param name="building">The building.</param>
    /// <returns>This fake.</returns>
    public FakeConstructionQueries ClearUpgradeTarget(EntityId building)
    {
        _upgradeTargets.Remove(building);
        return this;
    }

    /// <inheritdoc />
    public bool TryGetSite(EntityId building, out SiteInfo site) => _sites.TryGetValue(building, out site);

    /// <inheritdoc />
    public bool TryGetUpgradeTarget(EntityId building, out BuildingTypeId target) =>
        _upgradeTargets.TryGetValue(building, out target);
}

/// <summary>A settable stand-in for <see cref="IStatisticsQueries"/>.</summary>
internal sealed class FakeStatisticsQueries : IStatisticsQueries
{
    private readonly Dictionary<StatKey, long[]> _series = [];
    private readonly Dictionary<StatKey, long[]> _seasonal = [];
    private long _seasonsSampled;

    /// <summary>Sets a series, oldest sample first.</summary>
    /// <param name="key">The series.</param>
    /// <param name="samples">The samples, oldest first.</param>
    /// <returns>This fake.</returns>
    public FakeStatisticsQueries SetSeries(StatKey key, params long[] samples)
    {
        _series[key] = samples;
        return this;
    }

    /// <summary>Sets a seasonal series, oldest sample first.</summary>
    /// <param name="key">The series.</param>
    /// <param name="seasonsSampled">The total seasons sampled, for axis labels.</param>
    /// <param name="samples">The samples, oldest first.</param>
    /// <returns>This fake.</returns>
    public FakeStatisticsQueries SetSeasonalSeries(StatKey key, long seasonsSampled, params long[] samples)
    {
        _seasonal[key] = samples;
        _seasonsSampled = seasonsSampled;
        return this;
    }

    /// <inheritdoc />
    public int Series(StatKey key, Span<long> destination)
    {
        if (!_series.TryGetValue(key, out long[]? samples))
        {
            return 0;
        }

        // Like the real queries: the newest samples that fit, oldest first.
        int count = Math.Min(destination.Length, samples.Length);
        samples.AsSpan(samples.Length - count).CopyTo(destination);
        return count;
    }

    /// <inheritdoc />
    public (int Written, long Seasons) SeasonalSeries(StatKey key, Span<long> destination)
    {
        if (!_seasonal.TryGetValue(key, out long[]? samples))
        {
            return (0, _seasonsSampled);
        }

        int count = Math.Min(destination.Length, samples.Length);
        samples.AsSpan(samples.Length - count).CopyTo(destination);
        return (count, _seasonsSampled);
    }
}

/// <summary>A settable stand-in for <see cref="IAdvisorsQueries"/>.</summary>
internal sealed class FakeAdvisorsQueries : IAdvisorsQueries
{
    private readonly List<AdviceView> _advice = [];

    /// <summary>Adds an active advice note.</summary>
    /// <param name="advice">The note.</param>
    /// <returns>This fake.</returns>
    public FakeAdvisorsQueries Add(AdviceView advice)
    {
        _advice.Add(advice);
        return this;
    }

    /// <inheritdoc />
    public ReadOnlySpan<AdviceView> Active() => CollectionsMarshal.AsSpan(_advice);
}

/// <summary>
/// A settable stand-in for <see cref="IPlacementQueries"/>. By default every placement is allowed with no warnings; set
/// <see cref="ValidateHandler"/> or <see cref="ValidateRoadHandler"/> to answer otherwise.
/// </summary>
internal sealed class FakePlacementQueries : IPlacementQueries
{
    /// <summary>Gets or sets how a building placement is judged; the default allows everything.</summary>
    public Func<BuildingTypeId, int, int, Rotation, PlacementResult> ValidateHandler { get; set; } =
        static (_, _, _, _) => PlacementResult.Ok;

    /// <summary>Gets or sets how a road placement is judged, given the path as an array; the default allows everything.</summary>
    public Func<TileCoord[], BuildingTypeId, PlacementResult> ValidateRoadHandler { get; set; } =
        static (_, _) => PlacementResult.Ok;

    /// <inheritdoc />
    public PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation) => ValidateHandler(type, x, y, rotation);

    /// <inheritdoc />
    public PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType) => ValidateRoadHandler(path.ToArray(), roadType);
}

/// <summary>
/// A settable stand-in for <see cref="IExplainQueries"/>. An explanation that was not set comes back as a single line with the
/// key <see cref="UnsetKey"/>, so a test sees at once that it asked for something it did not arrange.
/// </summary>
internal sealed class FakeExplainQueries : IExplainQueries
{
    /// <summary>The key of the line returned for an explanation that was not set.</summary>
    public const string UnsetKey = "fake.explanation_not_set";

    private readonly Dictionary<GoodId, Explanation> _goods = [];
    private readonly Dictionary<EntityId, Explanation> _buildings = [];
    private readonly Dictionary<EntityId, Explanation> _houses = [];

    /// <summary>Sets a good's explanation.</summary>
    /// <param name="good">The good.</param>
    /// <param name="explanation">The explanation.</param>
    /// <returns>This fake.</returns>
    public FakeExplainQueries SetGood(GoodId good, Explanation explanation)
    {
        _goods[good] = explanation;
        return this;
    }

    /// <summary>Sets a building's explanation.</summary>
    /// <param name="building">The building.</param>
    /// <param name="explanation">The explanation.</param>
    /// <returns>This fake.</returns>
    public FakeExplainQueries SetBuilding(EntityId building, Explanation explanation)
    {
        _buildings[building] = explanation;
        return this;
    }

    /// <summary>Sets a house's explanation.</summary>
    /// <param name="house">The house.</param>
    /// <param name="explanation">The explanation.</param>
    /// <returns>This fake.</returns>
    public FakeExplainQueries SetHouse(EntityId house, Explanation explanation)
    {
        _houses[house] = explanation;
        return this;
    }

    /// <inheritdoc />
    public Explanation ExplainGood(GoodId good) => _goods.GetValueOrDefault(good) ?? Unset();

    /// <inheritdoc />
    public Explanation ExplainBuilding(EntityId building) => _buildings.GetValueOrDefault(building) ?? Unset();

    /// <inheritdoc />
    public Explanation ExplainHouse(EntityId house) => _houses.GetValueOrDefault(house) ?? Unset();

    private static Explanation Unset() => new(ExplanationLine.Leaf(UnsetKey));
}

/// <summary>A stand-in for <see cref="IModifiersQueries"/>, which has no members in M1.</summary>
internal sealed class FakeModifiersQueries : IModifiersQueries;

/// <summary>A stand-in for <see cref="IExplorationQueries"/>, which has no members in M1.</summary>
internal sealed class FakeExplorationQueries : IExplorationQueries;

/// <summary>A stand-in for <see cref="IEconomyQueries"/>, which has no members in M1.</summary>
internal sealed class FakeEconomyQueries : IEconomyQueries;

/// <summary>A settable stand-in for <see cref="IHappeningsQueries"/>. Nothing is pending or cooling down until a test says so. (M2-12.)</summary>
internal sealed class FakeHappeningsQueries : IHappeningsQueries
{
    private readonly HashSet<HappeningId> _coolingDown = [];

    /// <summary>Gets or sets the pending happening, as <see cref="IHappeningsQueries.PendingHappening"/> reports it.</summary>
    public PendingHappening? Pending { get; set; }

    /// <summary>Sets whether a happening is cooling down.</summary>
    /// <param name="happening">The happening.</param>
    /// <param name="coolingDown">Whether it is on cooldown.</param>
    /// <returns>This fake.</returns>
    public FakeHappeningsQueries SetCooldown(HappeningId happening, bool coolingDown = true)
    {
        if (coolingDown)
        {
            _coolingDown.Add(happening);
        }
        else
        {
            _coolingDown.Remove(happening);
        }

        return this;
    }

    /// <inheritdoc />
    PendingHappening? IHappeningsQueries.PendingHappening() => Pending;

    /// <inheritdoc />
    bool IHappeningsQueries.IsOnCooldown(HappeningId happening) => _coolingDown.Contains(happening);
}
