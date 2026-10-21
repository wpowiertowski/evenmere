using Evenmere.Content.Definitions;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Systems.Construction.Handlers;

/// <summary>
/// Handles <see cref="UpgradeBuilding"/>: starts an in-place upgrade site for a finished building whose type is the
/// <c>upgradeOf</c> of the requested one. The old building keeps its state and keeps working while the site draws the new
/// type's bill and does its build time; on completion the building changes type in place (the registry raises
/// <c>OnTypeChanged</c>) and <see cref="BuildingUpgraded"/> is emitted. Houses are refused, because their tiers are
/// Population's job.
/// </summary>
/// <param name="state">The construction state.</param>
internal sealed class UpgradeBuildingHandler(ConstructionState state) : ICommandHandler<UpgradeBuilding>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, UpgradeBuilding command)
    {
        if (!ctx.Queries.Map.TryGetBuilding(command.Building, out BuildingInfo building))
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.Building} is not a building.");
        }

        var placement = BuildingPlacement.For(ctx, state);
        if (!placement.IsKnown(command.ToType))
        {
            return CommandResult.Invalid($"Building type {command.ToType.Value} is not in the content.");
        }

        if (!building.IsComplete)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{command.Building} is not a finished building, so it cannot be upgraded.");
        }

        BuildingDef from = ctx.Content.Get(building.Type);
        BuildingDef to = ctx.Content.Get(command.ToType);
        string fromName = ctx.Content.NameOf(building.Type);
        string toName = ctx.Content.NameOf(command.ToType);
        if (to.UpgradeOf != building.Type)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{toName} is not an upgrade of {fromName}.");
        }

        if (from.Category == BuildingCategory.Residence)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{fromName} is a house; Population changes house tiers as residents advance.");
        }

        // The registry changes a type in place, so the footprint must stay the same size as it stands rotated.
        Footprint rotated = building.Rotation.Rotate(to.Footprint);
        if (rotated.Width != building.Width || rotated.Height != building.Height)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{toName} has a different footprint from {fromName}, so it cannot replace it in place.");
        }

        if (state.Sites.Contains(command.Building))
        {
            return CommandResult.Invalid($"{command.Building} is already being upgraded.");
        }

        if (!ctx.Queries.Progression.IsUnlocked(command.ToType))
        {
            return CommandResult.Reject(RejectReason.PlacementInvalid, $"{toName} cannot be built: {PlacementIssue.NotUnlocked}.");
        }

        if (to.Unique && placement.AlreadyExists(command.ToType))
        {
            return CommandResult.Reject(RejectReason.PlacementInvalid, $"{toName} cannot be built: {PlacementIssue.UniqueAlreadyBuilt}.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, UpgradeBuilding command) =>
        state.Sites.Add(command.Building, new SiteRecord { Kind = SiteKind.Upgrade, Target = command.ToType, PlacedTick = ctx.Tick });
}
