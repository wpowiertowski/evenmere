using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// The production system: producers, recipes and their buffers. Its cadence is <see cref="Cadence.EveryTick"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each tick, for every finished producer in ascending ID order, productivity is
/// <c>workforce fill × area yield × (1000 + 10 × bonus%) × season / 1000³</c> in per-mille, and zero unless the building is
/// working (not paused, in storehouse reach, staffed, with its inputs and room for its outputs). It is added to the cycle's
/// progress, and a cycle completes at <c>cycleSeconds × 4 × 1000</c>: it consumes the recipe's inputs, adds its outputs
/// to the output buffer and counts them as produced. The bonus is the progression modifiers for the building plus its own
/// site bonuses.
/// </para>
/// <para>
/// The system listens to the building lifecycle (<see cref="IBuildingLifecycleListener"/>): a record is created when a
/// producer activates and dropped when it is removed, and the cached area yield and site bonus of nearby producers are
/// refreshed whenever a building comes or goes. M1-13 subscribes it, in pipeline order.
/// </para>
/// </remarks>
internal sealed class ProductionSystem : ISimSystem, IBuildingLifecycleListener
{
    private readonly ProductionState _state;
    private readonly ContentDb _content;
    private readonly SiteEvaluator _sites;
    private readonly int[] _seasonFactors;

    /// <summary>Initializes a new instance of the <see cref="ProductionSystem"/> class.</summary>
    /// <param name="state">The production state to run over; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content.</param>
    public ProductionSystem(ProductionState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;
        _content = content;
        _sites = new SiteEvaluator(content);
        _seasonFactors = Seasons.SeasonCurve.BuildTable(content);
        state.EnsureGoodSlots(content.Goods.Count);
        Queries = new ProductionQueries(state, content);
        Buffers = new ProductionBuffers(state, content);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Production;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IProductionQueries Queries { get; }

    /// <summary>Gets Production's buffers as Logistics sees them; the composition root hands this to the write services.</summary>
    public IProductionBuffers Buffers { get; }

    /// <summary>Gets the season factor the tick uses for a building type in a season, in per-mille; the tests read it to pin the table.</summary>
    /// <param name="type">The building type.</param>
    /// <param name="season">The season.</param>
    /// <returns>The precomputed factor, the same value <see cref="Seasons.SeasonCurve"/> builds the table from.</returns>
    internal int SeasonFactorFor(BuildingTypeId type, Season season) =>
        _seasonFactors[type.Value * GameTime.SeasonsPerYear + (int)season];

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        router.Register(new SetBuildingPausedHandler(_state));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        RollDay(ctx.Clock.Day);

        IMapQueries map = ctx.Queries.Map;
        foreach (IdTable<ProducerRecord>.Entry entry in _state.Producers)
        {
            // Paused is a registry state (ADR-0011), so it is read here every tick, never cached.
            if (map.TryGetBuilding(entry.Id, out BuildingInfo building) && building.IsComplete)
            {
                Step(ctx, entry.Id, building, entry.Value);
            }
        }
    }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        BuildingDef def = _content.Get(building.Type);
        if (def.Recipe is { } recipe)
        {
            if (_state.Producers.TryGet(building.Id, out ProducerRecord? existing))
            {
                existing.AreaYield = _sites.AreaYield(ctx.Queries.Map, building, def);
                existing.SiteBonusPercent = _sites.SiteBonusPercent(ctx.Queries.Map, building, def);
            }
            else
            {
                AddRecord(ctx, building, def, recipe);
            }
        }

