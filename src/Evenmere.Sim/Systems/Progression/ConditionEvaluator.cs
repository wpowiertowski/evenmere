using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Modifiers;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// Tests a content <see cref="Condition"/> against the world as it is now. The inspirations, the modifier <c>when</c>
/// clauses and (later) the advisors and happenings all use this one evaluator.
/// </summary>
/// <remarks>
/// <para>Where each kind reads its answer from:</para>
/// <list type="bullet">
/// <item><description><c>count</c>: the completed buildings in <c>IMapQueries.ActiveBuildings</c>, selected by building type, tag or category from the content, and optionally only those standing on a terrain (the origin tile's terrain).</description></item>
/// <item><description><c>produced</c>, <c>consumed</c>, <c>roadTiles</c> (without a road type), <c>expeditions</c>, <c>festivalsHeld</c>, <c>greatWorks</c> and <c>discovered</c>: the milestone counters. The exploration, festival and great-work systems add to theirs; until they exist the counters read zero.</description></item>
/// <item><description><c>roadTiles</c> with a road type: the live per-type counts in <c>RoadsState</c>, read through <c>IRoadsQueries.RoadTiles</c>.</description></item>
/// <item><description><c>stock</c>: the island pool. <c>population</c>: <c>IPopulationQueries</c>. <c>visitorsPerDay</c>: the visitors gauge in the progression state, set by the future visitors system.</description></item>
/// <item><description><c>researched</c>, <c>adopted</c>: <c>IsDone</c> on the research and culture queries.</description></item>
/// <item><description><c>season</c>: the clock. <c>era</c>: the current era, which counts once its era has begun.</description></item>
/// <item><description><c>all</c>, <c>any</c>, <c>not</c>: the combinators, which stop at the first deciding sub-condition.</description></item>
/// </list>
/// <para>
/// Change-driven evaluation: answers that only read counters, the season and the era are memoised per condition and reused
/// while no counter changed and neither the season nor the era did (<see cref="MilestoneCounters.Version"/> is the dirty
/// signal, so there is no dirty set to reset). The cache compares conditions by reference, because the same content
/// instances are re-evaluated every day and every query; the season is memoised per tick to skip the clock divisions.
/// Everything else — buildings, the pool, residents, unlocks, the live road counts and the visitors gauge — is evaluated
/// fresh on every call. The cache is never iterated, only looked up, so its bucket order cannot reach the simulation.
/// </para>
/// </remarks>
internal sealed class ConditionEvaluator : IModifierEnvironment
{
    private readonly ProgressionState _state;
    private readonly ContentDb _content;
    private readonly MilestoneCounters _counters;
    private readonly SimContextHolder _holder;

    // The context, cached on first use: the holder is bound once when the simulation context is created, and systems
    // (with their evaluators) are built fresh for every world, so the first context seen is the only one there is.
    // Skipping the re-lookup keeps the per-evaluation path short, which the daily pass runs hundreds of times.
    private SimContext? _context;

    // Memoised answers for conditions that only read counters, the season and the era. The table compares conditions
    // by reference: content records are loaded once and re-evaluated by the same instances every day (inspirations) or
    // every query (modifier whens), so identity hashing is far cheaper than hashing whole condition trees. Two lookups
    // of equal but distinct instances recompute instead of sharing, which is only ever slower, never wrong. Entries
    // are only ever looked up, never iterated, so the table's bucket order is invisible to the simulation. An entry is
    // fresh while no counter changed and neither the season nor the era did.
    private readonly Dictionary<Condition, CachedAnswer> _cache = new(ReferenceEqualityComparer.Instance);

    // The season of the last tick the evaluator saw: the clock needs two divisions to answer, so within a tick every
    // evaluation reuses the memoized one.
    private long _seasonTick = -1;
    private Season _season;

    /// <summary>Initializes a new instance of the <see cref="ConditionEvaluator"/> class.</summary>
    /// <param name="state">The progression state, for the current era.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="counters">The milestone counters.</param>
    /// <param name="holder">Holds the simulation context, which supplies the clock and the other systems' queries.</param>
    public ConditionEvaluator(ProgressionState state, ContentDb content, MilestoneCounters counters, SimContextHolder holder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(holder);

        _state = state;
        _content = content;
        _counters = counters;
        _holder = holder;
    }

    /// <summary>Gets the season of the current tick.</summary>
    public Season Season => _holder.Require().Clock.Season;

    /// <summary>Gets how many memoised answers the evaluator holds. Tests use it to see that repeated evaluations hit the cache.</summary>
    internal int CachedAnswerCount => _cache.Count;

    /// <summary>Tests a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see langword="true"/> if it holds.</returns>
    public bool Evaluate(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Test(condition, _context ??= _holder.Require());
    }

    /// <summary>
    /// Lists the milestone counters a condition reads, in the order they first occur walking the tree. Conditions that
    /// read no counter (buildings, the pool, residents, unlocks, the season, the era) map to the empty set: they are
    /// evaluated fresh on every call rather than through the memoised path.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The counter keys it depends on.</returns>
    internal CounterKey[] Dependencies(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var found = new List<CounterKey>();
        CollectDependencies(condition, found);
        return [.. found];
    }

