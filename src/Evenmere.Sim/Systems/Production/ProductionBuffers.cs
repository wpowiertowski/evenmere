using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// Production's buffers as Logistics uses them (ADR-0005): the open requests for carts, and the moves of goods between a
/// producer's buffers and a cart. The request flags live in each <see cref="ProducerRecord"/> as the tick the need first
/// arose, which the production tick raises and clears as the buffers fill and empty; this class only lists them and clears
/// them when goods move.
/// </summary>
internal sealed class ProductionBuffers(ProductionState state, ContentDb content) : IProductionBuffers, IBindsContext
{
    private SimContext? _context;

    private SimContext Context =>
        _context ?? throw new InvalidOperationException("The production buffers have no context yet; SimContext binds them when it is created.");

    /// <inheritdoc />
    public void Bind(SimContext context) => _context = context;

    /// <inheritdoc />
    public void ListRequests(List<TransportRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        int first = requests.Count;
        Amount cartLoad = content.Rules.CartLoad;
        foreach (IdTable<ProducerRecord>.Entry entry in state.Producers)
        {
            ProducerRecord record = entry.Value;
            RecipeDef recipe = content.Get(record.Recipe);

            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                long since = record.OutputRequestTicks[i];
                if (since != ProducerRecord.NoRequest)
                {
                    requests.Add(new TransportRequest(
                        entry.Id, TransportDirection.Pickup, recipe.Outputs[i].Good, Amount.Min(cartLoad, record.Outputs[i]), since));
                }
            }

            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                long since = record.InputRequestTicks[i];
                if (since != ProducerRecord.NoRequest)
                {
                    requests.Add(new TransportRequest(entry.Id, TransportDirection.Delivery, recipe.Inputs[i].Good, cartLoad, since));
                }
            }
        }

        // The records come in ID order, so only the request tick still has to be put first. The key is unique, so the
        // order is the same whatever the sort does with equal elements.
        requests.Sort(first, requests.Count - first, RequestOrder.Instance);
    }

    /// <inheritdoc />
    public bool TryTakeOutput(EntityId building, GoodId good, Amount amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);

        if (!state.Producers.TryGet(building, out ProducerRecord? record))
        {
            return false;
        }

        ImmutableArray<GoodAmount> outputs = content.Get(record.Recipe).Outputs;
        int index = IndexOf(outputs, good);
        if (index < 0 || record.Outputs[index] < amount)
        {
            return false;
        }

        record.Outputs[index] -= amount;

        // The goods moved, so the request is answered. If a whole load is still waiting, that is a new need from now.
        record.OutputRequestTicks[index] = record.Outputs[index] >= content.Rules.CartLoad ? Context.Tick : ProducerRecord.NoRequest;
        return true;
    }

    /// <summary>
    /// Puts delivered goods in a building's input buffer, with no upper limit, so a cart that was loaded never loses its goods.
    /// Goods for a building that has since gone, or that does not use the good, are dropped, because the building is no
    /// longer there to take them; check <see cref="Evenmere.Sim.Queries.IProductionQueries"/> (<c>TryGet</c>) before a delivery if they must be returned.
    /// </summary>
    /// <param name="building">The consumer.</param>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to add, in milli-units.</param>
    public void AddInput(EntityId building, GoodId good, Amount amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);

        if (!state.Producers.TryGet(building, out ProducerRecord? record))
        {
            return;
        }

        ImmutableArray<GoodAmount> inputs = content.Get(record.Recipe).Inputs;
        int index = IndexOf(inputs, good);
        if (index < 0)
        {
            return;
        }

        record.Inputs[index] += amount;

        // The goods arrived, so the delivery is no longer pending. The next production tick opens a new request if the buffer is still low.
        record.InputRequestTicks[index] = ProducerRecord.NoRequest;
    }

    private static int IndexOf(ImmutableArray<GoodAmount> goods, GoodId good)
    {
        for (int i = 0; i < goods.Length; i++)
        {
            if (goods[i].Good == good)
            {
                return i;
            }
        }

        return -1;
    }

    // Request tick first, then building ID, then pickups before deliveries, then good ID: a total order over distinct requests.
    private sealed class RequestOrder : IComparer<TransportRequest>
    {
        public static RequestOrder Instance { get; } = new();

        public int Compare(TransportRequest x, TransportRequest y)
        {
            int byTick = x.RequestTick.CompareTo(y.RequestTick);
            if (byTick != 0)
            {
                return byTick;
            }

            int byBuilding = x.Building.CompareTo(y.Building);
            if (byBuilding != 0)
            {
                return byBuilding;
            }

            int byDirection = x.Direction.CompareTo(y.Direction);
            return byDirection != 0 ? byDirection : x.Good.Value.CompareTo(y.Good.Value);
        }
    }
}
