using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// Handles <see cref="SetCivicTarget"/>: chooses the civic that receives the day's culture. Progress already made on
/// other civics is kept. Setting the target to a civic that is already queued promotes it out of the queue, so the
/// target is never in the queue; setting the same target twice is a no-op. A new target prunes the queue in order,
/// keeping only the entries that are still reachable behind it, so no queued civic can lose its prerequisite to a retarget.
/// </summary>
/// <param name="state">The culture state to write.</param>
/// <param name="content">The loaded content, for the availability check.</param>
internal sealed class SetCivicTargetHandler(CultureState state, ContentDb content) : ICommandHandler<SetCivicTarget>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetCivicTarget command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!CultureMath.IsAvailable(state, content, EraOrder(ctx), command.Civic))
        {
            return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Civic} is not available: it is adopted, a prerequisite is missing, or its era has not begun.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetCivicTarget command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        state.Target = command.Civic;
        for (int i = 0; i < state.Queue.Length; i++)
        {
            if (state.Queue[i] == command.Civic)
            {
                var grown = new CivicId[state.Queue.Length - 1];
                Array.Copy(state.Queue, grown, i);
                Array.Copy(state.Queue, i + 1, grown, i, grown.Length - i);
                state.Queue = grown;
                break;
            }
        }

        PruneQueue(ctx);
    }

    /// <summary>
    /// Drops every queued civic the new target left unreachable, in order: each entry is kept only if its prerequisites are
    /// adopted, the new target, or an entry kept before it. Dropping an entry makes its dependents unreachable too, so the
    /// queue stays a chain the day can actually adopt.
    /// </summary>
    /// <param name="ctx">The simulation context, for the current era.</param>
    private void PruneQueue(SimContext ctx)
    {
        int eraOrder = EraOrder(ctx);
        CivicId[] queue = state.Queue;
        int kept = 0;
        for (int i = 0; i < queue.Length; i++)
        {
            // The kept prefix sits at the front of the same array and never passes the entry being read, so compacting in
            // place is safe: queue[kept] is written only after queue[i] has been checked.
            if (CultureMath.CheckQueueEntry(state, content, eraOrder, state.Target, queue, i, kept) == QueueReachability.Reachable)
            {
                queue[kept] = queue[i];
                kept++;
            }
        }

        if (kept != queue.Length)
        {
            var pruned = new CivicId[kept];
            Array.Copy(queue, pruned, kept);
            state.Queue = pruned;
        }
    }

    private int EraOrder(SimContext ctx)
    {
        EraId era = ctx.Queries.Progression.CurrentEra;
        return (uint)era.Value < (uint)content.Eras.Count ? content.Get(era).Order : 0;
    }
}
