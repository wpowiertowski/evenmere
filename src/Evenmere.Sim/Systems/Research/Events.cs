using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Research;

/// <summary>A tech was researched and its effects applied.</summary>
/// <param name="Tick">The tick during which the tech was completed.</param>
/// <param name="Tech">The tech.</param>
public sealed record TechResearched(long Tick, TechId Tech) : SimEvent(Tick);

/// <summary>
/// A tech's or a civic's inspiration condition became true, which discounts the node's cost. Research emits it for techs
/// and Culture for civics, so exactly one of <see cref="Tech"/> and <see cref="Civic"/> is set; use <see cref="ForTech"/>
/// and <see cref="ForCivic"/> to create it.
/// </summary>
/// <param name="Tick">The tick during which the inspiration triggered.</param>
/// <param name="Tech">The tech, for a tech inspiration; otherwise <see langword="null"/>.</param>
/// <param name="Civic">The civic, for a civic inspiration; otherwise <see langword="null"/>.</param>
public sealed record InspirationTriggered(long Tick, TechId? Tech, CivicId? Civic) : SimEvent(Tick)
{
    /// <summary>Creates the event for a tech.</summary>
    /// <param name="tick">The tick during which the inspiration triggered.</param>
    /// <param name="tech">The tech.</param>
    /// <returns>The event.</returns>
    public static InspirationTriggered ForTech(long tick, TechId tech) => new(tick, tech, null);

    /// <summary>Creates the event for a civic.</summary>
    /// <param name="tick">The tick during which the inspiration triggered.</param>
    /// <param name="civic">The civic.</param>
    /// <returns>The event.</returns>
    public static InspirationTriggered ForCivic(long tick, CivicId civic) => new(tick, null, civic);
}
