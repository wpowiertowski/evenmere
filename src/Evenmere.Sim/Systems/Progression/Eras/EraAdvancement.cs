using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Systems.Progression.Eras;

/// <summary>
/// The era check: when both gateways of the next era are complete, the island moves into it. Called from
/// <see cref="ProgressionSystem.Run"/> on its daily step (step 12), after research and culture have completed
/// their nodes that day, so the era begins the same tick the second gateway completes. (M2-11, per ADR-0010.)
/// </summary>
/// <remarks>
/// Completion is read from the research and culture done sets, the same record the availability gate reads through the
/// current era. At most one era begins per check; a day later the check runs again, so the island converges instead of
/// skipping eras silently. The advisor hook (<see cref="Queries.IProgressionQueries.GatewayStatus"/>) answers from the
/// applied effect sources, which research and culture write exactly once per completion, so the two agree in every game.
/// </remarks>
internal static class EraAdvancement
{
    /// <summary>
    /// Moves the island into the next era when both of its gateways are complete, and announces it.
    /// </summary>
    /// <param name="state">The progression state, whose era moves forward at most one step.</param>
    /// <param name="ctx">The simulation context, which supplies the content, the completion sets and the event sink.</param>
    internal static void Check(ProgressionState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);

        ContentDb content = ctx.Content;
        if (content.Eras.Count == 0 || (uint)state.Era >= (uint)content.Eras.Count)
        {
            return;
        }

        int order = content.Get(EraId.From(checked((ushort)state.Era))).Order;
        EraDef? next = null;
        foreach (EraId id in content.ErasByOrder)
        {
            EraDef candidate = content.Get(id);
            if (candidate.Order > order)
            {
                next = candidate;
                break;
            }
        }

        // No later era, or a later era without both gateways named (lint EVL008 requires both on real content).
        if (next is not { RequiresTech: { } tech, RequiresCivic: { } civic })
        {
            return;
        }

        if (!ctx.Queries.Research.IsDone(tech) || !ctx.Queries.Culture.IsDone(civic))
        {
            return;
        }

        state.Era = next.Id.Value;
        ctx.Events.Emit(new EraBegan(ctx.Tick, next.Id));
    }
}
