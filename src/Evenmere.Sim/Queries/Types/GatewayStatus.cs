namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// Whether an era's two gateways are complete: the tech gateway (researched) and the civic gateway (adopted).
/// A gateway counts as done when its source's effects have been applied, which research and culture do exactly once
/// on completion, so in a real game this agrees with the research and culture done sets. (M2-11, per ADR-0017.)
/// </summary>
/// <param name="TechDone">Whether the era's tech gateway has been researched. False when the era names no tech gateway.</param>
/// <param name="CivicDone">Whether the era's civic gateway has been adopted. False when the era names no civic gateway.</param>
public readonly record struct GatewayStatus(bool TechDone, bool CivicDone);
