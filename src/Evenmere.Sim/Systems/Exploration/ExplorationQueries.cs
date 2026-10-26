using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>Implements <see cref="IExplorationQueries"/> over the exploration and map states. (M2-06.)</summary>
internal sealed class ExplorationQueries : IExplorationQueries
{
    private readonly ExplorationState _state;
    private readonly MapState _map;

    /// <summary>Initializes a new instance of the <see cref="ExplorationQueries"/> class.</summary>
    /// <param name="state">The exploration state.</param>
    /// <param name="map">The map state, which holds the regions and discoveries.</param>
    public ExplorationQueries(ExplorationState state, MapState map)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);

        _state = state;
        _map = map;
    }

    /// <inheritdoc />
    public bool IsRevealed(ushort region) => ExpeditionMath.IsRevealedBit(_state.RevealedRegions, region);

    /// <inheritdoc />
    public ushort[] RevealedRegions()
    {
        var revealed = new List<ushort>();
        // Regions are dense from zero on every island this game builds, but a damaged state could hold sparse
        // bits, so the query walks the bitset rather than the map: every set bit below 65536 is reported.
        for (int word = 0; word < _state.RevealedRegions.Length; word++)
        {
            ulong bits = _state.RevealedRegions[word];
            while (bits != 0)
            {
                int bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                revealed.Add((ushort)((word << 6) + bit));
                bits &= bits - 1;
            }
        }

        revealed.Sort();
        return [.. revealed];
    }

    /// <inheritdoc />
    public ExpeditionView? ExpeditionForLodge(EntityId lodge)
    {
        if (_state.Expeditions.TryGet(lodge, out ExpeditionRecord record))
        {
            return new ExpeditionView(lodge, record.TargetRegion, record.DaysLeft);
        }

        return null;
    }

    /// <inheritdoc />
    public ExpeditionView[] Expeditions()
    {
        var views = new List<ExpeditionView>(_state.Expeditions.Count);
        foreach (IdTable<ExpeditionRecord>.Entry entry in _state.Expeditions)
        {
            views.Add(new ExpeditionView(entry.Id, entry.Value.TargetRegion, entry.Value.DaysLeft));
        }

        return [.. views];
    }

    /// <inheritdoc />
    public DiscoveryView[] Discoveries()
    {
        StoredDiscovery[] placed = _map.Discoveries;
        var views = new DiscoveryView[placed.Length];
        for (int i = 0; i < placed.Length; i++)
        {
            StoredDiscovery discovery = placed[i];
            bool revealed = ExpeditionMath.IsRevealedBit(_state.RevealedRegions, discovery.Region);
            bool applied = (uint)i < (uint)_state.DiscoveriesApplied.Length && _state.DiscoveriesApplied[i];
            views[i] = new DiscoveryView(discovery.Region, discovery.Kind, discovery.X, discovery.Y, revealed, applied);
        }

        return views;
    }
}
