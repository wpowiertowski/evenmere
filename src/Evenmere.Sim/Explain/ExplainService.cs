using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Explain;

/// <summary>
/// The explain service: structured answers that trace any number to its causes. Every answer is a tree of locale keys
/// and typed values; the simulation never produces English text. Producer groups and causes are ordered by ID, so the
/// same world always gives the same tree.
/// </summary>
/// <param name="content">The loaded content, which names recipes, tiers and rules.</param>
/// <param name="queries">The read-only queries, late-bound because the roster builds them after the systems.</param>
/// <param name="clock">The current clock, late-bound because it advances every tick.</param>
internal sealed class ExplainService(ContentDb content, Func<ISimQueries> queries, Func<GameClock> clock) : IExplainQueries
{
    private const int MaxChainDepth = 3;

    private readonly ContentDb _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly Func<ISimQueries> _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    private readonly Func<GameClock> _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public Explanation ExplainGood(GoodId good)
    {
        if (good.Value >= _content.Goods.Count)
        {
            return new Explanation(ExplanationLine.Leaf("explain.good.unknown", ExplainArg.Of(good)));
        }

        var path = new List<GoodId>(4);
        return new Explanation(BuildGood(good, 0, path));
    }

    /// <inheritdoc />
    public Explanation ExplainBuilding(EntityId building)
    {
        ISimQueries q = _queries();
        if (!q.Map.TryGetBuilding(building, out BuildingInfo info))
        {
            return new Explanation(ExplanationLine.Leaf("explain.building.unknown", ExplainArg.Of(building)));
        }

        if (info.State == BuildingState.Site)
        {
            return new Explanation(BuildSite(building, in info));
        }

        if (q.Production.TryGet(building, out ProductionInfo production))
        {
            return new Explanation(BuildProducer(building, in info, in production));
        }

        return new Explanation(BuildPlain(building, in info));
    }

    /// <inheritdoc />
    public Explanation ExplainHouse(EntityId house)
    {
        ISimQueries q = _queries();
        if (!q.Population.TryGetHouse(house, out HouseInfo info)
            || !q.Needs.TryGetHouseNeeds(house, out HouseNeedsInfo needs))
        {
            return new Explanation(ExplanationLine.Leaf("explain.house.unknown", ExplainArg.Of(house)));
        }

        var children = new List<ExplanationLine>(needs.Needs.Length + 3);

        // Each need with its status and reason, basic ones first in tier order (the queries already order them).
        foreach (NeedStatus need in needs.Needs)
        {
            children.Add(NeedLine(in need));
        }

        children.Add(OccupancyLine(in info, in needs));
        children.Add(AdvanceLine(house, in info));
        children.Add(DeclineLine(in info, in needs));

        ExplanationLine root = new(
            "explain.house.status",
            [ExplainArg.Of(info.Tier), ExplainArg.Of(info.Residents), ExplainArg.Of(info.TargetResidents), ExplainArg.Of(info.Capacity)],
            [SubjectLink.ForBuilding(house), SubjectLink.ForTier(info.Tier)],
            [.. children]);
        return new Explanation(root);
    }

