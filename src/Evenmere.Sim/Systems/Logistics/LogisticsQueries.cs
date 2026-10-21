using Evenmere.Content;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// Implements <see cref="ILogisticsQueries"/>: the jobs under way as view copies for the carts the view replays, and
/// the free carts per storehouse. The view snapshot is rebuilt at the end of every tick, so the span stays valid until
/// the next tick.
/// </summary>
internal sealed class LogisticsQueries : ILogisticsQueries
{
    private readonly LogisticsState _state;
    private readonly ContentDb _content;
    private readonly Func<ISimQueries> _queries;
    private TransportJobView[] _views = [];
    private int _count;

    /// <summary>Initializes a new instance of the <see cref="LogisticsQueries"/> class.</summary>
    /// <param name="state">The logistics slice of the world state.</param>
    /// <param name="content">The loaded content, for the carts each storehouse owns.</param>
    /// <param name="queries">
    /// A late-bound accessor for the query facade. The roster builds this system before the facade exists, and queries
    /// carry no <see cref="Pipeline.SimContext"/>, so the map is reached through this. It is called only when a query
    /// arrives, never during construction.
    /// </param>
    public LogisticsQueries(LogisticsState state, ContentDb content, Func<ISimQueries> queries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(queries);

        _state = state;
        _content = content;
        _queries = queries;
    }

    /// <inheritdoc />
    public ReadOnlySpan<TransportJobView> ActiveJobs() => new(_views, 0, _count);

    /// <inheritdoc />
    public int FreeCarts(EntityId storehouse)
    {
        if (!_queries().Map.TryGetBuilding(storehouse, out BuildingInfo building))
        {
            return 0;
        }

        // Only a finished, working storehouse owns carts; a site, a ruin or a paused hall sends none out.
        if (building.State != BuildingState.Active)
        {
            return 0;
        }

        if (_content.Get(building.Type).Storage is not { } storage)
        {
            return 0;
        }

        int busy = _state.BusyCarts.TryGet(storehouse, out int held) ? held : 0;
        return Math.Max(0, storage.Carts - busy);
    }

    /// <summary>
    /// Rebuilds the view snapshot from the jobs, in ascending job-ID order. The system calls it at the end of every
    /// tick, so the span <see cref="ActiveJobs"/> returns stays valid until the next tick.
    /// </summary>
    internal void Update()
    {
        if (_views.Length < _state.Jobs.Count)
        {
            _views = new TransportJobView[Math.Max(4, _state.Jobs.Count)];
        }

        int count = 0;
        foreach (IdTable<TransportJobRecord>.Entry entry in _state.Jobs)
        {
            TransportJobRecord job = entry.Value;
            _views[count++] = new TransportJobView(
                entry.Id, job.Storehouse, job.Building, job.Good, job.Amount, job.Direction, job.Phase, job.PhaseStartTick, job.PhaseTicks);
        }

        _count = count;
    }
}
