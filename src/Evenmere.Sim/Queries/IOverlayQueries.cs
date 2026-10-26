using Evenmere.Content.Definitions;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only overlay layers that make invisible rules visible: storehouse reach, market and service coverage,
/// fertility per crop, deposits and buildable-with-access (M2-20, per ADR-0017). Layers are computed on demand from
/// <see cref="IMapQueries"/>, <see cref="IRoadsQueries"/> and <see cref="IProgressionQueries"/>, cached by tick, and
/// never stored in the world state, so they never change the state hash.
/// </summary>
public interface IOverlayQueries
{
    /// <summary>Gets the layer width in tiles; every layer has <c>Width × Height</c> entries.</summary>
    int Width { get; }

    /// <summary>Gets the layer height in tiles.</summary>
    int Height { get; }

    /// <summary>
    /// Tiles a new building could be supplied from: 1 when a 1 × 1 footprint there would lie in storehouse reach
    /// (some edge-adjacent road tile, or the tile itself, is within a finished storehouse's reach field), else 0.
    /// </summary>
    /// <returns>A copy of the cached layer in row-major order (<c>y * Width + x</c>).</returns>
    byte[] StorehouseReach();

    /// <summary>
    /// Tiles a house could be covered by a market from: 1 when a 1 × 1 house there would be covered by a finished
    /// market that is itself in storehouse reach (ADR-0005), else 0.
    /// </summary>
    /// <returns>A copy of the cached layer in row-major order.</returns>
    byte[] MarketCoverage();

    /// <summary>
    /// Tiles a house could be covered from: 1 when a 1 × 1 house there would be covered by a finished building that
    /// provides <paramref name="service"/>, else 0. Markets additionally need storehouse reach (see
    /// <see cref="MarketCoverage"/>).
    /// </summary>
    /// <param name="service">The service whose coverage to show.</param>
    /// <returns>A copy of the cached layer in row-major order.</returns>
    byte[] ServiceCoverage(ServiceKind service);

    /// <summary>
    /// Tiles fertile for a crop: 1 when the tile's fertility mask holds <paramref name="crop"/>'s bit, else 0.
    /// </summary>
    /// <param name="crop">The crop whose fertility layer to show.</param>
    /// <returns>A copy of the cached layer in row-major order.</returns>
    byte[] Fertility(CropKind crop);

    /// <summary>
    /// Tiles with a mineral deposit: 0 for none, otherwise the <see cref="DepositKind"/> value plus one
    /// (the same encoding the map state stores), so the view can colour each kind.
    /// </summary>
    /// <returns>A copy of the cached layer in row-major order.</returns>
    byte[] Deposits();

    /// <summary>
    /// Tiles a building could stand on: 1 when the terrain is buildable, the slope is within the placement limit,
    /// the terrain's access flag (if any) is unlocked, and the tile is revealed, else 0. Fog is not implemented yet
    /// (M2-06), so every tile counts as revealed until then.
    /// </summary>
    /// <returns>A copy of the cached layer in row-major order.</returns>
    byte[] BuildableWithAccess();
}
