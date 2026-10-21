using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads.Commands;

/// <summary>
/// Handles <see cref="PlaceRoad"/>: lays the valid prefix of the path. Validation and application run the same check as
/// <c>IPlacementQueries.ValidateRoad</c> (<see cref="RoadPlacement.Evaluate"/>), so the ghost preview and the command agree.
/// </summary>
internal sealed class PlaceRoadHandler(RoadNetwork network, RoadPlacement placement, ContentDb content) : ICommandHandler<PlaceRoad>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, PlaceRoad command)
    {
        if (command.Path is null || command.Path.Length == 0)
        {
            return CommandResult.Invalid("The road path has no tiles.");
        }

        // Settled from the content alone, before the map is consulted, so a command that names something that is not a road
        // is refused the same way whether or not there is a map.
        if (!network.IsRoadType(command.RoadType))
        {
            return CommandResult.Invalid($"{NameOf(command.RoadType)} is not a road type.");
        }

        var issues = new PlacementIssue[command.Path.Length];
        return placement.Evaluate(command.Path, command.RoadType, issues) > 0
            ? CommandResult.Ok
            : CommandResult.Reject(RejectReason.PlacementInvalid, $"{issues[0]} at {command.Path[0]}.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, PlaceRoad command)
    {
        var issues = new PlacementIssue[command.Path.Length];
        int prefix = placement.Evaluate(command.Path, command.RoadType, issues);
        network.PlaceRoads(ctx, command.Path.AsSpan(0, prefix), command.RoadType);
    }

    private string NameOf(BuildingTypeId type) =>
        type.Value < content.Buildings.Count ? content.Buildings.NameOf(type) : type.ToString();
}
