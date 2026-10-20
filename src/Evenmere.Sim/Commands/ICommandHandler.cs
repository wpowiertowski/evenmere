using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Commands;

/// <summary>
/// Handles one kind of command. A system registers its handlers from <see cref="ISimSystem.Register"/>. At the start
/// of a tick the router calls <see cref="Validate"/> and, only if that accepts, <see cref="Apply"/>, both against the
/// current state, so an earlier command in the same batch can change what a later one sees.
/// </summary>
/// <typeparam name="TCommand">The exact command type this handler serves.</typeparam>
public interface ICommandHandler<TCommand>
    where TCommand : Command
{
    /// <summary>Checks the command against the current state without changing anything.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="command">The command to check.</param>
    /// <returns><see cref="CommandResult.Ok"/>, or a rejection that becomes a <see cref="CommandRejected"/> event.</returns>
    CommandResult Validate(SimContext ctx, TCommand command);

    /// <summary>Carries out a command that <see cref="Validate"/> accepted.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="command">The command to apply.</param>
    void Apply(SimContext ctx, TCommand command);
}
