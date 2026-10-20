using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Research.Commands;

/// <summary>
/// Chooses the tech that receives the day's knowledge. Progress already made on other techs is kept. Refused with
/// <see cref="RejectReason.NodeNotAvailable"/> if the tech is not available (done, a prerequisite missing, or its era
/// has not begun). Handled by M1-11.
/// </summary>
/// <param name="Tech">The tech to research.</param>
public sealed record SetResearchTarget(TechId Tech) : Command;
