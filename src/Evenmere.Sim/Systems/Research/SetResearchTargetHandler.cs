using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// Handles <see cref="SetResearchTarget"/>: chooses the tech that receives the day's knowledge. Progress already made on
/// other techs is kept. Setting the target to a tech that is already queued promotes it out of the queue, so the target
/// is never in the queue; setting the same target twice is a no-op. A new target prunes the queue in order, keeping only
/// the entries that are still reachable behind it, so no queued tech can lose its prerequisite to a retarget.
/// </summary>
/// <param name="state">The research state to write.</param>
/// <param name="content">The loaded content, for the availability check.</param>
internal sealed class SetResearchTargetHandler(ResearchState state, ContentDb content) : ICommandHandler<SetResearchTarget>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetResearchTarget command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!ResearchMath.IsAvailable(state, content, EraOrder(ctx), command.Tech))
        {
            return CommandResult.Reject(RejectReason.NodeNotAvailable, $"{command.Tech} is not available: it is done, a prerequisite is missing, or its era has not begun.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetResearchTarget command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        state.Target = command.Tech;
        for (int i = 0; i < state.Queue.Length; i++)
        {
            if (state.Queue[i] == command.Tech)
            {
                var grown = new TechId[state.Queue.Length - 1];
                Array.Copy(state.Queue, grown, i);
                Array.Copy(state.Queue, i + 1, grown, i, grown.Length - i);
                state.Queue = grown;
                break;
            }
        }

        PruneQueue(ctx);
    }

    /// <summary>
    /// Drops every queued tech the new target left unreachable, in order: each entry is kept only if its prerequisites are
    /// done, the new target, or an entry kept before it. Dropping an entry makes its dependents unreachable too, so the
    /// queue stays a chain the day can actually research.
    /// </summary>
    /// <param name="ctx">The simulation context, for the current era.</param>
    private void PruneQueue(SimContext ctx)
    {
        int eraOrder = EraOrder(ctx);
        TechId[] queue = state.Queue;
        int kept = 0;
        for (int i = 0; i < queue.Length; i++)
        {
            // The kept prefix sits at the front of the same array and never passes the entry being read, so compacting in
            // place is safe: queue[kept] is written only after queue[i] has been checked.
            if (ResearchMath.CheckQueueEntry(state, content, eraOrder, state.Target, queue, i, kept) == QueueReachability.Reachable)
            {
                queue[kept] = queue[i];
                kept++;
            }
        }

        if (kept != queue.Length)
        {
            var pruned = new TechId[kept];
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
