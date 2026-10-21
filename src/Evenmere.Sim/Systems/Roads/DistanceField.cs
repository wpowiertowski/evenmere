using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The road-distance field of one source building (a storehouse, a service building, or the Founders' Hall, which is both):
/// the road cost from the source's access tiles to every road tile within the source's bound. The field is stored as two
/// parallel arrays sorted by tile index, so a lookup is a binary search and the content never depends on a hash order.
/// </summary>
/// <remarks>
/// One field serves both purposes. It is searched out to the larger of the storehouse reach and the service coverage, and a
/// query then compares the cost with the bound of the purpose it is asking about (<see cref="StorageBound"/> or
/// <see cref="ServiceBound"/>). Costs are in cost units, where 1000 is one tile of dirt road. A field is immutable once built
/// except for <see cref="Assign"/>, which the network calls when it rebuilds the field.
/// </remarks>
/// <param name="building">The source building as it stood when the field was created. A type change in place replaces the field.</param>
/// <param name="storageBound">The storehouse reach in cost units, or 0 when the building is not a storehouse.</param>
/// <param name="serviceBound">The service coverage in cost units, or 0 when the building provides no service.</param>
/// <param name="serviceKind">The service the building provides; meaningful only when <paramref name="serviceBound"/> is above 0.</param>
internal sealed class DistanceField(BuildingInfo building, int storageBound, int serviceBound, ServiceKind serviceKind)
{
    /// <summary>Gets the source building.</summary>
    public BuildingInfo Building { get; } = building;

    /// <summary>Gets the source building's entity ID.</summary>
    public EntityId Source => Building.Id;

    /// <summary>Gets the storehouse reach in cost units; 0 when the building is not a storehouse.</summary>
    public int StorageBound { get; } = storageBound;

    /// <summary>Gets the service coverage in cost units; 0 when the building provides no service.</summary>
    public int ServiceBound { get; } = serviceBound;

    /// <summary>Gets the service the building provides.</summary>
    public ServiceKind ServiceKind { get; } = serviceKind;

    /// <summary>Gets the bound the search runs to: the larger of the reach and the coverage.</summary>
    public int Bound => Math.Max(StorageBound, ServiceBound);

    /// <summary>Gets the indices of the road tiles within the bound, ascending.</summary>
    public int[] Tiles { get; private set; } = [];

    /// <summary>Gets the cost to each tile of <see cref="Tiles"/>, in the same order.</summary>
    public int[] Costs { get; private set; } = [];

    /// <summary>
    /// Gets the number of the build that last computed this field. Builds are numbered from 1 across the whole network, so
    /// a field was not rebuilt between two moments exactly when its revision is unchanged.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Gets the first column of the box that any change touching this field must intersect.</summary>
    public int ExtentMinX { get; private set; }

    /// <summary>Gets the first row of the box that any change touching this field must intersect.</summary>
    public int ExtentMinY { get; private set; }

    /// <summary>Gets the last column of the box that any change touching this field must intersect.</summary>
    public int ExtentMaxX { get; private set; }

    /// <summary>Gets the last row of the box that any change touching this field must intersect.</summary>
    public int ExtentMaxY { get; private set; }

    /// <summary>Replaces the contents of the field after a build.</summary>
    /// <param name="tiles">The tile indices, ascending.</param>
    /// <param name="costs">The costs, parallel to <paramref name="tiles"/>.</param>
    /// <param name="revision">The build number.</param>
    /// <param name="minX">The first column of the extent.</param>
    /// <param name="minY">The first row of the extent.</param>
    /// <param name="maxX">The last column of the extent.</param>
    /// <param name="maxY">The last row of the extent.</param>
    public void Assign(int[] tiles, int[] costs, int revision, int minX, int minY, int maxX, int maxY)
    {
        Tiles = tiles;
        Costs = costs;
        Revision = revision;
        ExtentMinX = minX;
        ExtentMinY = minY;
        ExtentMaxX = maxX;
        ExtentMaxY = maxY;
    }

    /// <summary>Tests whether a tile is in the field.</summary>
    /// <param name="tile">The tile index.</param>
    /// <returns><see langword="true"/> if the tile is a road tile within the bound.</returns>
    public bool Contains(int tile) => Array.BinarySearch(Tiles, tile) >= 0;

    /// <summary>Finds the cost to a tile.</summary>
    /// <param name="tile">The tile index.</param>
    /// <param name="cost">The cost in cost units, when the tile is in the field.</param>
    /// <returns><see langword="true"/> if the tile is a road tile within the bound.</returns>
    public bool TryGetCost(int tile, out int cost)
    {
        int index = Array.BinarySearch(Tiles, tile);
        if (index < 0)
        {
            cost = 0;
            return false;
        }

        cost = Costs[index];
        return true;
    }
}
