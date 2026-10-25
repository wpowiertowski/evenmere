using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Systems.Construction.Handlers;

/// <summary>
/// Handles <see cref="Demolish"/>. A site (or the upgrade under way on a finished building) refunds 100% of the materials it
/// has drawn; an active or paused building refunds half of its bill, rounded down to the milli-unit per good. A monument
/// site refunds the finished stages in full plus what the current stage drew (all scaled by <c>materialCost</c>), and a
/// finished monument refunds half of its final stage and its island-wide effects are removed via
/// <c>RemoveSource</c>. The Founders' Hall, which is the one building that is both <c>unique</c> and available from the
/// <c>start</c>, is refused with <see cref="RejectReason.Protected"/>. The building is then removed through the registry,
/// which tells the lifecycle listeners, and <see cref="BuildingDemolished"/> is emitted.
/// </summary>
/// <param name="state">The construction state.</param>
internal sealed class DemolishHandler(ConstructionState state) : ICommandHandler<Demolish>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, Demolish command)
    {
        if (!ctx.Queries.Map.TryGetBuilding(command.Building, out BuildingInfo building))
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.Building} is not a building.");
        }

        // A ruin's type is gone from the content, so there is no definition to ask; it can always be cleared away.
        if (building.State != BuildingState.Ruin && SiteMath.IsProtected(ctx.Content.Get(building.Type)))
        {
            return CommandResult.Reject(RejectReason.Protected, $"{ctx.Content.NameOf(building.Type)} cannot be demolished.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, Demolish command)
    {
        ctx.Queries.Map.TryGetBuilding(command.Building, out BuildingInfo building);

        // The site part: everything the site drew is returned in full, whether it is a new building or an upgrade.
        if (state.Sites.TryGet(command.Building, out SiteRecord site))
        {
            RefundDrawn(ctx, command.Building, site);
            state.Sites.Remove(command.Building);
        }

        // The finished part: half of what the building itself cost; a finished monument also loses its effects.
        if (building.IsComplete)
        {
            RefundFinished(ctx, building);
            if (ctx.Content.TryGetMonumentOf(building.Type, out MonumentId monument))
            {
                ctx.ProgressionService.RemoveSource(EffectSource.ForMonument(monument));
            }
        }

        ctx.BuildingRegistry.Remove(command.Building);
        ctx.Events.Emit(new BuildingDemolished(ctx.Tick, building.Id, building.Type, building.Origin));
    }

    // Returns what a site drew: the finished monument stages in full plus what the current stage drew (all scaled by
    // materialCost), or what an ordinary site drew from its bill.
    private static void RefundDrawn(SimContext ctx, EntityId building, SiteRecord site)
    {
        if (site.Kind == SiteKind.Build && ctx.Content.TryGetMonumentOf(site.Target, out MonumentId monument))
        {
            MonumentDef def = ctx.Content.Get(monument);
            int materialBonus = ctx.Queries.Progression.BonusPercent(
                Stat.MaterialCost, ModifierContext.ForBuilding(building, site.Target));
            for (int finished = 0; finished < site.StageIndex; finished++)
            {
                foreach (GoodAmount line in def.Stages[finished].Goods)
                {
                    ctx.Storage.Add(line.Good, MonumentStages.Scale(line.Amount, materialBonus));
                }
            }

            foreach (GoodAmount line in def.Stages[site.StageIndex].Goods)
            {
                ctx.Storage.Add(line.Good, MonumentStages.DrawnAfter(line.Amount, site.StepsDrawn, materialBonus));
            }

            return;
        }

        foreach (GoodAmount line in ctx.Content.Get(site.Target).Cost.Goods)
        {
            ctx.Storage.Add(line.Good, SiteMath.DrawnAfter(line.Amount, site.StepsDrawn));
        }
    }

    // Returns half of what a finished building cost: the scaled final stage for a monument, the bill otherwise.
    private static void RefundFinished(SimContext ctx, BuildingInfo building)
    {
        if (ctx.Content.TryGetMonumentOf(building.Type, out MonumentId monument))
        {
            MonumentDef def = ctx.Content.Get(monument);
            CostSpec last = def.Stages[^1];
            int materialBonus = ctx.Queries.Progression.BonusPercent(
                Stat.MaterialCost, ModifierContext.ForBuilding(building.Id, building.Type));
            foreach (GoodAmount line in last.Goods)
            {
                ctx.Storage.Add(line.Good, MonumentStages.FinalStageRefund(line.Amount, materialBonus));
            }

            return;
        }

        foreach (GoodAmount line in ctx.Content.Get(building.Type).Cost.Goods)
        {
            ctx.Storage.Add(line.Good, SiteMath.DemolishRefund(line.Amount));
        }
    }
}
