using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// Answers whether research has opened a tile for use (the terrain ladder in progression.md). The set of opened ground lives
/// in Progression and is read through <see cref="IProgressionQueries.HasAccess"/>; this helper only joins it to what a tile
/// or a use requires, so Construction and Roads ask the same question the same way.
/// </summary>
internal static class TerrainAccessCheck
{
    /// <summary>Tests whether an access requirement is met.</summary>
    /// <param name="requiredAccess">The access flag the use needs, or <see langword="null"/> when it needs none.</param>
    /// <param name="accessSet">The progression queries, which know which ground is open.</param>
    /// <returns><see langword="true"/> when nothing is required or the required flag is open.</returns>
    public static bool IsUsable(TerrainAccess? requiredAccess, IProgressionQueries accessSet)
    {
        ArgumentNullException.ThrowIfNull(accessSet);
        return requiredAccess is not { } required || accessSet.HasAccess(required);
    }

    /// <summary>Tests whether a tile's terrain is open: always, or once the flag its terrain definition names has been researched.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="content">The content that holds the terrain definitions.</param>
    /// <param name="accessSet">The progression queries.</param>
    /// <returns><see langword="true"/> if the terrain needs no access or its access is open.</returns>
    public static bool IsUsable(in TileInfo tile, ContentDb content, IProgressionQueries accessSet)
    {
        ArgumentNullException.ThrowIfNull(content);
        return IsUsable(content.Get(tile.Terrain).Access, accessSet);
    }

    /// <summary>Gets the access a bridge needs to cross a river of a width class.</summary>
    /// <param name="width">The river width class.</param>
    /// <returns><see cref="TerrainAccess.NarrowRivers"/> or <see cref="TerrainAccess.WideRivers"/>; <see langword="null"/> when there is no river.</returns>
    public static TerrainAccess? BridgeAccess(RiverWidthClass width) => width switch
    {
        RiverWidthClass.Narrow => TerrainAccess.NarrowRivers,
        RiverWidthClass.Wide => TerrainAccess.WideRivers,
        _ => null,
    };
}