    private ExplanationLine BuildGood(GoodId good, int depth, List<GoodId> path)
    {
        ISimQueries q = _queries();
        Amount produced = q.Production.ProducedYesterday(good);
        Amount demand = q.Needs.DemandYesterday(good);
        Amount stock = q.Storage.Stock(good);

        Ratio fulfil = q.Needs.Fulfilment(good);

        // Every producer of the good, in ascending building-ID order, grouped by type in ascending type order.
        var producers = new List<(BuildingInfo Building, ProductionInfo Info)>();
        foreach (BuildingInfo building in q.Map.ActiveBuildings)
        {
            if (q.Production.TryGet(building.Id, out ProductionInfo info) && Produces(info.Recipe, good))
            {
                producers.Add((building, info));
            }
        }

        var groups = new SortedDictionary<int, ProducerGroup>();
        foreach ((BuildingInfo building, ProductionInfo info) in producers)
        {
            int type = building.Type.Value;
            if (!groups.TryGetValue(type, out ProducerGroup? group))
            {
                group = new ProducerGroup(building.Type);
                groups[type] = group;
            }

            group.Add(building.Id, in info);
        }

        var children = new List<ExplanationLine>(groups.Count + 2);
        foreach (ProducerGroup group in groups.Values)
        {
            children.Add(group.Line());
        }

        // Chained causes, up to MaxChainDepth levels deep. The path holds the goods already expanded above.
        if (depth < MaxChainDepth && !path.Contains(good))
        {
            path.Add(good);

            GoodId? missing = DominantMissingInput(producers);
            if (missing.HasValue)
            {
                if (path.Contains(missing.Value))
                {
                    children.Add(new ExplanationLine(
                        "explain.good.cause.missing_input",
                        [ExplainArg.Of(missing.Value)],
                        [SubjectLink.ForGood(missing.Value)],
                        []));
                }
                else
                {
                    ExplanationLine nested = BuildGood(missing.Value, depth + 1, path);
                    children.Add(new ExplanationLine(
                        "explain.good.cause.missing_input",
                        [ExplainArg.Of(missing.Value)],
                        [SubjectLink.ForGood(missing.Value)],
                        [nested]));
                }
            }

            foreach (ExplanationLine workforce in WorkforceCauses(producers))
            {
                children.Add(workforce);
            }

            path.RemoveAt(path.Count - 1);
        }

        return new ExplanationLine(
            "explain.good.supply",
            [ExplainArg.Of(good), ExplainArg.Of(produced), ExplainArg.Of(demand), ExplainArg.Of(stock), ExplainArg.Of(fulfil)],
            [SubjectLink.ForGood(good)],
            [.. children]);
    }

    private bool Produces(RecipeId recipe, GoodId good)
    {
        if (recipe.Value >= _content.Recipes.Count)
        {
            return false;
        }

        foreach (GoodAmount output in _content.Get(recipe).Outputs)
        {
            if (output.Good == good)
            {
                return true;
            }
        }

        return false;
    }

    private GoodId? DominantMissingInput(List<(BuildingInfo Building, ProductionInfo Info)> producers)
    {
        var tally = new SortedDictionary<int, int>();
        foreach ((BuildingInfo _, ProductionInfo info) in producers)
        {
            if (info.Stall != StallReason.MissingInput)
            {
                continue;
            }

            if (info.Recipe.Value >= _content.Recipes.Count)
            {
                continue;
            }

            RecipeDef recipe = _content.Get(info.Recipe);
            for (int i = 0; i < recipe.Inputs.Length && i < info.InputBuffer.Length; i++)
            {
                if (info.InputBuffer[i].Amount < recipe.Inputs[i].Amount)
                {
                    int subject = recipe.Inputs[i].Good.Value;
                    tally[subject] = tally.GetValueOrDefault(subject) + 1;
                }
            }
        }

        int best = -1;
        int bestCount = 0;
        foreach ((int subject, int count) in tally)
        {
            if (count > bestCount)
            {
                best = subject;
                bestCount = count;
            }
        }

        return best < 0 ? null : GoodId.From(checked((ushort)best));
    }

    private List<ExplanationLine> WorkforceCauses(List<(BuildingInfo Building, ProductionInfo Info)> producers)
    {
        ISimQueries q = _queries();
        var tiers = new SortedSet<int>();
        foreach ((BuildingInfo building, ProductionInfo info) in producers)
        {
            if (info.Stall != StallReason.NoWorkforce)
            {
                continue;
            }

            if (building.Type.Value >= _content.Buildings.Count)
            {
                continue;
            }

            WorkforceSpec? workforce = _content.Get(building.Type).Workforce;
            if (workforce is not null)
            {
                tiers.Add(workforce.Tier.Value);
            }
        }

        var causes = new List<ExplanationLine>(tiers.Count);
        foreach (int subject in tiers)
        {
            var tier = TierId.From(checked((ushort)subject));
            int supply = q.Workforce.Supply(tier);
            int demand = q.Workforce.Demand(tier);
            causes.Add(new ExplanationLine(
                "explain.good.cause.workforce",
                [ExplainArg.Of(tier), ExplainArg.Of(supply), ExplainArg.Of(demand)],
                [SubjectLink.ForTier(tier)],
                []));
        }

        return causes;
    }

