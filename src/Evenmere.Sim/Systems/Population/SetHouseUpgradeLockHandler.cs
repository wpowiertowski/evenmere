using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Population.Commands;

namespace Evenmere.Sim.Systems.Population;

/// <summary>Handles <see cref="SetHouseUpgradeLock"/>: the player locks a house against advancing, or lifts the lock.</summary>
/// <param name="system">The population system that owns the houses.</param>
internal sealed class SetHouseUpgradeLockHandler(PopulationSystem system) : ICommandHandler<SetHouseUpgradeLock>
{
    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetHouseUpgradeLock command)
    {
        if (system.IsHouse(command.House))
        {
            return CommandResult.Ok;
        }

        // An ID the allocator has not handed out yet names nothing, which needs no look at the map. Any other building
        // without a record is "not a house", and that includes a residence that is still a construction site.
        if (command.House.Value < 1 || command.House.Value >= ctx.World.EntityIds.NextValue)
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.House} is not a building.");
        }

        return ctx.Queries.Map.TryGetBuilding(command.House, out _)
            ? CommandResult.Reject(RejectReason.NotApplicable, $"{command.House} is not a house, so it has no upgrades to lock.")
            : CommandResult.Reject(RejectReason.UnknownTarget, $"{command.House} is not a building.");
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetHouseUpgradeLock command) => system.SetUpgradeLock(ctx, command.House, command.Locked);
}
