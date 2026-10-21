using Evenmere.Content;
using Evenmere.Core.Time;
using Evenmere.Core.Ids;
using Evenmere.Sim.Explain;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Implements <see cref="IExplainQueries"/> by delegating to the explain service (M1-12).</summary>
/// <param name="content">The loaded content.</param>
/// <param name="queries">The read-only queries, late-bound because the roster builds them after the systems.</param>
/// <param name="clock">The current clock, late-bound because it advances every tick.</param>
internal sealed class ExplainQueries(ContentDb content, Func<ISimQueries> queries, Func<GameClock> clock) : IExplainQueries
{
    private readonly ExplainService _service = new(content, queries, clock);

    /// <inheritdoc />
    public Explanation ExplainGood(GoodId good) => _service.ExplainGood(good);

    /// <inheritdoc />
    public Explanation ExplainBuilding(EntityId building) => _service.ExplainBuilding(building);

    /// <inheritdoc />
    public Explanation ExplainHouse(EntityId house) => _service.ExplainHouse(house);
}