    private ExplanationLine BuildProducer(EntityId building, in BuildingInfo info, in ProductionInfo production)
    {
        ISimQueries q = _queries();
        var children = new List<ExplanationLine>(8)
        {
            ProductivityLine(building, in info, in production),
        };

        if (production.Stall != StallReason.None)
        {
            children.Add(new ExplanationLine(
                "explain.building.stall",
                [ExplainArg.Of(production.Stall)],
                [],
                [ExplanationLine.Leaf(FixKey(production.Stall))]));
        }

        foreach (GoodAmount held in production.InputBuffer)
        {
            children.Add(new ExplanationLine(
                "explain.building.input",
                [ExplainArg.Of(held.Good), ExplainArg.Of(held.Amount)],
                [SubjectLink.ForGood(held.Good)],
                []));
        }

        foreach (GoodAmount held in production.OutputBuffer)
        {
            children.Add(new ExplanationLine(
                "explain.building.output",
                [ExplainArg.Of(held.Good), ExplainArg.Of(held.Amount)],
                [SubjectLink.ForGood(held.Good)],
                []));
        }

        children.Add(ReachLine(building));
        children.Add(RoadLine(building));

        return new ExplanationLine(
            "explain.building.status",
            [ExplainArg.Of(info.Type), ExplainArg.Of(production.Stall), ExplainArg.Of(production.Productivity)],
            [SubjectLink.ForBuilding(building), SubjectLink.ForBuildingType(info.Type)],
            [.. children]);
    }

    private ExplanationLine ProductivityLine(EntityId building, in BuildingInfo info, in ProductionInfo production)
    {
        ISimQueries q = _queries();
        var context = ModifierContext.ForBuilding(building, info.Type);
        var contributions = new List<ModifierContribution>();
        q.Progression.ListModifiers(Stat.Productivity, in context, contributions);

        int activeSum = 0;
        var parts = new List<ExplanationLine>(contributions.Count + 5);
        foreach (ModifierContribution contribution in contributions)
        {
            if (contribution.IsActive)
            {
                activeSum += contribution.Percent;
            }

            parts.Add(new ExplanationLine(
                contribution.IsActive ? "explain.building.modifier" : "explain.building.modifier.inactive",
                [SourceArg(contribution.Source), ExplainArg.Of(contribution.Percent)],
                [],
                []));
        }

        int siteBonus = production.BonusPercent - activeSum;
        if (siteBonus != 0)
        {
            // A zero site bonus changes nothing, so the line would only add noise.
            parts.Add(new ExplanationLine("explain.building.site_bonus", [ExplainArg.Of(siteBonus)], [], []));
        }

        // Nothing in M1 carries a season curve, so the season factor is always neutral and the
        // line is omitted; a building with a real seasonal curve still shows it.
        if (SeasonFactor(info.Type) is { } season)
        {
            GameClock now = _clock();
            parts.Add(new ExplanationLine(
                "explain.building.season",
                [ExplainArg.Of(now.Season), ExplainArg.Of(season)],
                [],
                []));
        }

        // Buildings without an area-yield spec (bakeries and their kin) always run at full land
        // yield, so the line would only add noise; gatherers and farms still show it.
        bool hasAreaYield = HasAreaYield(info.Type);
        var factors = new List<ExplanationLine>(hasAreaYield ? 3 : 2)
        {
            new("explain.building.workforce", [ExplainArg.Of(production.WorkforceFill)], [], []),
            new(
                production.InputsReady ? "explain.building.inputs.ready" : "explain.building.inputs.missing",
                [ExplainArg.Of(production.Recipe)],
                [],
                []),
        };
        if (hasAreaYield)
        {
            factors.Add(new("explain.building.area_yield", [ExplainArg.Of(production.AreaYield)], [], []));
        }

        factors.AddRange(parts);

        return new ExplanationLine(
            "explain.building.productivity",
            [ExplainArg.Of(production.Productivity)],
            [],
            [.. factors]);
    }

    /// <summary>Tests whether a building type measures yield from the land around it.</summary>
    /// <param name="type">The building type.</param>
    /// <returns><see langword="true"/> when the content gives the type an area-yield spec.</returns>
    private bool HasAreaYield(BuildingTypeId type) =>
        type.Value < _content.Buildings.Count && _content.Get(type).Placement.AreaYield is not null;

