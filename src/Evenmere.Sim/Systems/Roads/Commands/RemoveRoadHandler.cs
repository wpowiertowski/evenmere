using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Systems.Roads.Commands;

/// <summary>
/// Handles <see cref="RemoveRoad"/>: clears the road from the listed tiles. Tiles without a road are skipped, but a command
/// that names no road at all has nothing to do and is refused as <see cref="RejectReason.UnknownTarget"/>, like any command
/// that names something that does not exist.
/// </summary>
internal sealed class RemoveRoadHandler(RoadNetwork network) : ICommandHandler<RemoveRoad>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, RemoveRoad command)
    {
        if (command.Tiles is null)
        {
            return CommandResult.Invalid("The tile list is missing.");
        }

        return network.HasAnyRoad(command.Tiles)
            ? CommandResult.Ok
            : CommandResult.Reject(RejectReason.UnknownTarget, "None of the listed tiles has a road.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, RemoveRoad command) => network.RemoveRoads(ctx, command.Tiles);
}
