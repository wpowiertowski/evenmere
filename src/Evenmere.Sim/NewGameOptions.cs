using Evenmere.Content.Maps;

namespace Evenmere.Sim;

/// <summary>The choices that define a new world: the seed, and the island it starts on.</summary>
public sealed record NewGameOptions
{
    /// <summary>Gets the world seed. Every system's random stream is derived from it, so one seed gives one world.</summary>
    public ulong Seed { get; init; }

    /// <summary>
    /// Gets the loaded map, or <see langword="null"/> for the empty world that the test harness uses. Callers that pass a
    /// map load it with <c>MapLoader.Load</c> (the simulation itself does no IO): the scenario runner, the CLI and the game
    /// host. A null map skips all map setup: no terrain, no start unlocks, no Founders' Hall and no start stock.
    /// </summary>
    public MapDef? Map { get; init; }
}
