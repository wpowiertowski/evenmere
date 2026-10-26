using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Roads;

namespace Evenmere.Sim.Systems.Overlays;

/// <summary>
/// Computes the <see cref="IOverlayQueries"/> layers on demand from the map, road and progression queries and caches
/// them by tick. Nothing is stored in the world state and nothing here runs in the tick pipeline, so layers never
/// change the state hash. Tile layers (fertility, deposits, buildable) read the map directly. Reach and coverage
/// layers expand the road network's distance fields by one tile, which is exactly the 1 × 1 footprint rule behind
/// <see cref="RoadNetwork.IsFootprintInStorehouseReach"/> and <see cref="RoadNetwork.IsCovered"/>: a tile reads 1
/// exactly when a 1 × 1 building there would be supplied or covered. The lit road network with its halo is the honest
/// picture: every road tile beside an in-reach road is itself a supplied footprint.
/// </summary>
internal sealed class OverlayQueries : IOverlayQueries
{
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;
    private readonly RoadNetwork _network;
    private readonly Func<long> _tick;
    private readonly int _serviceCount;
    private readonly int _cropCount;
    private readonly int _layerCount;
    private readonly long[] _ticks;
    private readonly int[] _epochs;
    private readonly int[] _widths;
    private readonly int[] _heights;
    private readonly byte[][] _layers;

    /// <summary>Initializes a new instance of the <see cref="OverlayQueries"/> class.</summary>
    /// <param name="content">The immutable content.</param>
    /// <param name="queries">A late-bound accessor for the query facade, called only when a layer is read.</param>
    /// <param name="network">The road network, whose distance fields back the reach and coverage layers.</param>
    /// <param name="tick">The current tick, for cache keys.</param>
    public OverlayQueries(ContentDb content, Func<ISimQueries> queries, RoadNetwork network, Func<long> tick)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(tick);
        _content = content;
        _queries = queries;
        _network = network;
        _tick = tick;
        _serviceCount = Enum.GetValues<ServiceKind>().Length;
        _cropCount = Enum.GetValues<CropKind>().Length;