    /// <summary>Finds the season factor the content gives a building, if it is not neutral.</summary>
    /// <param name="type">The building type.</param>
    /// <returns>The factor from the first season curve whose tag the building carries, or <see langword="null"/> when none applies or it is neutral (1000 per-mille).</returns>
    private Ratio? SeasonFactor(BuildingTypeId type)
    {
        if (type.Value >= _content.Buildings.Count || _content.Rules.Seasons.IsEmpty)
        {
            return null;
        }

        ImmutableArray<string> tags = _content.Get(type).Tags;
        if (tags.IsEmpty)
        {
            return null;
        }

        GameClock now = _clock();
        foreach (SeasonCurve curve in _content.Rules.Seasons)
        {
            foreach (string tag in tags)
            {
                if (string.Equals(tag, curve.Tag, StringComparison.Ordinal))
                {
                    Ratio factor = curve.Of(now.Season);
                    return factor == Ratio.One ? null : factor;
                }
            }
        }

        return null;
    }

    private ExplanationLine BuildPlain(EntityId building, in BuildingInfo info)
    {
        var children = new List<ExplanationLine>(2)
        {
            ReachLine(building),
            RoadLine(building),
        };

        return new ExplanationLine(
            "explain.building.plain",
            [ExplainArg.Of(info.Type)],
            [SubjectLink.ForBuilding(building), SubjectLink.ForBuildingType(info.Type)],
            [.. children]);
    }

    private ExplanationLine BuildSite(EntityId building, in BuildingInfo info)
    {
        ISimQueries q = _queries();
        var children = new List<ExplanationLine>(3);
        int stage = info.Stage;
        const int steps = 10;
        Ratio progress = Ratio.Zero;
        StallReason stall = StallReason.None;
        GoodId? waitingFor = null;

        if (q.Construction.TryGetSite(building, out SiteInfo site))
        {
            stage = site.Stage;
            progress = site.Progress;
            stall = site.Stall;
            waitingFor = site.WaitingFor;
        }

        if (waitingFor.HasValue)
        {
            children.Add(new ExplanationLine(
                "explain.building.waiting_for",
                [ExplainArg.Of(waitingFor.Value)],
                [SubjectLink.ForGood(waitingFor.Value)],
                []));
        }

        if (stall != StallReason.None)
        {
            children.Add(new ExplanationLine(
                "explain.building.stall",
                [ExplainArg.Of(stall)],
                [],
                [ExplanationLine.Leaf(FixKey(stall))]));
        }

        children.Add(ReachLine(building));
        children.Add(RoadLine(building));

        // Steps is a plain number: every M1 site builds in 10 equal material steps.
        return new ExplanationLine(
            "explain.building.site",
            [ExplainArg.Of(info.Type), ExplainArg.Of(stage), ExplainArg.Of(steps), ExplainArg.Of(progress), ExplainArg.Of(stall)],
            [SubjectLink.ForBuilding(building), SubjectLink.ForBuildingType(info.Type)],
            [.. children]);
    }

    private ExplanationLine ReachLine(EntityId building)
    {
        ISimQueries q = _queries();
        if (q.Roads.TryNearestStorehouse(building, out EntityId storehouse, out int cost))
        {
            return new ExplanationLine(
                "explain.building.reach",
                [ExplainArg.Of(storehouse), ExplainArg.Of(cost)],
                [SubjectLink.ForBuilding(storehouse)],
                []);
        }

        return new ExplanationLine("explain.building.no_reach", [], [], []);
    }

    private ExplanationLine RoadLine(EntityId building)
    {
        ISimQueries q = _queries();
        return q.Roads.HasRoadAccess(building)
            ? new ExplanationLine("explain.building.road", [], [], [])
            : new ExplanationLine("explain.building.no_road", [], [], []);
    }

