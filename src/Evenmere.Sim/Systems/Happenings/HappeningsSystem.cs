using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>
/// The happenings system: gentle choice events. Its cadence is <see cref="Cadence.Daily"/>. (M2-12.)
/// </summary>
/// <remarks>
/// Each daily step does three things in order. First it expires the duration-bound modifiers whose tick has come,
/// removing them through <see cref="Services.IProgressionService.RemoveSource"/>. Then a happening pending for ten
/// days resolves into its default option, so an unanswered happening never waits forever. Finally, while nothing is
/// pending, every eligible happening (its trigger holds and its cooldown has elapsed) is rolled in ascending ID order
/// with the system's own <c>Pcg32</c> at its per-mille chance; the first success becomes pending and the roll stops.
/// </remarks>
internal sealed class HappeningsSystem : ISimSystem
{
    /// <summary>Gets how many days a pending happening waits for a choice before the default option applies.</summary>
    internal const int PendingTimeoutDays = 10;

    private readonly HappeningsState _state;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="HappeningsSystem"/> class.</summary>
    /// <param name="state">The happenings state to run over; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content, for the happening definitions.</param>
    /// <param name="clock">Reads the simulation clock, so the queries answer cooldowns against the current tick.</param>
    internal HappeningsSystem(HappeningsState state, ContentDb content, Func<GameClock> clock)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        state.EnsureSlots(content.Happenings.Count);
        _state = state;
        _content = content;
        Queries = new HappeningsQueries(state, clock);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Happenings;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IHappeningsQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new ChooseHappeningOptionHandler(_state));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        ExpireTempModifiers(ctx);
        TimeoutPending(ctx);
        RollHappenings(ctx);
    }

    /// <summary>
    /// Applies one option of a happening: grants goods and points at once, registers modifiers (and festival-request
    /// discounts) with the source <c>happening:&lt;id&gt;</c>, clears the pending slot and starts the cooldown.
    /// </summary>
    /// <param name="state">The happenings state to write.</param>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="happening">The happening being resolved; must name content the loader accepted.</param>
    /// <param name="optionIndex">Which option to apply; out-of-range indices fall back to the first option.</param>
    internal static void Resolve(HappeningsState state, SimContext ctx, HappeningId happening, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);

        HappeningDef def = ctx.Content.Get(happening);
        int option = (uint)optionIndex < (uint)def.Options.Length ? optionIndex : 0;
        // Content the loader accepted always carries at least one option; the null below only guards
        // hand-built definitions in tests that never went through content loading.
        HappeningOption? effects = def.Options.Length > 0 ? def.Options[option] : null;

        var source = EffectSource.ForHappening(happening);

        // Clearing first keeps repeats working: applying the same source twice is a no-op by contract, so without
        // this a second occurrence would grant nothing and refresh nothing. Unlocks never go down, so removing then
        // re-applying leaves them set while grants fire again.
        ctx.ProgressionService.RemoveSource(source);

        List<Effect>? rest = null;
        List<Effect>? discounts = null;
        if (effects is not null)
        {
            foreach (Effect effect in effects.Effects)
            {
                if (effect is RequestFestivalEffect request)
                {
                    // A festival request has no behaviour in the effect applier; it becomes a festivalCost
                    // discount the holding cost check reads, with the happening as its source.
                    discounts ??= [];
                    discounts.Add(new ModifierEffect(Stat.FestivalCost, new IslandTarget(), -request.DiscountPercent, null));
                }
                else
                {
                    rest ??= [];
                    rest.Add(effect);
                }
            }
        }

        bool hasModifiers = false;
        if (rest is { Count: > 0 })
        {
            ctx.ProgressionService.ApplyEffects(source, [.. rest]);
            foreach (Effect effect in rest)
            {
                if (effect is ModifierEffect)
                {
                    hasModifiers = true;
                    break;
                }
            }
        }

        if (discounts is { Count: > 0 })
        {
            ctx.ProgressionService.ApplyEffects(source, [.. discounts]);
            hasModifiers = true;
        }

        if ((effects?.DurationDays ?? 0) > 0 && hasModifiers)
        {
            // One entry per resolve: drop a stale expiry from a previous occurrence first, or it would remove
            // the fresh modifiers early.
            long expires = ctx.Tick + ((long)(effects?.DurationDays ?? 0) * GameTime.TicksPerDay);
            var kept = new List<TempModifierRecord>(state.TempModifiers.Length + 1);
            foreach (TempModifierRecord record in state.TempModifiers)
            {
                if (record.Happening != happening)
                {
                    kept.Add(record);
                }
            }

            kept.Add(new TempModifierRecord(happening, expires));
            state.TempModifiers = [.. kept];
        }

        state.Pending = null;
        if ((uint)happening.Value < (uint)state.CooldownUntil.Length)
        {
            state.CooldownUntil[happening.Value] = ctx.Tick + ((long)def.CooldownDays * GameTime.TicksPerDay);
        }

        ctx.Events.Emit(new HappeningResolved(ctx.Tick, happening, option));
    }

    private void ExpireTempModifiers(SimContext ctx)
    {
        if (_state.TempModifiers.Length == 0)
        {
            return;
        }

        List<TempModifierRecord>? kept = null;
        bool expiredAny = false;
        for (int i = 0; i < _state.TempModifiers.Length; i++)
        {
            TempModifierRecord record = _state.TempModifiers[i];
            if (ctx.Tick < record.ExpiresTick)
            {
                if (expiredAny)
                {
                    // Safe: kept is created alongside the first expiry, before expiredAny turns true.
                    kept!.Add(record);
                }

                continue;
            }

            if (!expiredAny)
            {
                expiredAny = true;
                kept = new List<TempModifierRecord>(_state.TempModifiers.Length - i);
                for (int j = 0; j < i; j++)
                {
                    kept.Add(_state.TempModifiers[j]);
                }
            }

            ctx.ProgressionService.RemoveSource(EffectSource.ForHappening(record.Happening));
        }

        if (expiredAny)
        {
            // Safe: kept is created alongside the first expiry, before expiredAny turns true.
            _state.TempModifiers = [.. kept!];
        }
    }

    private void TimeoutPending(SimContext ctx)
    {
        if (_state.Pending is not { } pending)
        {
            return;
        }

        long elapsedDays = (ctx.Tick - pending.RaisedTick) / GameTime.TicksPerDay;
        if (elapsedDays < PendingTimeoutDays)
        {
            return;
        }

        HappeningDef def = ctx.Content.Get(pending.Happening);
        int option = (uint)def.DefaultOption < (uint)def.Options.Length ? def.DefaultOption : 0;
        Resolve(_state, ctx, pending.Happening, option);
    }

    private void RollHappenings(SimContext ctx)
    {
        if (_state.Pending is not null || _content.Happenings.Count == 0)
        {
            return;
        }

        // Ascending ID order, so the roll sequence (and which happening wins) is deterministic.
        for (int i = 0; i < _content.Happenings.Count; i++)
        {
            if ((uint)i < (uint)_state.CooldownUntil.Length && ctx.Tick < _state.CooldownUntil[i])
            {
                continue;
            }

            var happening = HappeningId.From(checked((ushort)i));
            HappeningDef def = _content.Get(happening);
            if (!ctx.Queries.Progression.Evaluate(def.Trigger))
            {
                continue;
            }

            // A per-mille integer roll from the system's own stream: the draw is stored in the world state,
            // so a save round trip continues the sequence exactly.
            if (ctx.Rng.NextInt(1000) < def.ChancePerMille)
            {
                _state.Pending = new PendingHappening(happening, ctx.Tick);
                ctx.Events.Emit(new HappeningPending(ctx.Tick, happening));
                return;
            }
        }
    }
}
