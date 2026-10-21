using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.NewGame;

/// <summary>
/// What <see cref="Simulation.CreateNew"/> does after the empty world exists, when the options carry a map: load the
/// island and settle it, deterministically, before tick 0. A null map skips all of this and leaves the empty world the
/// test harness uses.
/// </summary>
/// <remarks>
/// The order is load-bearing. The roster already subscribed every lifecycle listener in pipeline order when it was built,
/// so everything from here on notifies in that order: the map first (its tiles exist before anything stands on them),
/// then the start unlocks (placement checks read them), then the Founders' Hall as an active building through the
/// registry (so the listeners learn of it), then the start stock. Research and civic targets stay none: a fresh
/// <c>ResearchState</c> and <c>CultureState</c> already hold no target and an empty queue.
/// </remarks>
internal static class NewGameSetup
{
    /// <summary>Runs the new-game setup over a fresh world.</summary>
    /// <param name="context">The simulation's context, already bound to its services.</param>
    /// <param name="map">The map service, which loads the island into the map state.</param>
    /// <param name="progression">The progression system, which applies the start unlocks.</param>
    /// <param name="mapDef">The loaded map.</param>
    /// <exception cref="ArgumentException">The map was loaded against different content, or the start site is not a valid rotation.</exception>
    /// <exception cref="InvalidOperationException">The world already holds buildings.</exception>
    internal static void Run(SimContext context, MapSystem map, ProgressionSystem progression, MapDef mapDef)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(mapDef);

        map.LoadMap(mapDef);
        progression.ApplyStartUnlocks(context);

        if (!IsValidRotation(mapDef.StartSite.Rotation))
        {
            throw new ArgumentException(
                $"The start site of map '{mapDef.Id}' has rotation {mapDef.StartSite.Rotation}; a rotation is 0 to 3 quarter turns.",
                nameof(mapDef));
        }

        var rotation = (Rotation)mapDef.StartSite.Rotation;
        ConstructionSystem.PlacePreBuilt(
            context, context.Content.Required.FoundersHall, mapDef.StartSite.X, mapDef.StartSite.Y, rotation);

        foreach (GoodAmount line in mapDef.StartStock)
        {
            context.Storage.Add(line.Good, line.Amount);
        }
    }

    private static bool IsValidRotation(int rotation) => rotation is >= 0 and <= 3;
}