    private static ExplanationLine NeedLine(in NeedStatus need)
    {
        if (need.Good.HasValue)
        {
            GoodId good = need.Good.Value;
            if (need.Met)
            {
                return new ExplanationLine(
                    "explain.house.need.good.met",
                    [ExplainArg.Of(good), ExplainArg.Of(need.Fulfilment)],
                    [SubjectLink.ForGood(good)],
                    []);
            }

            return need.Reason == NeedUnmetReason.NoMarketCoverage
                ? new ExplanationLine(
                    "explain.house.need.good.no_market",
                    [ExplainArg.Of(good), ExplainArg.Of(need.Fulfilment)],
                    [SubjectLink.ForGood(good)],
                    [])
                : new ExplanationLine(
                    "explain.house.need.good.shortage",
                    [ExplainArg.Of(good), ExplainArg.Of(need.Fulfilment)],
                    [SubjectLink.ForGood(good)],
                    []);
        }

        ServiceKind service = need.Service ?? ServiceKind.Market;
        return need.Met
            ? new ExplanationLine("explain.house.need.service.met", [ExplainArg.Of(service)], [], [])
            : new ExplanationLine("explain.house.need.service.missing", [ExplainArg.Of(service)], [], []);
    }

    private static ExplanationLine OccupancyLine(in HouseInfo info, in HouseNeedsInfo needs)
    {
        return new ExplanationLine(
            "explain.house.occupancy",
            [ExplainArg.Of(info.Residents), ExplainArg.Of(info.TargetResidents), ExplainArg.Of(info.Capacity), ExplainArg.Of(needs.BasicMet), ExplainArg.Of(needs.BasicTotal)],
            [],
            []);
    }

    private ExplanationLine AdvanceLine(EntityId house, in HouseInfo info)
    {
        int required = _content.Rules.AdvanceStreakDays;
        var blocker = new ExplanationLine(BlockerKey(info.AdvanceBlocker, info.Tier), BlockerArgs(house, in info), [], BlockerChildren(in info));
        return new ExplanationLine(
            "explain.house.advance",
            [ExplainArg.Of(info.AdvanceStreakDays), ExplainArg.Of(required)],
            [],
            [blocker]);
    }

    private string BlockerKey(AdvanceBlocker blocker, TierId tier)
    {
        if (blocker == AdvanceBlocker.NextTierLocked && NextTier(tier) is { } next)
        {
            return "explain.house.blocker.locked";
        }

        return blocker switch
        {
            AdvanceBlocker.None => "explain.house.blocker.none",
            AdvanceBlocker.NeedsUnmet => "explain.house.blocker.needs",
            AdvanceBlocker.NextTierLocked => "explain.house.blocker.locked",
            AdvanceBlocker.NoNextTier => "explain.house.blocker.top",
            AdvanceBlocker.UpgradeLocked => "explain.house.blocker.locked_upgrade",
            AdvanceBlocker.MissingMaterials => "explain.house.blocker.materials",
            _ => "explain.house.blocker.none",
        };
    }

    private ImmutableArray<ExplainArg> BlockerArgs(EntityId house, in HouseInfo info)
    {
        if (info.AdvanceBlocker == AdvanceBlocker.NextTierLocked && NextTier(info.Tier) is { } next)
        {
            return [ExplainArg.Of(next)];
        }

        return [];
    }

    private ImmutableArray<ExplanationLine> BlockerChildren(in HouseInfo info)
    {
        if (info.AdvanceBlocker != AdvanceBlocker.MissingMaterials || NextTier(info.Tier) is not { } next)
        {
            return [];
        }

        if (next.Value >= _content.Tiers.Count)
        {
            return [];
        }

        ISimQueries q = _queries();
        TierDef def = _content.Get(next);
        ImmutableArray<ExplanationLine>.Builder builder = ImmutableArray.CreateBuilder<ExplanationLine>(def.UpgradeCost.Goods.Length);
        foreach (GoodAmount need in def.UpgradeCost.Goods)
        {
            Amount stock = q.Storage.Stock(need.Good);

            builder.Add(new ExplanationLine(
                "explain.house.blocker.material",
                [ExplainArg.Of(need.Good), ExplainArg.Of(need.Amount), ExplainArg.Of(stock)],
                [SubjectLink.ForGood(need.Good)],
                []));
        }

        return builder.MoveToImmutable();
    }

