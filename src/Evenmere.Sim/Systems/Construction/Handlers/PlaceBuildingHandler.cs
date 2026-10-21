using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Systems.Construction.Handlers;

/// <summary>
/// Handles <see cref="PlaceBuilding"/>: runs the same <see cref="BuildingPlacement"/> check as the ghost preview, then
/// registers a construction site and emits <see cref="BuildingPlaced"/>. The site draws its materials as it is built, so
/// placing needs no stock in the pool.
/// </summary>
/// <param name="state">The construction state.</param>
internal sealed class PlaceBuildingHandler(ConstructionState state) : ICommandHandler<PlaceBuilding>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, PlaceBuilding command)
    {
        var placement = BuildingPlacement.For(ctx, state);
        if (!placement.IsKnown(command.Type))
        {
            return CommandResult.Invalid($"Building type {command.Type.Value} is not in the content.");
        }

        if (!command.Rotation.IsValid())
        {
            return CommandResult.Invalid($"Rotation {(byte)command.Rotation} is not 0 to 3 quarter turns.");
        }

        BuildingDef def = ctx.Content.Get(command.Type);
        string name = ctx.Content.NameOf(command.Type);
        if (def.RoadCost is not null)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{name} is a road type; roads are placed with PlaceRoad.");
        }

        PlacementResult result = placement.Validate(command.Type, command.X, command.Y, command.Rotation);
        if (result.CanPlace)
        {
            return CommandResult.Ok;
        }

        PlacementFinding first = result.Errors[0];
        string tiles = first.Tiles.IsDefaultOrEmpty ? string.Empty : $" at {first.Tiles[0]}{(first.Tiles.Length > 1 ? $" and {first.Tiles.Length - 1} more" : string.Empty)}";
        return CommandResult.Reject(
            RejectReason.PlacementInvalid,
            $"{name} cannot be placed at ({command.X}, {command.Y}): {first.Issue}{tiles}.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, PlaceBuilding command)
    {
        var origin = new TileCoord(command.X, command.Y);
        EntityId id = ctx.BuildingRegistry.Add(command.Type, command.X, command.Y, command.Rotation, BuildingState.Site);
        state.Sites.Add(id, new SiteRecord { Kind = SiteKind.Build, Target = command.Type, PlacedTick = ctx.Tick });
        ctx.Events.Emit(new BuildingPlaced(ctx.Tick, id, command.Type, origin, command.Rotation));
    }
}
