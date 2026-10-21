using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// Handles <see cref="SetResearchQueue"/>: replaces the techs to research after the target, in order. An empty queue
/// clears it. Every entry must be reachable: not done, not the target, not repeated, its era begun, and every
/// prerequisite done, the target, or earlier in the queue.
/// </summary>
/// <param name="state">The research state to write.</param>
/// <param name="content">The loaded content, for the reachability check.</param>
internal sealed class SetResearchQueueHandler(ResearchState state, ContentDb content) : ICommandHandler<SetResearchQueue>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetResearchQueue command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        int eraOrder = EraOrder(ctx);
        for (int i = 0; i < command.Queue.Length; i++)
        {
            switch (ResearchMath.CheckQueueEntry(state, content, eraOrder, state.Target, command.Queue, i, i))
            {
                case QueueReachability.Reachable:
                    break;
                case QueueReachability.UnknownOrDone:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: it is unknown or already researched.");
                case QueueReachability.IsTarget:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: it is the target.");
                case QueueReachability.Duplicate:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued twice.");
                case QueueReachability.EraLocked:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: its era has not begun.");
                default:
                    return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Queue[i]} cannot be queued: a prerequisite is neither done nor earlier in the queue nor the target.");
            }
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetResearchQueue command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        var copy = new TechId[command.Queue.Length];
        Array.Copy(command.Queue, copy, copy.Length);
        state.Queue = copy;
    }

    private int EraOrder(SimContext ctx)
    {
        EraId era = ctx.Queries.Progression.CurrentEra;
        return (uint)era.Value < (uint)content.Eras.Count ? content.Get(era).Order : 0;
    }
}
