using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the carts and transport jobs (ADR-0005). Implemented by the logistics system (M1-09).</summary>
public interface ILogisticsQueries
{
    /// <summary>
    /// Gets the jobs that are under way, in ascending job-ID order, for the view to replay as carts. The span is a copy of
    /// the jobs made for the view, not the simulation's own records, and it stays valid only until the next tick.
    /// </summary>
    /// <returns>The active jobs; empty when there are none.</returns>
    ReadOnlySpan<TransportJobView> ActiveJobs();

    /// <summary>Gets how many of a storehouse's carts are idle.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <returns>The number of free carts; 0 for a building that is not a storehouse.</returns>
    int FreeCarts(EntityId storehouse);
}
