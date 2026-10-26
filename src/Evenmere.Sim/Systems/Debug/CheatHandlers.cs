using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research;

namespace Evenmere.Sim.Systems.Debug;

/// <summary>
/// The cheat command handlers (ADR-0012). They are host tooling, not a ticked system: the simulation registers
/// them directly, with no pipeline entry and no random stream, and every handler first checks the save's cheats
/// flag. The handlers write through the same services the real systems use, in ascending ID order, so a cheat
/// applied on two worlds with the same flag gives the same state.
/// </summary>
internal static class CheatHandlers
{
    /// <summary>Registers every cheat command handler.</summary>
    /// <param name="router">The command router.</param>
    public static void RegisterAll(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);

        router.Register(new CheatGrantGoodsHandler());
        router.Register(new CheatCompleteNodeHandler());
        router.Register(new CheatSetEraHandler());
        router.Register(new CheatRevealAllHandler());
        router.Register(new CheatAddResidentsHandler());
    }

    /// <summary>Tests the save's cheats flag first, as every cheat handler must.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <returns><see cref="CommandResult.Ok"/> when cheats are enabled, else the rejection.</returns>
    internal static CommandResult CheckAllowed(SimContext ctx) => ctx.CheatsUsed
        ? CommandResult.Ok
        : CommandResult.Reject(
            RejectReason.CheatsDisabled,
            "Cheat commands need a cheats-enabled save: start a new game in a debug build or with --cheats.");
}

/// <summary>Handles <see cref="CheatGrantGoods"/>: adds the goods to the island pool.</summary>
internal sealed class CheatGrantGoodsHandler : ICommandHandler<CheatGrantGoods>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, CheatGrantGoods command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        CommandResult allowed = CheatHandlers.CheckAllowed(ctx);
        if (allowed.IsRejected)
        {
            return allowed;
        }

        if ((uint)command.Good.Value >= (uint)ctx.Content.Goods.Count)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"The content defines no good {command.Good.Value}.");
        }

        if (command.Amount < Amount.Zero)
        {
            return CommandResult.Reject(RejectReason.Invalid, "A grant cannot be negative.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, CheatGrantGoods command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        ctx.Storage.Add(command.Good, command.Amount);
    }
}

/// <summary>
/// Handles <see cref="CheatCompleteNode"/>: marks one tech researched or one civic adopted, applying its
/// effects and emitting the researched or adopted event, exactly as the research and culture systems do when
/// they complete a node — except availability is bypassed on purpose, so locked nodes complete too.
/// </summary>
internal sealed class CheatCompleteNodeHandler : ICommandHandler<CheatCompleteNode>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, CheatCompleteNode command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        CommandResult allowed = CheatHandlers.CheckAllowed(ctx);
        if (allowed.IsRejected)
        {
            return allowed;
        }

        bool hasTech = command.Tech is not null;
        bool hasCivic = command.Civic is not null;
        if (hasTech == hasCivic)
        {
            return CommandResult.Reject(RejectReason.Invalid, "A completion names exactly one node: a tech or a civic.");
        }

        if (command.Tech is { } tech && (uint)tech.Value >= (uint)ctx.Content.Techs.Count)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"The content defines no tech {tech.Value}.");
        }

        if (command.Civic is { } civic && (uint)civic.Value >= (uint)ctx.Content.Civics.Count)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"The content defines no civic {civic.Value}.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, CheatCompleteNode command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (command.Tech is { } tech)
        {
            CompleteTech(ctx, tech);
        }
        else if (command.Civic is { } civic)
        {
            CompleteCivic(ctx, civic);
        }
    }

    private static void CompleteTech(SimContext ctx, TechId tech)
    {
        ResearchState state = ctx.World.Research;
        if (Bits.Has(state.Done, tech.Value))
        {
            return;
        }

        TechDef def = ctx.Content.Get(tech);
        Bits.Set(state.Done, tech.Value);
        ctx.ProgressionService.ApplyEffects(EffectSource.ForTech(tech), def.Effects);
        ctx.Events.Emit(new TechResearched(ctx.Tick, tech));
        AdvanceTarget(state, tech);
    }

    private static void CompleteCivic(SimContext ctx, CivicId civic)
    {
        CultureState state = ctx.World.Culture;
        if (Bits.Has(state.Done, civic.Value))
        {
            return;
        }

        CivicDef def = ctx.Content.Get(civic);
        Bits.Set(state.Done, civic.Value);
        ctx.ProgressionService.ApplyEffects(EffectSource.ForCivic(civic), def.Effects);
        ctx.Events.Emit(new CivicAdopted(ctx.Tick, civic));
        AdvanceTarget(state, civic);
    }

    // Mirrors the research and culture completion: a completed target promotes the queue head, a completed
    // queued node leaves it, and any other node keeps the queues untouched.
    private static void AdvanceTarget(ResearchState state, TechId done)
    {
        if (state.Target != done)
        {
            state.Queue = Without(state.Queue, done);
            return;
        }

        if (state.Queue.Length > 0)
        {
            state.Target = state.Queue[0];
            var rest = new TechId[state.Queue.Length - 1];
            Array.Copy(state.Queue, 1, rest, 0, rest.Length);
            state.Queue = rest;
        }
        else
        {
            state.Target = null;
        }
    }

    private static void AdvanceTarget(CultureState state, CivicId done)
    {
        if (state.Target != done)
        {
            state.Queue = Without(state.Queue, done);
            return;
        }

        if (state.Queue.Length > 0)
        {
            state.Target = state.Queue[0];
            var rest = new CivicId[state.Queue.Length - 1];
            Array.Copy(state.Queue, 1, rest, 0, rest.Length);
            state.Queue = rest;
        }
        else
        {
            state.Target = null;
        }
    }

    private static TechId[] Without(TechId[] queue, TechId done)
    {
        int at = Array.IndexOf(queue, done);
        if (at < 0)
        {
            return queue;
        }

        var kept = new TechId[queue.Length - 1];
        Array.Copy(queue, kept, at);
        Array.Copy(queue, at + 1, kept, at, kept.Length - at);
        return kept;
    }

    private static CivicId[] Without(CivicId[] queue, CivicId done)
    {
        int at = Array.IndexOf(queue, done);
        if (at < 0)
        {
            return queue;
        }

        var kept = new CivicId[queue.Length - 1];
        Array.Copy(queue, kept, at);
        Array.Copy(queue, at + 1, kept, at, kept.Length - at);
        return kept;
    }
}

