using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture.Commands;

/// <summary>
/// Chooses the civic that receives the day's culture. Progress already made on other civics is kept. Refused with
/// <see cref="RejectReason.NodeNotAvailable"/> if the civic is not available (adopted, a prerequisite missing, or its era
/// has not begun). Handled by M1-11.
/// </summary>
/// <param name="Civic">The civic to adopt.</param>
public sealed record SetCivicTarget(CivicId Civic) : Command;
