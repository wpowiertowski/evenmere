using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Production.Commands;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// Handles <see cref="SetBuildingPaused"/>. The pause lives in the building registry (ADR-0011): this handler only checks
/// that the target is a finished producer and moves it between <see cref="BuildingState.Active"/> and
/// <see cref="BuildingState.Paused"/>. The production tick and the workforce both read the state from the map, so no
/// system keeps a flag of its own.
/// </summary>
/// <param name="state">The production state, which says which buildings are producers.</param>
internal sealed class SetBuildingPausedHandler(ProductionState state) : ICommandHandler<SetBuildingPaused>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetBuildingPaused command)
    {
        if (!ctx.Queries.Map.TryGetBuilding(command.Building, out BuildingInfo building))
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.Building} is not a building.");
        }

        // Sites and ruins have no producer record, and houses and roads never do.
        if (!building.IsComplete || !state.Producers.Contains(command.Building))
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{command.Building} is not a finished producer, so it cannot be paused.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetBuildingPaused command)
    {
        BuildingState wanted = command.Paused ? BuildingState.Paused : BuildingState.Active;
        if (ctx.Queries.Map.TryGetBuilding(command.Building, out BuildingInfo building) && building.State != wanted)
        {
            ctx.BuildingRegistry.SetState(command.Building, wanted);
        }
    }
}
