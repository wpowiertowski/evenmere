using Evenmere.Content.Definitions;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Systems.Construction.Handlers;

/// <summary>
/// Handles <see cref="Demolish"/>. A site (or the upgrade under way on a finished building) refunds 100% of the materials it
/// has drawn; an active or paused building refunds half of its bill, rounded down to the milli-unit per good. The
/// Founders' Hall, which is the one building that is both <c>unique</c> and available from the <c>start</c>, is refused
/// with <see cref="RejectReason.Protected"/>. The building is then removed through the registry, which tells the lifecycle
/// listeners, and <see cref="BuildingDemolished"/> is emitted.
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
            foreach (GoodAmount line in ctx.Content.Get(site.Target).Cost.Goods)
            {
                ctx.Storage.Add(line.Good, SiteMath.DrawnAfter(line.Amount, site.StepsDrawn));
            }

            state.Sites.Remove(command.Building);
        }

        // The finished part: half of what the building itself cost.
        if (building.IsComplete)
        {
            foreach (GoodAmount line in ctx.Content.Get(building.Type).Cost.Goods)
            {
                ctx.Storage.Add(line.Good, SiteMath.DemolishRefund(line.Amount));
            }
        }

        ctx.BuildingRegistry.Remove(command.Building);
        ctx.Events.Emit(new BuildingDemolished(ctx.Tick, building.Id, building.Type, building.Origin));
    }
}