        RefreshNeighbours(ctx, building, null);
    }

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Whatever the building held goes with it; carts already on the road find no record and drop their goods (see ProductionBuffers.AddInput).
        _state.Producers.Remove(building.Id);
        RefreshNeighbours(ctx, building, null);
    }

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        BuildingDef def = _content.Get(building.Type);
        bool hadRecord = _state.Producers.TryGet(building.Id, out ProducerRecord? record);
        if (building.IsComplete && def.Recipe is { } recipe)
        {
            if (record is not null && record.Recipe == recipe)
            {
                // An upgrade that keeps the recipe keeps the goods and the progress; only the surroundings are read again.
                record.AreaYield = _sites.AreaYield(ctx.Queries.Map, building, def);
                record.SiteBonusPercent = _sites.SiteBonusPercent(ctx.Queries.Map, building, def);
            }
            else
            {
                _state.Producers.Remove(building.Id);
                AddRecord(ctx, building, def, recipe);
            }
        }
        else if (hadRecord)
        {
            _state.Producers.Remove(building.Id);
        }

        RefreshNeighbours(ctx, building, previousType);
    }

    private void AddRecord(SimContext ctx, BuildingInfo building, BuildingDef def, RecipeId recipe)
    {
        var record = ProducerRecord.Create(recipe, _content.Get(recipe));
        record.AreaYield = _sites.AreaYield(ctx.Queries.Map, building, def);
        record.SiteBonusPercent = _sites.SiteBonusPercent(ctx.Queries.Map, building, def);
        _state.Producers.Add(building.Id, record);
    }

    // Recomputes the caches of the producers that a building coming, going or changing can affect. The test is a
    // generous bounding test, and recomputing is idempotent, so a producer that did not need it just gets the same answer.
    private void RefreshNeighbours(SimContext ctx, BuildingInfo changed, BuildingTypeId? previousType)
    {
        IMapQueries map = ctx.Queries.Map;
        foreach (IdTable<ProducerRecord>.Entry entry in _state.Producers)
        {
            if (entry.Id == changed.Id || !map.TryGetBuilding(entry.Id, out BuildingInfo producer) || !producer.IsComplete)
            {
                continue;
            }

            BuildingDef def = _content.Get(producer.Type);
            ProducerRecord record = entry.Value;
            if (SiteEvaluator.AreaYieldDependsOn(def, producer, changed, previousType))
            {
                record.AreaYield = _sites.AreaYield(map, producer, def);
            }

            if (SiteEvaluator.SiteBonusDependsOn(def, producer, changed))
            {
                record.SiteBonusPercent = _sites.SiteBonusPercent(map, producer, def);
            }
        }
    }

    // Moves the day buckets on when a new day has begun. Yesterday is only the previous day: after a longer gap it is empty.
    private void RollDay(long day)
    {
        ProducedTotals totals = _state.Totals;
        if (day == totals.Day)
        {
            return;
        }

        if (day == totals.Day + 1)
        {
            Array.Copy(totals.Today, totals.Yesterday, totals.Today.Length);
        }
        else
        {
            Array.Clear(totals.Yesterday);
        }

        Array.Clear(totals.Today);
        totals.Day = day;
    }

    private void Step(SimContext ctx, EntityId id, BuildingInfo building, ProducerRecord record)
    {
        BuildingDef def = _content.Get(building.Type);
        RecipeDef recipe = _content.Get(record.Recipe);
        RulesDef rules = _content.Rules;

        // A building with no workforce spec needs nobody, so it is fully staffed.
        Ratio fill = def.Workforce is null
            ? Ratio.One
            : Ratio.FromPerMille(Math.Clamp(ctx.Queries.Workforce.FillOf(id).PerMille, 0, ProductionMath.One));
        bool inputsReady = ProductionMath.HasInputs(record, recipe);
        int bonus = ctx.Queries.Progression.BonusPercent(Stat.Productivity, ModifierContext.ForBuilding(id, building.Type))
            + record.SiteBonusPercent;
        int season = _seasonFactors[building.Type.Value * GameTime.SeasonsPerYear + (int)ctx.Clock.Season];

        // The reasons are checked in priority order: the first that holds is the one the player is told.
        StallReason reason;
        if (building.State == BuildingState.Paused)
        {
            reason = StallReason.Paused;
        }
        else if (!ctx.Queries.Roads.IsInStorehouseReach(id))
        {
            reason = StallReason.NoStorehouseReach;
        }
        else if (fill.PerMille == 0)
        {
            reason = StallReason.NoWorkforce;
        }
        else if (!inputsReady)
        {
            reason = StallReason.MissingInput;
        }
        else if (!ProductionMath.HasOutputRoom(record, recipe, rules.OutputBuffer))
        {
            reason = StallReason.OutputFull;
        }
        else
        {
            reason = StallReason.None;
        }

        // A working building can still make no progress if its area yields nothing or its penalties cancel its bonuses;
        // there is no stall reason for that, so the explain queries show it through the area yield and bonus factors.
        // The season factor applies after the M1-08 productivity terms, in per-mille, rounded down once at the end.
        long productivity = reason == StallReason.None
            ? ProductionMath.Productivity(fill, record.AreaYield, bonus) * season / ProductionMath.One
            : 0;

        record.WorkforceFill = fill;
        record.InputsReady = inputsReady;
        record.BonusPercent = bonus;
        record.Productivity = Ratio.FromPerMille((int)productivity);

        if (productivity > 0)
        {
            record.Progress += productivity;
            CompleteCycles(ctx, record, recipe, rules);
        }

        RaiseRequests(ctx.Tick, record, recipe, rules, building.State != BuildingState.Paused);

        if (reason != record.Stall)
        {
            record.Stall = reason;
            ctx.Events.Emit(reason == StallReason.None
                ? new ProductionResumed(ctx.Tick, id)
                : new ProductionStalled(ctx.Tick, id, reason));
        }
    }

    // Completes every cycle the progress has paid for. A bonus can pay for more than one in a tick; if the inputs or the
    // room run out part-way, the progress waits at a full cycle and the next tick reports why.
    private void CompleteCycles(SimContext ctx, ProducerRecord record, RecipeDef recipe, RulesDef rules)
    {
        long target = ProductionMath.CycleTarget(recipe);
        while (record.Progress >= target)
        {
            if (!ProductionMath.HasInputs(record, recipe) || !ProductionMath.HasOutputRoom(record, recipe, rules.OutputBuffer))
            {
                record.Progress = target;
                return;
            }

            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                record.Inputs[i] -= recipe.Inputs[i].Amount;
            }

            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                GoodAmount made = recipe.Outputs[i];
                record.Outputs[i] += made.Amount;
                _state.Totals.Today[made.Good.Value] += made.Amount;
                ctx.Counters.Add(CounterKey.Produced(made.Good), made.Amount.Milli);
            }

            record.Progress -= target;
        }
    }

    // Opens a request when a need arises and closes it when the need has gone. An open request keeps its tick, which is its place in the queue.
    private static void RaiseRequests(long tick, ProducerRecord record, RecipeDef recipe, RulesDef rules, bool wantsDeliveries)
    {
        for (int i = 0; i < recipe.Outputs.Length; i++)
        {
            ref long since = ref record.OutputRequestTicks[i];
            if (record.Outputs[i] < rules.CartLoad)
            {
                since = ProducerRecord.NoRequest;
            }
            else if (since == ProducerRecord.NoRequest)
            {
                since = tick;
            }
        }

        for (int i = 0; i < recipe.Inputs.Length; i++)
        {
            // A paused building is not consuming, so it is not sent more goods; pausing is how the player stops the drain.
            ref long since = ref record.InputRequestTicks[i];
            bool low = record.Inputs[i] < ProductionMath.DeliveryThreshold(recipe.Inputs[i].Amount, rules.CartLoad);
            if (!wantsDeliveries || !low)
            {
                since = ProducerRecord.NoRequest;
            }
            else if (since == ProducerRecord.NoRequest)
            {
                since = tick;
            }
        }
    }
}
