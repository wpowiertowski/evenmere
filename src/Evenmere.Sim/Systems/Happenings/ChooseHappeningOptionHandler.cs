using Evenmere.Content.Definitions;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>
/// Handles <see cref="Commands.ChooseHappeningOption"/>: answers the pending happening, applying the option's
/// effects through <see cref="HappeningsSystem.Resolve"/> and emitting <see cref="HappeningResolved"/>.
/// Choosing the same option twice is impossible (the first choice clears the pending slot); a repeat occurrence of
/// the same happening resolves cleanly because the resolve clears the old source first. (M2-12.)
/// </summary>
/// <param name="state">The happenings state to write.</param>
internal sealed class ChooseHappeningOptionHandler(HappeningsState state) : ICommandHandler<Commands.ChooseHappeningOption>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, Commands.ChooseHappeningOption command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if ((uint)command.Happening.Value >= (uint)ctx.Content.Happenings.Count)
        {
            return CommandResult.Invalid($"There is no happening {command.Happening}.");
        }

        if (state.Pending is not { } pending)
        {
            return CommandResult.Invalid("There is no pending happening to answer.");
        }

        if (pending.Happening != command.Happening)
        {
            return CommandResult.Invalid($"The pending happening is {ctx.Content.NameOf(pending.Happening)}, not {ctx.Content.NameOf(command.Happening)}.");
        }

        HappeningDef def = ctx.Content.Get(command.Happening);
        if (command.OptionIndex < 0 || (uint)command.OptionIndex >= (uint)def.Options.Length)
        {
            return CommandResult.Invalid($"Happening {ctx.Content.NameOf(command.Happening)} has no option {command.OptionIndex}.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, Commands.ChooseHappeningOption command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        HappeningsSystem.Resolve(state, ctx, command.Happening, command.OptionIndex);
    }
}
