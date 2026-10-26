using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Happenings.Commands;

/// <summary>
/// Answers a pending happening with one of its options. The option's effects apply at once: goods and points are
/// granted, modifiers register with the source <c>happening:&lt;id&gt;</c> (duration-bound ones expire later), and a
/// festival request becomes a discount the cost check reads. Choosing clears the pending slot and starts the cooldown.
/// Handled by M2-12.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.Invalid"/> when the happening is not defined by the content, nothing is
/// pending, the pending happening is a different one, or the option index is out of range.
/// </remarks>
/// <param name="Happening">The happening being answered; must be the pending one.</param>
/// <param name="OptionIndex">Which option to apply, from 0 below the option count.</param>
public sealed record ChooseHappeningOption(HappeningId Happening, int OptionIndex) : Command;
