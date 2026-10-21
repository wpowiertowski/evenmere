using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// Handles <see cref="SetCivicQueue"/>: replaces the civics to adopt after the target, in order. An empty queue clears
/// it. Every entry must be reachable: not adopted, not the target, not repeated, its era begun, and every prerequisite
/// adopted, the target, or earlier in the queue.
/// </summary>
/// <param name="state">The culture state to write.</param>
/// <param name="content">The loaded content, for the reachability check.</param>
internal sealed class SetCivicQueueHandler(CultureState state, ContentDb content) : ICommandHandler<SetCivicQueue>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetCivicQueue command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        int eraOrder = EraOrder(ctx);
        for (int i = 0; i < command.Queue.Length; i++)
        {
            switch (CultureMath.CheckQueueEntry(state, content, eraOrder, state.Target, command.Queue, i, i))
            {
                case QueueReachability.Reachable:
                    break;
                case QueueReachability.UnknownOrDone:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: it is unknown or already adopted.");
                case QueueReachability.IsTarget:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: it is the target.");
                case QueueReachability.Duplicate:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued twice.");
                case QueueReachability.EraLocked:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: its era has not begun.");
                default:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: a prerequisite is neither adopted nor earlier in the queue nor the target.");
            }
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetCivicQueue command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        var copy = new CivicId[command.Queue.Length];
        Array.Copy(command.Queue, copy, copy.Length);
        state.Queue = copy;
    }

    private int EraOrder(SimContext ctx)
    {
        EraId era = ctx.Queries.Progression.CurrentEra;
        return (uint)era.Value < (uint)content.Eras.Count ? content.Get(era).Order : 0;
    }
}
