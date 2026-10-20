namespace Evenmere.Sim.Systems.Advisors.Commands;

/// <summary>
/// Dismisses an advice note: Advisors stops raising that kind of advice for that subject for one season. Refused with
/// <see cref="RejectReason.UnknownTarget"/> if no advice has that ID. Handled by M1-15.
/// </summary>
/// <param name="AdviceId">The advice's ID, as <c>AdviceView.Id</c> reports it.</param>
public sealed record DismissAdvice(int AdviceId) : Command;
