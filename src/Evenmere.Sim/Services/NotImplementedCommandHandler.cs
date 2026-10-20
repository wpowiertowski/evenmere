using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Services;

/// <summary>
/// The placeholder handler M1-00 registers for every M1 command, so each command already has exactly one handler (the
/// architecture guard demands it) and a player or script that sends one gets a polite refusal instead of a crash. The task
/// that owns the command replaces the registration with its real handler; nothing else changes.
/// </summary>
/// <typeparam name="TCommand">The command type this handler stands in for.</typeparam>
internal sealed class NotImplementedCommandHandler<TCommand>(string task) : ICommandHandler<TCommand>
    where TCommand : Command
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, TCommand command) =>
        CommandResult.Reject(RejectReason.NotImplemented, $"{typeof(TCommand).Name} is not implemented yet; task {task} adds its handler.");

    /// <inheritdoc />
    public void Apply(SimContext ctx, TCommand command) =>
        throw new InvalidOperationException($"{typeof(TCommand).Name} reached Apply, but its placeholder handler always rejects it in Validate.");
}
