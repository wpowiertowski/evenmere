using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Culture.Festivals;

/// <summary>
/// Handles <see cref="Festivals.Commands.HoldFestival"/>: checks the unlock, the season, the one-running and
/// once-per-year rules and the discounted cost, then takes the goods, registers the festival's effects with the source
/// <c>festival:&lt;id&gt;</c> and starts the run the daily step counts down.
/// </summary>
/// <param name="state">The culture state to write.</param>
internal sealed class HoldFestivalHandler(CultureState state) : ICommandHandler<Festivals.Commands.HoldFestival>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, Festivals.Commands.HoldFestival command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if ((uint)command.Festival.Value >= (uint)ctx.Content.Festivals.Count)
        {
            return CommandResult.Invalid($"There is no festival {command.Festival}.");
        }

        FestivalDef def = ctx.Content.Get(command.Festival);
        if (!ctx.Queries.Progression.IsFestivalUnlocked(command.Festival))
        {
            return CommandResult.Reject(RejectReason.FestivalNotUnlocked, $"Festival {ctx.Content.NameOf(command.Festival)} is not unlocked.");
        }

        if (state.FestivalRunning || state.Active is not null)
        {
            return CommandResult.Reject(RejectReason.FestivalAlreadyRunning, "A festival is already running; only one runs at a time.");
        }

        if (FestivalScheduler.HeldInYear(state.FestivalLastHeldYear, command.Festival, ctx.Clock.Year))
        {
            return CommandResult.Reject(RejectReason.FestivalHeldThisYear, $"Festival {ctx.Content.NameOf(command.Festival)} was already held this year.");
        }

        if (def.Seasons.Length > 0 && !Contains(def.Seasons, ctx.Clock.Season))
        {
            return CommandResult.Reject(RejectReason.WrongSeason, $"Festival {ctx.Content.NameOf(command.Festival)} cannot be held in {ctx.Clock.Season}.");
        }

        BuildingInfo? ground = Find(ctx, command.Ground);
        if (ground is null)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.Ground} is not a building.");
        }

        if (ground.Value.State is not (BuildingState.Active or BuildingState.Paused))
        {
            return CommandResult.Invalid($"Festival {ctx.Content.NameOf(command.Festival)} needs a finished festival ground; {command.Ground} is still a site.");
        }

        if (!HasTag(ctx.Content.Get(ground.Value.Type).Tags, FestivalScheduler.GroundTag))
        {
            return CommandResult.Invalid($"Festival {ctx.Content.NameOf(command.Festival)} needs a festival ground; {command.Ground} is a {ctx.Content.NameOf(ground.Value.Type)}.");
        }

        if (def.Cost.Coin > Core.Numerics.Amount.Zero)
        {
            return CommandResult.Reject(RejectReason.FestivalGoodsShort, $"Festival {ctx.Content.NameOf(command.Festival)} costs coin, which the island cannot pay.");
        }

        CostSpec cost = FestivalScheduler.DiscountedCost(def, ctx.Queries.Progression);
        foreach (GoodAmount line in cost.Goods)
        {
            if (ctx.Storage.Get(line.Good) < line.Amount)
            {
                return CommandResult.Reject(RejectReason.FestivalGoodsShort, $"Festival {ctx.Content.NameOf(command.Festival)} needs {line.Amount} of {ctx.Content.NameOf(line.Good)}.");
            }
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, Festivals.Commands.HoldFestival command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        FestivalDef def = ctx.Content.Get(command.Festival);
        CostSpec cost = FestivalScheduler.DiscountedCost(def, ctx.Queries.Progression);
        foreach (GoodAmount line in cost.Goods)
        {
            // Validate checked every line, so each take succeeds and the pool is never left half-paid.
            if (!ctx.Storage.TryTake(line.Good, line.Amount))
            {
                throw new InvalidOperationException($"The pool lost {line.Amount} of {ctx.Content.NameOf(line.Good)} between the check and the take.");
            }
        }

        int days = FestivalScheduler.DurationDays(def, ctx.Queries.Progression);
        state.Active = new FestivalRun(command.Festival, command.Ground, days);
        state.FestivalRunning = true;
        state.FestivalLastHeldYear[command.Festival.Value] = FestivalScheduler.Marker(ctx.Clock.Year);
        ctx.Counters.Add(CounterKey.FestivalsHeld, 1);
        ctx.ProgressionService.ApplyEffects(EffectSource.ForFestival(command.Festival), def.Effects);
        ctx.Events.Emit(new FestivalStarted(ctx.Tick, command.Festival, command.Ground));
    }

    private static BuildingInfo? Find(SimContext ctx, EntityId ground)
    {
        // Ascending ID order, so two runs find the same building; there is only ever one with the ID.
        foreach (BuildingInfo building in ctx.Queries.Map.Buildings)
        {
            if (building.Id == ground)
            {
                return building;
            }
        }

        return null;
    }

    private static bool Contains(System.Collections.Immutable.ImmutableArray<Core.Time.Season> seasons, Core.Time.Season season)
    {
        foreach (Core.Time.Season candidate in seasons)
        {
            if (candidate == season)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTag(System.Collections.Immutable.ImmutableArray<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
