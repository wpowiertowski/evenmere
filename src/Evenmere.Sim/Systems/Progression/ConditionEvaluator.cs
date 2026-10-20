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
/// <item><description><c>count</c>: the completed buildings in <c>IMapQueries.ActiveBuildings</c>, selected by building type, tag or category from the content.</description></item>
/// <item><description><c>produced</c>, <c>consumed</c>, <c>roadTiles</c>: the milestone counters.</description></item>
/// <item><description><c>stock</c>: the island pool. <c>population</c>: <c>IPopulationQueries</c>.</description></item>
/// <item><description><c>researched</c>, <c>adopted</c>: <c>IsDone</c> on the research and culture queries.</description></item>
/// <item><description><c>season</c>: the clock. <c>era</c>: the current era, which counts once its era has begun.</description></item>
/// <item><description><c>all</c>, <c>any</c>, <c>not</c>: the combinators, which stop at the first deciding sub-condition.</description></item>
/// </list>
/// <para>The remaining kinds evaluate to <see langword="false"/> until M2-09.</para>
/// </remarks>
internal sealed class ConditionEvaluator : IModifierEnvironment
{
    private readonly ProgressionState _state;
    private readonly ContentDb _content;
    private readonly MilestoneCounters _counters;
    private readonly SimContextHolder _holder;

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

    /// <summary>Tests a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see langword="true"/> if it holds. Condition kinds the slice does not implement are <see langword="false"/>.</returns>
    public bool Evaluate(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Test(condition, _holder.Require());
    }

    private bool Test(Condition condition, SimContext ctx)
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
                // The counter counts road tiles of any type. A type-specific count needs a counter per road type.
                // TODO(M2-09): count road tiles per road type.
                return c.Road is null && _counters.Get(CounterKey.RoadTilesLaid) >= c.AtLeast;
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

            // TODO(M2-09): expeditions, festivals held, great works, visitors per day and discoveries need the systems and
            // counters that M2 adds (exploration, culture, economy). Until then they never hold.
            case ExpeditionsCondition:
            case FestivalsHeldCondition:
            case GreatWorksCondition:
            case VisitorsPerDayCondition:
            case DiscoveredCondition:
                return false;
            default:
                throw new ArgumentException($"{condition.GetType().Name} is not a known condition kind.", nameof(condition));
        }
    }

    private bool CountHolds(CountCondition condition, SimContext ctx)
    {
        // TODO(M2-09): buildings counted by the terrain they stand on. Until then a terrain-qualified count never holds.
        if (condition.Terrain is not null)
        {
            return false;
        }

        if (condition.AtLeast <= 0)
        {
            return true;
        }

        int found = 0;
        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            if (Selects(condition.Selector, building.Type) && ++found >= condition.AtLeast)
            {
                return true;
            }
        }

        return false;
    }

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