    private ExplanationLine DeclineLine(in HouseInfo info, in HouseNeedsInfo needs)
    {
        int grace = _content.Rules.DeclineGraceDays;
        var children = new List<ExplanationLine>(needs.Needs.Length + 1);
        if (info.DeclinePaused)
        {
            children.Add(ExplanationLine.Leaf("explain.house.decline.paused"));
        }
        else
        {
            if (info.DeclineTimerDays > 0)
            {
                foreach (NeedStatus need in needs.Needs)
                {
                    if (!need.EverMet || need.Met)
                    {
                        continue;
                    }

                    if (need.Good.HasValue)
                    {
                        children.Add(new ExplanationLine(
                            "explain.house.decline.need.good",
                            [ExplainArg.Of(need.Good.Value)],
                            [SubjectLink.ForGood(need.Good.Value)],
                            []));
                    }
                    else if (need.Service.HasValue)
                    {
                        children.Add(new ExplanationLine(
                            "explain.house.decline.need.service",
                            [ExplainArg.Of(need.Service.Value)],
                            [],
                            []));
                    }
                }

                children.Add(ExplanationLine.Leaf("explain.house.decline.stop"));
            }
            else
            {
                children.Add(ExplanationLine.Leaf("explain.house.decline.stop"));
            }
        }

        return new ExplanationLine(
            "explain.house.decline",
            [ExplainArg.Of(info.DeclineTimerDays), ExplainArg.Of(grace)],
            [],
            [.. children]);
    }

    private TierId? NextTier(TierId tier)
    {
        ImmutableArray<TierId> order = _content.TiersByOrder;
        foreach (TierId candidate in order)
        {
            if (candidate.Value >= _content.Tiers.Count)
            {
                continue;
            }

            if (_content.Get(candidate).AdvanceFrom == tier)
            {
                return candidate;
            }
        }

        return null;
    }

    private static ExplainArg SourceArg(EffectSource source) => source.Kind switch
    {
        EffectSourceKind.Tech => ExplainArg.Of(source.AsTech()),
        EffectSourceKind.Civic => ExplainArg.Of(source.AsCivic()),
        EffectSourceKind.Building => ExplainArg.Of(source.AsBuilding()),
        _ => ExplainArg.Of(source.Value),
    };

    private static string FixKey(StallReason reason) => reason switch
    {
        StallReason.Paused => "explain.fix.paused",
        StallReason.NoStorehouseReach => "explain.fix.no_storehouse_reach",
        StallReason.NoWorkforce => "explain.fix.no_workforce",
        StallReason.MissingInput => "explain.fix.missing_input",
        StallReason.OutputFull => "explain.fix.output_full",
        StallReason.MissingMaterial => "explain.fix.missing_material",
        StallReason.Underfunded => "explain.fix.underfunded",
        _ => "explain.fix.none",
    };

    private sealed class ProducerGroup
    {
        private readonly List<EntityId> _members = [];
        private readonly int[] _stalls = new int[8];
        private long _productivitySum;

        public ProducerGroup(BuildingTypeId type)
        {
            Type = type;
        }

        public BuildingTypeId Type { get; }

        public int Count => _members.Count;

        public void Add(EntityId building, in ProductionInfo info)
        {
            _members.Add(building);
            _productivitySum += info.Productivity.PerMille;
            int stall = (int)info.Stall;
            if (stall >= 0 && stall < _stalls.Length)
            {
                _stalls[stall]++;
            }
        }

        public ExplanationLine Line()
        {
            int count = Math.Max(1, Count);
            int average = (int)(_productivitySum / count);
            var children = new List<ExplanationLine>(2);
            for (int s = 0; s < _stalls.Length; s++)
            {
                if (_stalls[s] > 0)
                {
                    var reason = (StallReason)s;
                    children.Add(new ExplanationLine(
                        "explain.good.producer.stall",
                        [ExplainArg.Of(reason), ExplainArg.Of(_stalls[s])],
                        [],
                        []));
                }
            }

            return new ExplanationLine(
                "explain.good.producer",
                [ExplainArg.Of(Type), ExplainArg.Of(Count), ExplainArg.Of(Ratio.FromPerMille(average))],
                [SubjectLink.ForBuildingType(Type)],
                [.. children]);
        }
    }
}
