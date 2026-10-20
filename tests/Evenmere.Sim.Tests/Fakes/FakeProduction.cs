using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>A settable stand-in for <see cref="IProductionQueries"/>.</summary>
internal sealed class FakeProductionQueries : IProductionQueries
{
    private readonly Dictionary<EntityId, ProductionInfo> _producers = [];
    private readonly Dictionary<GoodId, Amount> _producedYesterday = [];

    /// <summary>Sets what a producer reports.</summary>
    /// <param name="info">The producer's state; its building ID is the key.</param>
    /// <returns>This fake.</returns>
    public FakeProductionQueries SetProducer(ProductionInfo info)
    {
        _producers[info.Building] = info;
        return this;
    }

    /// <summary>Sets what the island produced yesterday.</summary>
    /// <param name="good">The good.</param>
    /// <param name="amount">The amount in milli-units.</param>
    /// <returns>This fake.</returns>
    public FakeProductionQueries SetProducedYesterday(GoodId good, Amount amount)
    {
        _producedYesterday[good] = amount;
        return this;
    }

    /// <inheritdoc />
    public bool TryGet(EntityId building, out ProductionInfo info)
    {
        if (_producers.TryGetValue(building, out ProductionInfo? found))
        {
            info = found;
            return true;
        }

        info = null!;
        return false;
    }

    /// <inheritdoc />
    public Amount ProducedYesterday(GoodId good) => _producedYesterday.GetValueOrDefault(good);
}

/// <summary>
/// Production's buffers as a fake: the test lists the requests, and the fake keeps the goods of each building's output and
/// input buffer, with the real all-or-nothing rule for taking output.
/// </summary>
internal sealed class FakeProductionBuffers : IProductionBuffers
{
    private readonly List<TransportRequest> _requests = [];
    private readonly Dictionary<(EntityId Building, GoodId Good), Amount> _outputs = [];
    private readonly Dictionary<(EntityId Building, GoodId Good), Amount> _inputs = [];

    /// <summary>Opens a request; <see cref="ListRequests"/> returns the requests sorted by request tick and building ID.</summary>
    /// <param name="request">The request.</param>
    /// <returns>This fake.</returns>
    public FakeProductionBuffers AddRequest(TransportRequest request)
    {
        _requests.Add(request);
        return this;
    }

    /// <summary>Puts goods in a building's output buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good.</param>
    /// <param name="amount">The amount in milli-units.</param>
    /// <returns>This fake.</returns>
    public FakeProductionBuffers SetOutput(EntityId building, GoodId good, Amount amount)
    {
        _outputs[(building, good)] = amount;
        return this;
    }

    /// <summary>Reads a building's output buffer.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good.</param>
    /// <returns>The amount in milli-units.</returns>
    public Amount OutputOf(EntityId building, GoodId good) => _outputs.GetValueOrDefault((building, good));

    /// <summary>Reads a building's input buffer.</summary>
    /// <param name="building">The consumer.</param>
    /// <param name="good">The good.</param>
    /// <returns>The amount in milli-units.</returns>
    public Amount InputOf(EntityId building, GoodId good) => _inputs.GetValueOrDefault((building, good));

    /// <inheritdoc />
    public void ListRequests(List<TransportRequest> requests)
    {
        requests.AddRange(_requests.OrderBy(static r => r.RequestTick).ThenBy(static r => r.Building));
    }

    /// <inheritdoc />
    public bool TryTakeOutput(EntityId building, GoodId good, Amount amount)
    {
        Amount held = OutputOf(building, good);
        if (held < amount)
        {
            return false;
        }

        _outputs[(building, good)] = held - amount;
        return true;
    }

    /// <inheritdoc />
    public void AddInput(EntityId building, GoodId good, Amount amount) =>
        _inputs[(building, good)] = InputOf(building, good) + amount;
}
