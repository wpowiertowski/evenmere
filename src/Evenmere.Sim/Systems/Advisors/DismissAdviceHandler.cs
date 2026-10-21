using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Advisors.Commands;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// Handles <see cref="DismissAdvice"/>: drops the note and suppresses its (kind, subject) for exactly one season, so a
/// still-present problem raises a new record with a new ID only once the season ends. Unknown IDs are rejected with
/// <see cref="RejectReason.UnknownTarget"/>; cleared IDs never come back, so dismissing one twice is rejected too.
/// </summary>
/// <param name="system">The advisors system that owns the records.</param>
internal sealed class DismissAdviceHandler(AdvisorsSystem system) : ICommandHandler<DismissAdvice>
{
    private readonly AdvisorsSystem _system = system;

    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, DismissAdvice command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        return _system.HasAdvice(command.AdviceId)
            ? CommandResult.Ok
            : CommandResult.Reject(RejectReason.UnknownTarget, $"No advice has ID {command.AdviceId}.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, DismissAdvice command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        _system.Dismiss(ctx, command.AdviceId);
    }
}
