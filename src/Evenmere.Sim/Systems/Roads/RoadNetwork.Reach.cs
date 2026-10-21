using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

// The per-building lookups derived from the fields: the nearest storehouse and the services that cover a building.
internal sealed partial class RoadNetwork
{
    // What has been worked out for one building, with the epoch each half was worked out in. The storehouse half and the
    // service half are separate because deciding whether a market is in reach needs the storehouse half of the market
    // while the service half of a house is being worked out.
    private struct Derived
    {
        public int Building;
        public int StorehouseEpoch;
        public int Storehouse;
        public int StorehouseCost;
        public int CoverageEpoch;
        public int CoveredServices;
    }

    // Sorted by building ID so a lookup is a binary search. Entries are removed when the building is.
    private Derived[] _derived = [];
    private int _derivedCount;

    /// <summary>Tests whether some storehouse's reach covers the building. A storehouse is in its own reach.</summary>
    /// <param name="building">The building or site.</param>
    /// <returns><see langword="true"/> if a storehouse's road distance to the building is within that storehouse's reach.</returns>
    public bool IsInStorehouseReach(EntityId building) => TryNearestStorehouse(building, out _, out _);

    /// <summary>
    /// Tests whether a footprint that is not built yet would lie in storehouse reach: some road tile beside it is inside a
    /// storehouse's field, within that storehouse's reach. It asks the same fields, with the same costs, as a building on that
    /// footprint would.
    /// </summary>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="height">The footprint height in tiles.</param>
    /// <returns><see langword="true"/> if a road tile touching the footprint is within some storehouse's reach.</returns>
    public bool IsFootprintInStorehouseReach(int x, int y, int width, int height)
    {
        // With no roads there is nothing to touch, and nothing to ask the map about.
        if (width <= 0 || height <= 0 || _state.Layer.Length == 0)
        {
            return false;
        }

        EnsureReady();

        // A footprint with no entity ID: no field's source is EntityId.None, so the "storehouse is in its own reach" rule never applies.
        var footprint = new BuildingInfo(EntityId.None, default, new TileCoord(x, y), Rotation.Deg0, width, height, BuildingState.Site, 0);
        foreach (DistanceField field in _fields)
        {
            if (field.StorageBound > 0 && TryCostFrom(field, footprint, field.StorageBound, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the storehouse nearest to a building by road distance.</summary>
    /// <param name="building">The building or site.</param>
    /// <param name="storehouse">The nearest storehouse whose reach covers the building; the lowest ID wins a tie.</param>
    /// <param name="cost">The road cost from the storehouse to the building's nearest access tile.</param>
    /// <returns><see langword="true"/> if a storehouse reaches the building.</returns>
    public bool TryNearestStorehouse(EntityId building, out EntityId storehouse, out int cost)
    {
        storehouse = EntityId.None;
        cost = 0;
        if (building.IsNone)
        {
            return false;
        }

        EnsureReady();
        if (!TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        (int id, int distance) = NearestStorehouseOf(info);
        if (id == 0)
        {
            return false;
        }

        storehouse = new EntityId(id);
        cost = distance;
        return true;
    }

    /// <summary>Finds the road cost from one particular storehouse to a building.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <param name="building">The building or site.</param>
    /// <param name="cost">The road cost from the storehouse to the building's nearest access tile.</param>
    /// <returns><see langword="true"/> if the building lies within that storehouse's reach.</returns>
    public bool TryGetStorehouseCost(EntityId storehouse, EntityId building, out int cost)
    {
        cost = 0;
        if (storehouse.IsNone || building.IsNone)
        {
            return false;
        }

        EnsureReady();
        int index = FindField(storehouse);
        if (index < 0 || _fields[index].StorageBound <= 0 || !TryGetBuilding(building, out BuildingInfo info))
        {
            return false;
        }

        return TryCostFrom(_fields[index], info, _fields[index].StorageBound, out cost);
    }

    /// <summary>Tests whether a building lies within the coverage of a building that provides a service.</summary>
    /// <param name="house">The house, or any building.</param>
    /// <param name="service">The service.</param>
    /// <returns><see langword="true"/> if covered. A market covers only when it is itself in storehouse reach.</returns>
    public bool IsCovered(EntityId house, ServiceKind service)
    {
        if (house.IsNone || (uint)service >= 32)
        {
            return false;
        }

        EnsureReady();
        return TryGetBuilding(house, out BuildingInfo info) && (CoveredServicesOf(info) & (1 << (int)service)) != 0;
    }

    // Forgets what was derived for a building that no longer exists.
    private void ForgetDerived(EntityId building)
    {
        int index = FindDerived(building.Value);
        if (index < 0)
        {
            return;
        }

        Array.Copy(_derived, index + 1, _derived, index, _derivedCount - index - 1);
        _derivedCount--;
    }

    private int FindDerived(int building)
    {
        int low = 0;
        int high = _derivedCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = _derived[middle].Building.CompareTo(building);
            if (order == 0)
            {
                return middle;
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    // The slot for a building, added if it has none. Nothing may hold a reference into the array across this call.
    private int SlotFor(int building)
    {
        int index = FindDerived(building);
        if (index >= 0)
        {
            return index;
        }

        index = ~index;
        if (_derivedCount == _derived.Length)
        {
            Array.Resize(ref _derived, Math.Max(16, _derived.Length * 2));
        }

        Array.Copy(_derived, index, _derived, index + 1, _derivedCount - index);
        _derived[index] = new Derived { Building = building };
        _derivedCount++;
        return index;
    }

    // The nearest storehouse to a building and its cost, from the cache when the fields have not changed since.
    // The ID is 0 when no storehouse reaches the building.
    private (int Storehouse, int Cost) NearestStorehouseOf(in BuildingInfo building)
    {
        int slot = FindDerived(building.Id.Value);
        if (slot >= 0 && _derived[slot].StorehouseEpoch == _epoch)
        {
            return (_derived[slot].Storehouse, _derived[slot].StorehouseCost);
        }

        int best = 0;
        int bestCost = 0;
        foreach (DistanceField field in _fields)
        {
            // Fields are in ascending ID order and only a strictly cheaper one replaces the best, so a tie goes to the lowest ID.
            if (field.StorageBound > 0 && TryCostFrom(field, building, field.StorageBound, out int cost) && (best == 0 || cost < bestCost))
            {
                best = field.Source.Value;
                bestCost = cost;
            }
        }

        slot = SlotFor(building.Id.Value);
        _derived[slot].StorehouseEpoch = _epoch;
        _derived[slot].Storehouse = best;
        _derived[slot].StorehouseCost = bestCost;
        return (best, bestCost);
    }

    // The set of services that cover a building, one bit per ServiceKind, from the cache when the fields have not changed since.
    private int CoveredServicesOf(in BuildingInfo building)
    {
        int slot = FindDerived(building.Id.Value);
        if (slot >= 0 && _derived[slot].CoverageEpoch == _epoch)
        {
            return _derived[slot].CoveredServices;
        }

        int covered = 0;
        foreach (DistanceField field in _fields)
        {
            int bit = 1 << (int)field.ServiceKind;
            if (field.ServiceBound <= 0 || (covered & bit) != 0 || !TryCostFrom(field, building, field.ServiceBound, out _))
            {
                continue;
            }

            // ADR-0005: goods reach a house only through a market that a storehouse can supply, so a market cut off from every
            // storehouse covers nobody.
            if (field.ServiceKind == ServiceKind.Market && NearestStorehouseOf(field.Building).Storehouse == 0)
            {
                continue;
            }

            covered |= bit;
        }

        slot = SlotFor(building.Id.Value);
        _derived[slot].CoverageEpoch = _epoch;
        _derived[slot].CoveredServices = covered;
        return covered;
    }

    // The cheapest route from a field's source to a building: the least cost to one of the building's access tiles that the
    // field reaches, if it is within the bound. The source itself is at cost 0, so a storehouse is in its own reach.
    private bool TryCostFrom(DistanceField field, in BuildingInfo building, int bound, out int cost)
    {
        cost = 0;
        if (field.Source == building.Id)
        {
            return true;
        }

        if (field.Tiles.Length == 0)
        {
            return false;
        }

        Span<int> ring = RingBuffer(building, stackalloc int[StackRing]);
        int count = CollectAccess(building, ring);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (field.TryGetCost(ring[i], out int candidate) && candidate <= bound && (!found || candidate < cost))
            {
                cost = candidate;
                found = true;
            }
        }

        return found;
    }
}
