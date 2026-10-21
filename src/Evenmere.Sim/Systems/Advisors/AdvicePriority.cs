using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// The one priority table for advisor notes: lower shows first. It drives both which queued records fill the five
/// showing slots and the order <c>Active()</c> returns them in. A record that is already showing is never demoted for
/// a higher-priority newcomer; the table only orders which queued records fill free slots.
/// </summary>
internal static class AdvicePriority
{
    /// <summary>Gets the priority of a kind: lower shows first.</summary>
    /// <param name="kind">The advice kind.</param>
    /// <returns>The priority, 0 for the most urgent kind.</returns>
    public static int Of(AdviceKind kind) => kind switch
    {
        AdviceKind.GoodShortage => 0,
        AdviceKind.NoWorkforce => 1,
        AdviceKind.NoRoadAccess => 2,
        AdviceKind.OutsideStorehouseReach => 3,
        AdviceKind.HouseCannotAdvance => 4,
        AdviceKind.ResearchIdle => 5,
        AdviceKind.CivicIdle => 6,
        _ => 7,
    };
}