        // Reach, market, one per service, one per crop, deposits, buildable.
        _layerCount = 2 + _serviceCount + _cropCount + 2;
        _ticks = new long[_layerCount];
        _epochs = new int[_layerCount];
        _widths = new int[_layerCount];
        _heights = new int[_layerCount];
        _layers = new byte[_layerCount][];
        for (int i = 0; i < _layerCount; i++)
        {
            _ticks[i] = -1;
            _layers[i] = [];
        }
    }

    /// <inheritdoc />
    public int Width => _queries().Map.Width;

    /// <inheritdoc />
    public int Height => _queries().Map.Height;

    /// <inheritdoc />
    public byte[] StorehouseReach() => CopyOf(0, static (q, layer) => q.FillReach(layer));

    /// <inheritdoc />
    public byte[] MarketCoverage() => CopyOf(1, static (q, layer) => q.FillCoverage(layer, ServiceKind.Market));

    /// <inheritdoc />
    public byte[] ServiceCoverage(ServiceKind service)
    {
        if ((uint)service >= (uint)_serviceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "The service is not a known service kind.");
        }

        // Markets share the market layer's slot, so both spellings cache once.
        int index = service == ServiceKind.Market ? 1 : 2 + (int)service;
        return CopyOf(index, static (q, layer) => q.FillCoverage(layer, layer.Index == 1 ? ServiceKind.Market : (ServiceKind)(layer.Index - 2)));
    }

    /// <inheritdoc />
    public byte[] Fertility(CropKind crop)
    {
        if ((uint)crop >= (uint)_cropCount)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), crop, "The crop is not a known crop kind.");
        }

        int index = 2 + _serviceCount + (int)crop;
        return CopyOf(index, static (q, layer) => q.FillFertility(layer, (CropKind)(layer.Index - 2 - q._serviceCount)));
    }

    /// <inheritdoc />
    public byte[] Deposits() => CopyOf(2 + _serviceCount + _cropCount, static (q, layer) => q.FillDeposits(layer));

    /// <inheritdoc />
    public byte[] BuildableWithAccess() => CopyOf(2 + _serviceCount + _cropCount + 1, static (q, layer) => q.FillBuildable(layer));

    // Returns a copy of the cached layer, rebuilding it when the tick, the road builds or the map size moved on.
    // Reach and coverage layers also key on the network's build count, so a road change without a tick (tests send
    // commands without ticking) still rebuilds.
    private byte[] CopyOf(int index, Action<OverlayQueries, CachedLayer> fill)
    {
        ISimQueries queries = _queries();
        int width = queries.Map.Width;
        int height = queries.Map.Height;
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        long tick = _tick();
        _network.EnsureReady();
        int epoch = _network.BuildCount;

        if (_ticks[index] != tick || _epochs[index] != epoch || _widths[index] != width || _heights[index] != height)
        {
            var layer = new CachedLayer(index, new byte[width * height], width, height);
            fill(this, layer);
            _layers[index] = layer.Bytes;
            _ticks[index] = tick;
            _epochs[index] = epoch;
            _widths[index] = width;
            _heights[index] = height;
        }

        return (byte[])_layers[index].Clone();
    }

    // The layer being filled, so the fill callbacks stay free of closures.
    private sealed class CachedLayer(int index, byte[] bytes, int width, int height)
    {
        /// <summary>Gets the cache index of the layer.</summary>
        public int Index { get; } = index;

        /// <summary>Gets the bytes being filled, in row-major order.</summary>
        public byte[] Bytes { get; } = bytes;

        /// <summary>Gets the layer width in tiles.</summary>
        public int Width { get; } = width;

        /// <summary>Gets the layer height in tiles.</summary>
        public int Height { get; } = height;
    }

    private void FillReach(CachedLayer layer)
    {
        ISimQueries queries = _queries();
        foreach (BuildingInfo building in queries.Map.ActiveBuildings)
        {
            if (building.Type.Value >= _content.Buildings.Count)
            {
                continue;
            }

            int reach = _content.Get(building.Type).Storage?.Reach ?? 0;
            if (reach <= 0)
            {
                continue;
            }

            if (!_network.TryGetFieldEntries(building.Id, out int[] tiles, out int[] costs))
            {
                continue;
            }

            MarkField(tiles, costs, checked(reach * 1000), layer);
        }
    }

    private void FillCoverage(CachedLayer layer, ServiceKind service)
    {
        ISimQueries queries = _queries();
        foreach (BuildingInfo building in queries.Map.ActiveBuildings)
        {
            if (building.Type.Value >= _content.Buildings.Count)
            {
                continue;
            }

            ServiceSpec? spec = _content.Get(building.Type).Service;
            if (spec is not { } provided || provided.Kind != service || provided.Coverage <= 0)
            {
                continue;
            }

            // ADR-0005: a market cut off from every storehouse covers nobody.
            if (service == ServiceKind.Market && !_network.TryNearestStorehouse(building.Id, out _, out _))
            {
                continue;
            }

            if (!_network.TryGetFieldEntries(building.Id, out int[] tiles, out int[] costs))
            {
                continue;
            }

            MarkField(tiles, costs, checked(provided.Coverage * 1000), layer);
        }
    }

    // Marks the edge neighbours of the field's road tiles within the bound: those neighbours are exactly the 1 × 1
    // footprints whose access tiles reach. Field tiles are never marked themselves, so an isolated in-reach road with
    // no in-reach neighbour reads 0, exactly as its footprint would.
    private static void MarkField(int[] tiles, int[] costs, int bound, CachedLayer layer)
    {
        int width = layer.Width;
        int height = layer.Height;
        byte[] bytes = layer.Bytes;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (costs[i] > bound)
            {
                continue;
            }

            int tile = tiles[i];
            int x = tile % width;
            int y = tile / width;
            if (x > 0)
            {
                bytes[tile - 1] = 1;
            }

            if (x < width - 1)
            {
                bytes[tile + 1] = 1;
            }

            if (y > 0)
            {
                bytes[tile - width] = 1;
            }

            if (y < height - 1)
            {
                bytes[tile + width] = 1;
            }
        }
    }

    private void FillFertility(CachedLayer layer, CropKind crop)
    {
        IMapQueries map = _queries().Map;
        byte[] bytes = layer.Bytes;
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                bytes[(y * layer.Width) + x] = map.GetTile(x, y).IsFertileFor(crop) ? (byte)1 : (byte)0;
            }
        }
    }

    private void FillDeposits(CachedLayer layer)
    {
        IMapQueries map = _queries().Map;
        byte[] bytes = layer.Bytes;
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                DepositKind? deposit = map.GetTile(x, y).Deposit;
                bytes[(y * layer.Width) + x] = deposit is null ? (byte)0 : (byte)((int)deposit.Value + 1);
            }
        }
    }

    private void FillBuildable(CachedLayer layer)
    {
        ISimQueries queries = _queries();
        IMapQueries map = queries.Map;
        IProgressionQueries progression = queries.Progression;
        byte[] bytes = layer.Bytes;
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                TileInfo tile = map.GetTile(x, y);

                // Mirrors the terrain, slope, access and fog rules of the placement check (BuildingPlacement): the overlay
                // answers "could a building stand here at all", not "could this building stand here".
                bool buildable = _content.Get(tile.Terrain).Buildable
                    && tile.Slope <= BuildingPlacement.MaxSlope
                    && TerrainAccessCheck.IsUsable(tile, _content, progression)
                    && queries.Exploration.IsRevealed(tile.Region);
                bytes[(y * layer.Width) + x] = buildable ? (byte)1 : (byte)0;
            }
        }
    }
}
