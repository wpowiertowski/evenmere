using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Tools.Balance.EraOne;

/// <summary>
/// Where the Era I bot sends its next expedition: an idle Scout's Lodge and a fogged region beside revealed land.
/// The M2-02b scenario prototype proved the settlement must fit within the hall's storehouse reach until the first
/// storehouse, and that a site touching the hall still needs a road beside it; this ports its ideas into Tools (the
/// bot never references test code): a spiral from the hall is fog-checked before placing, and idle lodges open the
/// neighbouring regions. Everything reads through <see cref="ISimulation"/> queries, the way a player uses the lodge
/// picker, and the choice is deterministic: the lowest lodge ID and the lowest region number always win.
/// </summary>
public static class ExpeditionPlanner
{
    /// <summary>Gets the building tag a finished building must carry to send expeditions. Never a content ID.</summary>
    public const string LodgeTag = "expedition_lodge";

    /// <summary>
    /// Finds the bot's next expedition: the lowest-ID idle finished lodge and the lowest-numbered fogged region that
    /// borders revealed land. Regions are read tile by tile (4-connectivity, like the sim's own border check), so no
    /// map internals leak into the bot.
    /// </summary>
    /// <param name="content">The loaded content, for the lodge tag.</param>
    /// <param name="simulation">The simulation, read between ticks.</param>
    /// <returns>The lodge and the region, or <see langword="null"/> when no idle lodge or no adjacent fogged region exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static (EntityId Lodge, ushort Region)? FindExpedition(ContentDb content, ISimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(simulation);

        EntityId lodge = FirstIdleLodge(content, simulation);
        if (lodge == EntityId.None)
        {
            return null;
        }

        ushort? region = FirstAdjacentFogged(simulation);
        return region is null ? null : (lodge, region.Value);
    }

    private static EntityId FirstIdleLodge(ContentDb content, ISimulation simulation)
    {
        EntityId best = EntityId.None;
        foreach (BuildingInfo building in simulation.Query.Map.Buildings)
        {
            if (!building.IsComplete)
            {
                continue;
            }

            bool isLodge = false;
            foreach (string tag in content.Get(building.Type).Tags)
            {
                if (tag == LodgeTag)
                {
                    isLodge = true;
                    break;
                }
            }

            if (!isLodge)
            {
                continue;
            }

            if (simulation.Query.Exploration.ExpeditionForLodge(building.Id) is not null)
            {
                continue;
            }

            if (best == EntityId.None || building.Id < best)
            {
                best = building.Id;
            }
        }

        return best;
    }

    private static ushort? FirstAdjacentFogged(ISimulation simulation)
    {
        int width = simulation.Query.Map.Width;
        int height = simulation.Query.Map.Height;
        var fogged = new SortedSet<ushort>();
        var revealed = new HashSet<ushort>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ushort region = simulation.Query.Map.GetTile(x, y).Region;
                if (simulation.Query.Exploration.IsRevealed(region))
                {
                    revealed.Add(region);
                }
                else
                {
                    fogged.Add(region);
                }
            }
        }

        if (fogged.Count == 0)
        {
            return null;
        }

        // A fogged region qualifies when one of its tiles touches a revealed tile along an edge.
        var adjacent = new HashSet<ushort>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ushort region = simulation.Query.Map.GetTile(x, y).Region;
                if (!fogged.Contains(region))
                {
                    continue;
                }

                if ((x > 0 && revealed.Contains(simulation.Query.Map.GetTile(x - 1, y).Region))
                    || (x + 1 < width && revealed.Contains(simulation.Query.Map.GetTile(x + 1, y).Region))
                    || (y > 0 && revealed.Contains(simulation.Query.Map.GetTile(x, y - 1).Region))
                    || (y + 1 < height && revealed.Contains(simulation.Query.Map.GetTile(x, y + 1).Region)))
                {
                    adjacent.Add(region);
                }
            }
        }

        ushort best = 0;
        bool found = false;
        foreach (ushort region in adjacent)
        {
            if (!found || region < best)
            {
                best = region;
                found = true;
            }
        }

        return found ? best : null;
    }
}
