using Evenmere.Content;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Modifiers;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The progression system: unlocks, effects, the points ledger and milestone counters, and the condition evaluator. Its
/// cadence is <see cref="Cadence.Daily"/>, but in M1 nothing in it needs a daily step: effects arrive through
/// <see cref="Service"/> when research and culture complete a node, points and counters change when other systems credit
/// them, and the era only changes through <see cref="ApplyStartUnlocks"/>.
/// </summary>
/// <remarks>
/// The system owns the write services (<see cref="Service"/>, <see cref="Ledger"/>, <see cref="Counters"/>) that the roster
/// hands to <c>SimContext</c>, and feeds the modifiers service the season and the condition evaluator it needs.
/// </remarks>
internal sealed class ProgressionSystem : ISimSystem
{
    private readonly ProgressionState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionSystem"/> class over a state. A new game's empty state is sized to
    /// the content; a loaded state must already fit it.
    /// </summary>
    /// <param name="state">The progression slice of the world state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="modifiers">The modifiers service, whose index answers the stat queries and takes the modifier effects.</param>
    /// <exception cref="InvalidDataException">The state does not fit the content.</exception>
    public ProgressionSystem(ProgressionState state, ContentDb content, ModifiersSystem modifiers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(modifiers);

        _state = state;
        state.FitTo(content);

        var holder = new SimContextHolder();
        Counters = new MilestoneCounters(state);
        Ledger = new PointsLedger(state);
        var evaluator = new ConditionEvaluator(state, content, Counters, holder);
        Queries = new ProgressionQueries(state, modifiers.Index, Counters, evaluator, content);
        modifiers.Attach(evaluator);

        Service = new EffectApplier(state, modifiers, Ledger, holder);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Progression;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public ProgressionQueries Queries { get; }

    /// <summary>Gets the write side that applies effects, which other systems use through <c>SimContext.ProgressionService</c>.</summary>
    public IProgressionService Service { get; }

    /// <summary>Gets the points ledger, which other systems use through <c>SimContext.PointsLedger</c>.</summary>
    public PointsLedger Ledger { get; }

    /// <summary>Gets the milestone counters, which other systems use through <c>SimContext.Counters</c>.</summary>
    public MilestoneCounters Counters { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // The progression system has no commands: research and culture apply effects through the service when a node completes.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // Research and culture run before this system each day, so the gateways they completed today are done already.
        Eras.EraAdvancement.Check(_state, ctx);
    }

    /// <summary>
    /// Unlocks everything the content marks <c>"start": true</c> (buildings, recipes, tiers, traditions and festivals) and puts
    /// the island in the first era. A new game calls this once, before the first tick. Calling it again changes nothing.
    /// </summary>
    /// <param name="ctx">The simulation context, which supplies the content.</param>
    internal void ApplyStartUnlocks(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ContentDb content = ctx.Content;

        for (int i = 0; i < content.Buildings.Count; i++)
        {
            if (content.Buildings.Items[i].Start)
            {
                Bits.Set(_state.Buildings, i);
            }
        }

        for (int i = 0; i < content.Recipes.Count; i++)
        {
            if (content.Recipes.Items[i].Start)
            {
                Bits.Set(_state.Recipes, i);
            }
        }

        for (int i = 0; i < content.Tiers.Count; i++)
        {
            if (content.Tiers.Items[i].Start)
            {
                Bits.Set(_state.Tiers, i);
            }
        }

        for (int i = 0; i < content.Traditions.Count; i++)
        {
            if (content.Traditions.Items[i].Start)
            {
                Bits.Set(_state.Traditions, i);
            }
        }

        for (int i = 0; i < content.Festivals.Count; i++)
        {
            if (content.Festivals.Items[i].Start)
            {
                Bits.Set(_state.Festivals, i);
            }
        }

        // Era IDs follow string order, so the first era is the one with the lowest order number, not necessarily ID 0.
        if (content.ErasByOrder.Length > 0)
        {
            _state.Era = content.ErasByOrder[0].Value;
        }
    }
}
