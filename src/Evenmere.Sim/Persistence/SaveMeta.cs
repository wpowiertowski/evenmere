using Evenmere.Content;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// What the caller tells <see cref="SaveGame.Write"/> beyond the simulation itself. The tick, era and population come
/// from the simulation's queries; everything here is what the simulation does not know: the content its IDs refer to
/// (for the string tables), the game's version text and the island the world runs on.
/// </summary>
/// <param name="Content">The loaded content the simulation runs on; its string tables go into the header.</param>
/// <param name="GameVersion">The game's version text, stored in the header for players and support.</param>
/// <param name="MapId">The island ID (a folder under <c>maps/</c>), or <see langword="null"/> for the empty world.</param>
public sealed record SaveMeta(ContentDb Content, string GameVersion, string? MapId);
