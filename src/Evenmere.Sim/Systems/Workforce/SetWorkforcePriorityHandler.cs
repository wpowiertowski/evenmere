using Evenmere.Content;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// Handles <see cref="SetWorkforcePriority"/>: marks a workplace as high priority so the daily allocation fills it
/// before the others. The flag lives in the workforce state, so it survives save/load; only removing the building
/// clears it.
/// </summary>
internal sealed class SetWorkforcePriorityHandler(WorkforceState state, ContentDb content) : ICommandHandler<SetWorkforcePriority>
{
    private readonly WorkforceState _state = state;
    private readonly ContentDb _content = content;

    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, SetWorkforcePriority command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!ctx.Queries.Map.TryGetBuilding(command.Building, out Queries.Types.BuildingInfo building))
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"{command.Building} is not a building.");
        }

        if (_content.Get(building.Type).Workforce is null)
        {
            return CommandResult.Reject(RejectReason.NotApplicable, $"{command.Building} needs no workers, so it has no priority.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, SetWorkforcePriority command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!_state.Workplaces.TryGet(command.Building, out WorkplaceRecord? record))
        {
            record = new WorkplaceRecord();
            _state.Workplaces.Add(command.Building, record);
        }

        record.HighPriority = command.High;
    }
}
