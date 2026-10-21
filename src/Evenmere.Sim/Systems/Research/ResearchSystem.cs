using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// The research system: knowledge accrual, techs and inspirations. Its cadence is <see cref="Cadence.Daily"/>.
/// </summary>
/// <remarks>
/// Each daily run does three things in order. First it credits every active building's own knowledge outputs, scaled by
/// workforce fill and the <c>output.knowledge</c> bonus, so the Founders' Hall pays for the first techs. Then it checks
/// every unresearched node's inspiration condition. Then it takes the day's knowledge from the ledger (plus whatever was
/// banked while nothing was targeted) and spends it on the target, with any overflow carrying into the queued nodes in
/// order. Points that arrive while nothing is targeted are banked, not lost, and flow to the next target set.
/// </remarks>
internal sealed class ResearchSystem : ISimSystem
{
    private readonly ResearchState _state;
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;

    /// <summary>Initializes a new instance of the <see cref="ResearchSystem"/> class.</summary>
    /// <param name="state">The research state to run over; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">
    /// A late-bound accessor for the query facade. The roster builds this system before the facade exists, and queries
    /// carry no <see cref="SimContext"/>, so the queries reach progression through this. It is called only when a query
    /// runs, never during construction.
    /// </param>
    public ResearchSystem(ResearchState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        state.EnsureSlots(content.Techs.Count);
        _state = state;
        _content = content;
        _queries = queries;
        Queries = new ResearchQueries(state, content, queries);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Research;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IResearchQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new SetResearchTargetHandler(_state, _content));
        router.Register(new SetResearchQueueHandler(_state, _content));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        CreditBuildingOutputs(ctx);
        CheckInspirations(ctx);
        Accrue(ctx);
    }

    private void CreditBuildingOutputs(SimContext ctx)
    {
        foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
        {
            // Paused buildings produce nothing, and sites and ruins are not finished. The sequence also visits paused
            // buildings, so the state is checked outright rather than trusted to the filter.
            if (building.State != BuildingState.Active || (uint)building.Type.Value >= (uint)_content.Buildings.Count)
            {
                continue;
            }

            Amount output = _content.Get(building.Type).Outputs?.Knowledge ?? Amount.Zero;
            if (output <= Amount.Zero)
            {
                continue;
            }

            // A building with no workforce spec counts as fully staffed: FillOf reports 1000 for it.
            Ratio fill = ctx.Queries.Workforce.FillOf(building.Id);
            int bonus = ctx.Queries.Progression.BonusPercent(Stat.OutputKnowledge, ModifierContext.ForBuilding(building.Id, building.Type));
            Amount credit = ResearchMath.BuildingCredit(output, fill, bonus);
            if (credit > Amount.Zero)
            {
                ctx.PointsLedger.Credit(PointKind.Knowledge, credit);
            }
        }
    }

    private void CheckInspirations(SimContext ctx)
    {
        // Ascending ID order, so the events of one day are deterministic when several conditions hold at once.
        for (int i = 0; i < _content.Techs.Count; i++)
        {
            if (Bits.Has(_state.Inspired, i) || Bits.Has(_state.Done, i))
            {
                continue;
            }

            var tech = TechId.From(checked((ushort)i));
            if (_content.Get(tech).Inspiration is not { } when)
            {
                continue;
            }

            if (ctx.Queries.Progression.Evaluate(when))
            {
                Bits.Set(_state.Inspired, i);
                ctx.Events.Emit(InspirationTriggered.ForTech(ctx.Tick, tech));
            }
        }
    }

    private void Accrue(SimContext ctx)
    {
        int discount = _content.Rules.InspirationDiscount.PerMille;
        Amount today = ctx.PointsLedger.TakeToday(PointKind.Knowledge);
        _state.PointsPerDay = today;

        Amount points = today + _state.Banked;
        _state.Banked = Amount.Zero;

        while (true)
        {
            if (_state.Target is not { } target)
            {
                // Nothing to spend on: bank the points for the next target instead of losing them.
                _state.Banked = points;
                break;
            }

            Amount space = ResearchMath.EffectiveCost(_state, _content, discount, target) - ResearchMath.ProgressOf(_state, _content, target);
            if (space <= Amount.Zero)
            {
                // The inspiration floor can leave progress at or over the discounted cost; that completes the tech for free.
                Complete(ctx, target);
                continue;
            }

            if (points <= Amount.Zero)
            {
                break;
            }

            var give = Amount.Min(space, points);
            _state.Progress[target.Value] += give;
            points -= give;
            if (give == space)
            {
                Complete(ctx, target);
            }
            else
            {
                break;
            }
        }
    }

    private void Complete(SimContext ctx, TechId tech)
    {
        TechDef def = _content.Get(tech);
        Bits.Set(_state.Done, tech.Value);
        ctx.ProgressionService.ApplyEffects(EffectSource.ForTech(tech), def.Effects);
        ctx.Events.Emit(new TechResearched(ctx.Tick, tech));

        if (_state.Queue.Length > 0)
        {
            _state.Target = _state.Queue[0];
            var rest = new TechId[_state.Queue.Length - 1];
            Array.Copy(_state.Queue, 1, rest, 0, rest.Length);
            _state.Queue = rest;
        }
        else
        {
            _state.Target = null;
        }
    }
}