/// <summary>Handles <see cref="CheatSetEra"/>: moves the island into the era and emits the era-began event.</summary>
internal sealed class CheatSetEraHandler : ICommandHandler<CheatSetEra>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, CheatSetEra command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        CommandResult allowed = CheatHandlers.CheckAllowed(ctx);
        if (allowed.IsRejected)
        {
            return allowed;
        }

        if ((uint)command.Era.Value >= (uint)ctx.Content.Eras.Count)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"The content defines no era {command.Era.Value}.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, CheatSetEra command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        ProgressionState state = ctx.World.Progression;
        if (state.Era == command.Era.Value)
        {
            return;
        }

        state.Era = command.Era.Value;
        ctx.Events.Emit(new EraBegan(ctx.Tick, command.Era));
    }
}

/// <summary>
/// Handles <see cref="CheatRevealAll"/>. Exploration is still a stub (M2-06), so there is no fog to lift yet:
/// the handler validates the cheats flag and the region number, then reports that the reveal needs M2-06.
/// </summary>
internal sealed class CheatRevealAllHandler : ICommandHandler<CheatRevealAll>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, CheatRevealAll command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        CommandResult allowed = CheatHandlers.CheckAllowed(ctx);
        if (allowed.IsRejected)
        {
            return allowed;
        }

        if (command.Region < CheatRevealAll.AllRegions)
        {
            return CommandResult.Reject(RejectReason.Invalid, "A reveal names a region number, or -1 for all regions.");
        }

        return CommandResult.Reject(RejectReason.Invalid, "Revealing needs exploration (M2-06): the island has no fog yet.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, CheatRevealAll command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);
    }
}

/// <summary>
/// Handles <see cref="CheatAddResidents"/>: fills free beds in ascending house-ID order. The validation sums
/// the free beds first, so the move is all or nothing: it never leaves a partially filled island behind.
/// </summary>
internal sealed class CheatAddResidentsHandler : ICommandHandler<CheatAddResidents>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, CheatAddResidents command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        CommandResult allowed = CheatHandlers.CheckAllowed(ctx);
        if (allowed.IsRejected)
        {
            return allowed;
        }

        if (command.Count <= 0)
        {
            return CommandResult.Reject(RejectReason.Invalid, "A residents cheat moves at least one resident.");
        }

        int spare = FreeBeds(ctx);
        if (spare < command.Count)
        {
            return CommandResult.Reject(RejectReason.Invalid, $"The island has only {spare} free beds for {command.Count} residents.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, CheatAddResidents command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        PopulationState state = ctx.World.Population;
        int remaining = command.Count;
        foreach (IdTable<HouseRecord>.Entry entry in state.Houses)
        {
            if (remaining <= 0)
            {
                break;
            }

            ref HouseRecord house = ref entry.Value;
            int room = house.Capacity - house.Residents;
            if (room <= 0)
            {
                continue;
            }

            int moved = Math.Min(room, remaining);
            house.Residents += moved;
            state.AddTierResidents(house.Tier, moved);
            remaining -= moved;
        }
    }

    private static int FreeBeds(SimContext ctx)
    {
        int spare = 0;
        foreach (IdTable<HouseRecord>.Entry entry in ctx.World.Population.Houses)
        {
            int room = entry.Value.Capacity - entry.Value.Residents;
            if (room > 0)
            {
                spare += room;
            }
        }

        return spare;
    }
}
