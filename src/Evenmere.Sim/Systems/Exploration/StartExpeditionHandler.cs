using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// Handles <see cref="StartExpedition"/>: sends a Scout's Lodge to an adjacent fogged region, starting the countdown
/// the daily step ticks down. The lodge is validated by its <c>expedition_lodge</c> tag, never by its content ID, so
/// future lodges work without a code change. (M2-06.)
/// </summary>
internal sealed class StartExpeditionHandler : ICommandHandler<StartExpedition>
{
    /// <summary>The tag a finished building must carry to send expeditions.</summary>
    public const string LodgeTag = "expedition_lodge";

    private readonly ExplorationState _state;
    private readonly MapState _map;

    /// <summary>Initializes a new instance of the <see cref="StartExpeditionHandler"/> class.</summary>
    /// <param name="state">The exploration state.</param>
    /// <param name="map">The map state, which holds the regions.</param>
    public StartExpeditionHandler(ExplorationState state, MapState map)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);

        _state = state;
        _map = map;
    }

    /// <inheritdoc />
    public CommandResult Validate(SimContext ctx, StartExpedition command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        if (!ctx.Queries.Map.TryGetBuilding(command.Lodge, out BuildingInfo building))
        {
            return CommandResult.Reject(RejectReason.UnknownTarget, $"There is no building {command.Lodge.Value}.");
        }

        if (!building.IsComplete)
        {
            return CommandResult.Invalid($"Building {command.Lodge.Value} is not finished, so it cannot send an expedition.");
        }

        BuildingDef def = ctx.Content.Get(building.Type);
        if (!HasTag(def.Tags, LodgeTag))
        {
            return CommandResult.Invalid($"{ctx.Content.NameOf(building.Type)} is not a Scout's Lodge, so it cannot send expeditions.");
        }

        if (command.Region is < 0 or > ushort.MaxValue || !ExpeditionMath.RegionExists(_map, checked((ushort)command.Region)))
        {
            return CommandResult.Invalid($"There is no region {command.Region} on this island.");
        }

        ushort target = checked((ushort)command.Region);
        if (ExpeditionMath.IsRevealedBit(_state.RevealedRegions, target))
        {
            return CommandResult.Invalid($"Region {command.Region} is already revealed.");
        }

        if (_state.Expeditions.Contains(command.Lodge))
        {
            return CommandResult.Invalid($"Building {command.Lodge.Value} already has an expedition under way.");
        }

        if (!ExpeditionMath.BordersRevealed(_map, _state.RevealedRegions, target))
        {
            return CommandResult.Invalid($"Region {command.Region} does not border revealed land.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(SimContext ctx, StartExpedition command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(command);

        ctx.Queries.Map.TryGetBuilding(command.Lodge, out BuildingInfo building);
        ushort region = checked((ushort)command.Region);
        int distance = ExpeditionMath.DistanceToRegion(_map, building.Origin.X, building.Origin.Y, region);
        if (distance < 0)
        {
            distance = 0;
        }

        int average = ExpeditionMath.AverageElevationOf(_map, region);
        int bonus = ctx.Queries.Progression.BonusPercent(Stat.ExpeditionSpeed, ModifierContext.ForBuilding(command.Lodge, building.Type));
        int days = ExpeditionMath.DurationDays(distance, average, bonus);

        // The system ticks expeditions down on day starts, after commands apply: an expedition starting on a
        // day-start tick would lose a day the same tick, so it starts with one extra day which the run consumes
        // at once. Mid-day starts keep the estimate; they reveal on the Nth subsequent day-start.
        if (ctx.Clock.IsDayStart)
        {
            days++;
        }

        _state.Expeditions.Add(command.Lodge, new ExpeditionRecord(region, days, command.Lodge));
    }

    private static bool HasTag(IReadOnlyList<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
