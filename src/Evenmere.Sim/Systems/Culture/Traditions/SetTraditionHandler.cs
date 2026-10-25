using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Sim.Systems.Culture.Traditions;

/// <summary>
/// Handles <see cref="SetTradition"/>: slots a tradition card or clears the slot. Slotting registers the card's
/// effects with the source <c>tradition:&lt;id&gt;</c>; clearing (or replacing) removes them through
/// <see cref="IProgressionService.RemoveSource"/>, so the modifier index always matches the slots. Setting a slot to
/// the card it already holds changes nothing and emits no event.
/// </summary>
/// <param name="state">The culture state to write.</param>
/// <param name="content">The loaded content, for the card definitions.</param>
internal sealed class SetTraditionHandler(CultureState state, ContentDb content) : ICommandHandler<SetTradition>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetTradition command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!IsSlot(command.Slot) || command.Index < 0 || command.Index >= ctx.Queries.Progression.TraditionSlotCount(command.Slot))
        {
            return CommandResult.Reject(RejectReason.SlotMismatch, $"There is no tradition slot {command.Slot} {command.Index}.");
        }

        if (command.Tradition is not { } tradition)
        {
            return CommandResult.Ok;
        }

        if ((uint)tradition.Value >= (uint)content.Traditions.Count || !ctx.Queries.Progression.IsTraditionUnlocked(tradition))
        {
            return CommandResult.Reject(RejectReason.TraditionNotUnlocked, $"Tradition {NameOf(tradition)} is not unlocked.");
        }

        if (!TraditionSlots.FitsSlot(content.Get(tradition).Slot, command.Slot))
        {
            return CommandResult.Reject(RejectReason.SlotMismatch, $"Tradition {NameOf(tradition)} does not fit a {command.Slot} slot.");
        }

        if (TraditionSlots.IsSlottedElsewhere(state.Slotted, tradition, command.Slot, command.Index))
        {
            return CommandResult.Reject(RejectReason.AlreadySlotted, $"Tradition {NameOf(tradition)} is already slotted in another slot.");
        }

        if (HasTag(content.Get(tradition).Tags, TraditionSlots.StyleTag) && OtherStyleSlotted(command))
        {
            return CommandResult.Reject(RejectReason.StyleAlreadySlotted, $"Another style card is already slotted; only one style may be active at a time.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetTradition command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        TraditionId? current = TraditionSlots.Find(state.Slotted, command.Slot, command.Index);
        if (current == command.Tradition)
        {
            return;
        }

        if (current is { } old)
        {
            ctx.ProgressionService.RemoveSource(EffectSource.ForTradition(old));
            state.Slotted = TraditionSlots.WithRemoved(state.Slotted, command.Slot, command.Index);
        }

        if (command.Tradition is { } next)
        {
            state.Slotted = TraditionSlots.WithSet(state.Slotted, command.Slot, command.Index, next);
            ctx.ProgressionService.ApplyEffects(EffectSource.ForTradition(next), content.Get(next).Effects);
        }

        ctx.Events.Emit(new TraditionChanged(ctx.Tick, command.Slot, command.Index, current, command.Tradition));
    }

    /// <summary>
    /// Tests whether a style card other than the one being replaced is slotted. The entry the command writes is
    /// ignored: replacing the active style in its own slot leaves exactly one style slotted.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns><see langword="true"/> if another slot holds a style card.</returns>
    private bool OtherStyleSlotted(SetTradition command)
    {
        foreach (SlottedTradition entry in state.Slotted)
        {
            if ((entry.Slot != command.Slot || entry.Index != command.Index)
                && HasTag(content.Get(entry.Tradition).Tags, TraditionSlots.StyleTag))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSlot(TraditionSlotType slot) => (uint)slot <= (uint)TraditionSlotType.Wild;

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

    private string NameOf(TraditionId tradition) =>
        (uint)tradition.Value < (uint)content.Traditions.Count ? content.NameOf(tradition) : tradition.ToString();
}