    private void CollectDependencies(Condition condition, List<CounterKey> found)
    {
        switch (condition)
        {
            case ProducedCondition c:
                AddDependency(found, CounterKey.Produced(c.Good));
                break;
            case ConsumedCondition c:
                AddDependency(found, CounterKey.Consumed(c.Good));
                break;
            case RoadTilesCondition c:
                if (c.Road is null)
                {
                    AddDependency(found, CounterKey.RoadTilesLaid);
                }

                break;
            case ExpeditionsCondition:
                AddDependency(found, CounterKey.Expeditions);
                break;
            case FestivalsHeldCondition:
                AddDependency(found, CounterKey.FestivalsHeld);
                break;
            case GreatWorksCondition c:
                CollectGreatWorksDependencies(c, found);
                break;
            case DiscoveredCondition c:
                if (c.Kind is { } kind)
                {
                    AddDependency(found, CounterKey.Discovered(kind));
                }
                else if (c.Region is { } region)
                {
                    AddDependency(found, CounterKey.RegionsRevealed(region));
                }

                break;
            case AllCondition c:
                foreach (Condition child in c.Conditions)
                {
                    CollectDependencies(child, found);
                }

                break;
            case AnyCondition c:
                foreach (Condition child in c.Conditions)
                {
                    CollectDependencies(child, found);
                }

                break;
            case NotCondition c:
                CollectDependencies(c.Condition, found);
                break;
            default:
                break;
        }
    }

    private void CollectGreatWorksDependencies(GreatWorksCondition condition, List<CounterKey> found)
    {
        if (condition.Type is { } type)
        {
            AddDependency(found, condition.Displayed ? CounterKey.GreatWorksDisplayed(type) : CounterKey.GreatWorksCreated(type));
            return;
        }

        for (int i = 0; i < _content.GreatWorks.Count; i++)
        {
            var id = GreatWorkTypeId.From(checked((ushort)i));
            AddDependency(found, condition.Displayed ? CounterKey.GreatWorksDisplayed(id) : CounterKey.GreatWorksCreated(id));
        }
    }

    private static void AddDependency(List<CounterKey> found, CounterKey key)
    {
        if (!found.Contains(key))
        {
            found.Add(key);
        }
    }

    // The memoised path: a hit returns the stored answer, a miss evaluates and stores it when the condition only reads
    // counters, the season and the era. Anything else is evaluated fresh on every call. Sub-conditions recurse through
    // here, so the pure parts of a mixed tree still hit the cache. Inlining is hinted because every evaluation, cached
    // or not, passes through here on the way to the single lookup below.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Test(Condition condition, SimContext ctx)
    {
        if (ctx.Clock.Tick != _seasonTick)
        {
            _seasonTick = ctx.Clock.Tick;
            _season = ctx.Clock.Season;
        }

        if (_cache.TryGetValue(condition, out CachedAnswer cached)
            && cached.Version == _counters.Version
            && cached.Season == _season
            && cached.Era == _state.Era)
        {
            return cached.Result;
        }

        bool result = TestCore(condition, ctx);
        if (IsCacheable(condition))
        {
            _cache[condition] = new CachedAnswer(_counters.Version, _season, _state.Era, result);
        }

        return result;
    }

    // A condition is cacheable when its whole tree reads only counters, the season and the era. Typed road counts read
    // the live road layer, and the visitors gauge is written outside the counters, so neither is cached.
    private static bool IsCacheable(Condition condition) => condition switch
    {
        ProducedCondition => true,
        ConsumedCondition => true,
        RoadTilesCondition c => c.Road is null,
        ExpeditionsCondition => true,
        FestivalsHeldCondition => true,
        GreatWorksCondition => true,
        DiscoveredCondition => true,
        SeasonCondition => true,
        EraCondition => true,
        AllCondition c => AllCacheable(c.Conditions),
        AnyCondition c => AllCacheable(c.Conditions),
        NotCondition c => IsCacheable(c.Condition),
        _ => false,
    };

    private static bool AllCacheable(ImmutableArray<Condition> conditions)
    {
        foreach (Condition child in conditions)
        {
            if (!IsCacheable(child))
            {
                return false;
            }
        }

        return true;
    }

