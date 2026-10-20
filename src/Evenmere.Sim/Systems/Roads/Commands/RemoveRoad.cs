using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads.Commands;

/// <summary>
/// Clears the road from the given tiles; tiles without a road are skipped. Handled by M1-05.
/// </summary>
/// <remarks>The array is part of the command, so the sender must not change it after enqueueing the command. Two commands with equal tiles are equal.</remarks>
/// <param name="Tiles">The tiles to clear, in any order.</param>
public sealed record RemoveRoad(TileCoord[] Tiles) : Command
{
    /// <inheritdoc />
    public bool Equals(RemoveRoad? other) => other is not null && ValueArrays.Equal(Tiles, other.Tiles);

    /// <inheritdoc />
    public override int GetHashCode() => ValueArrays.Hash(Tiles);
}
