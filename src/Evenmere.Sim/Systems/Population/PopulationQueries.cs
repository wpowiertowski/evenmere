using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// Implements <see cref="IPopulationQueries"/> over the population state. Everything it reports was stored by the
/// system on each house's last daily pass, so it needs no simulation context and is valid between ticks.
/// </summary>
/// <param name="state">The population slice of the world state.</param>
internal sealed class PopulationQueries(PopulationState state) : IPopulationQueries
{
    /// <inheritdoc />
    public int TotalResidents
    {
        get
        {
            int total = 0;
            foreach (IdTable<HouseRecord>.Entry entry in state.Houses)
            {
                total += entry.Value.Residents;
            }

            return total;
        }
    }

    /// <inheritdoc />
    public int Residents(TierId tier)
    {
        int total = 0;
        foreach (IdTable<HouseRecord>.Entry entry in state.Houses)
        {
            if (entry.Value.Tier == tier)
            {
                total += entry.Value.Residents;
            }
        }

        return total;
    }

    /// <inheritdoc />
    public bool TryGetHouse(EntityId house, out HouseInfo info)
    {
        if (!state.Houses.TryGet(house, out HouseRecord record))
        {
            info = default;
            return false;
        }

        info = new HouseInfo(
            house,
            record.Tier,
            record.Residents,
            record.TargetResidents,
            record.Capacity,
            record.AdvanceStreakDays,
            record.DeclineTimerDays,
            record.UpgradeLocked,
            state.FestivalActive,
            record.AdvanceBlocker,
            MoraleOf(in record));
        return true;
    }

    /// <summary>Computes a house's morale from its underemployment: 1000 when nobody works below the tier, and 1000 for an empty house.</summary>
    /// <param name="house">The house record.</param>
    /// <returns>The morale in per-mille.</returns>
    private static int MoraleOf(in HouseRecord house)
    {
        if (house.Residents <= 0)
        {
            return 1000;
        }

        int underemployed = Math.Clamp(house.Underemployed, 0, house.Residents);
        return 1000 * (house.Residents - underemployed) / house.Residents;
    }
}