    private bool TestCore(Condition condition, SimContext ctx)
    {
        switch (condition)
        {
            case CountCondition c:
                return CountHolds(c, ctx);
            case ProducedCondition c:
                return _counters.Get(CounterKey.Produced(c.Good)) >= c.AtLeast.Milli;
            case ConsumedCondition c:
                return _counters.Get(CounterKey.Consumed(c.Good)) >= c.AtLeast.Milli;
            case StockCondition c:
                return ctx.Queries.Storage.Stock(c.Good) >= c.AtLeast;
            case PopulationCondition c:
                return (c.Tier is { } tier ? ctx.Queries.Population.Residents(tier) : ctx.Queries.Population.TotalResidents) >= c.AtLeast;
            case RoadTilesCondition c:
                return c.Road is { } road ? ctx.Queries.Roads.RoadTiles(road) >= c.AtLeast : _counters.Get(CounterKey.RoadTilesLaid) >= c.AtLeast;
            case ResearchedCondition c:
                return ctx.Queries.Research.IsDone(c.Tech);
            case AdoptedCondition c:
                return ctx.Queries.Culture.IsDone(c.Civic);
            case SeasonCondition c:
                return ctx.Clock.Season == c.Season;
            case EraCondition c:
                // An era "has begun" once the island is in it or in a later one; eras are compared by order, not by ID.
                return _content.Eras.Get(EraId.From(checked((ushort)_state.Era))).Order >= _content.Eras.Get(c.Era).Order;
            case AllCondition c:
                foreach (Condition child in c.Conditions)
                {
                    if (!Test(child, ctx))
                    {
                        return false;
                    }
                }

                return true;
            case AnyCondition c:
                foreach (Condition child in c.Conditions)
                {
                    if (Test(child, ctx))
                    {
                        return true;
                    }
                }

                return false;
            case NotCondition c:
                return !Test(c.Condition, ctx);

            // Expeditions, festivals, great works, visitors and discoveries read the counters and gauges their future
            // systems write; until those systems exist the counters read zero and the gauge reads zero.
            case ExpeditionsCondition c:
                return _counters.Get(CounterKey.Expeditions) >= c.AtLeast;
            case FestivalsHeldCondition c:
                return _counters.Get(CounterKey.FestivalsHeld) >= c.AtLeast;
            case GreatWorksCondition c:
                return GreatWorksHold(c);
            case VisitorsPerDayCondition c:
                return _state.VisitorsPerDay >= c.AtLeast;
            case DiscoveredCondition c:
                return DiscoveredHolds(c);
            default:
                throw new ArgumentException($"{condition.GetType().Name} is not a known condition kind.", nameof(condition));
        }
    }

    private bool CountHolds(CountCondition condition, SimContext ctx)
    {
        if (condition.AtLeast <= 0)
        {
            return true;
        }

        int found = 0;
        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            if (!Selects(condition.Selector, building.Type))
            {
                continue;
            }

            if (condition.Terrain is { } terrain && !StandsOn(ctx, building, terrain))
            {
                continue;
            }

            if (++found >= condition.AtLeast)
            {
                return true;
            }
        }

        return false;
    }

    // A building counts for the terrain of its origin (top-left) tile: footprints that straddle a boundary still have
    // exactly one terrain, so the count is stable whichever way the map is walked.
    private static bool StandsOn(SimContext ctx, in BuildingInfo building, TerrainId terrain)
    {
        if (!building.Origin.IsInside(ctx.Queries.Map.Width, ctx.Queries.Map.Height))
        {
            return false;
        }

        return ctx.Queries.Map.GetTile(building.Origin.X, building.Origin.Y).Terrain == terrain;
    }

    // Without a type every created work counts, whatever is on display; the loop stops at the first type that closes
    // the gap, so the common single-type case reads one counter.
    private bool GreatWorksHold(GreatWorksCondition condition)
    {
        if (condition.Type is { } type)
        {
            CounterKey key = condition.Displayed ? CounterKey.GreatWorksDisplayed(type) : CounterKey.GreatWorksCreated(type);
            return _counters.Get(key) >= condition.AtLeast;
        }

        long total = 0;
        for (int i = 0; i < _content.GreatWorks.Count; i++)
        {
            var id = GreatWorkTypeId.From(checked((ushort)i));
            total += _counters.Get(condition.Displayed ? CounterKey.GreatWorksDisplayed(id) : CounterKey.GreatWorksCreated(id));
            if (total >= condition.AtLeast)
            {
                return true;
            }
        }

        return total >= condition.AtLeast;
    }

    // Exactly one of kind and region is set; the loader enforces it. A hand-built condition with neither counts as met
    // only below a threshold of one, like an empty count.
    private bool DiscoveredHolds(DiscoveredCondition condition)
    {
        if (condition.Kind is { } kind)
        {
            return _counters.Get(CounterKey.Discovered(kind)) >= condition.AtLeast;
        }

        if (condition.Region is { } region)
        {
            return _counters.Get(CounterKey.RegionsRevealed(region)) >= condition.AtLeast;
        }

        return condition.AtLeast <= 0;
    }

    // A memoised answer with the stamps that prove it is fresh: the counters' version, the season and the era.
    private readonly record struct CachedAnswer(long Version, Season Season, int Era, bool Result);

    private bool Selects(BuildingSelector? selector, BuildingTypeId type)
    {
        switch (selector)
        {
            case null:
                return true;
            case BuildingTypeSelector byType:
                return byType.Building == type;
            case BuildingTagSelector byTag:
                foreach (string tag in _content.Buildings.Get(type).Tags)
                {
                    if (string.Equals(tag, byTag.Tag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            case BuildingCategorySelector byCategory:
                return _content.Buildings.Get(type).Category == byCategory.Category;
            default:
                throw new ArgumentException($"{selector.GetType().Name} is not a known building selector.", nameof(selector));
        }
    }
}
