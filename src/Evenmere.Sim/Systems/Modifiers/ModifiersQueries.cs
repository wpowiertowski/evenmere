using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// Implements <see cref="IModifiersQueries"/>, which has no members in M1 on purpose: the stat queries (<c>BonusPercent</c>,
/// <c>ListModifiers</c>) are on <see cref="IProgressionQueries"/>, answered by <see cref="ModifierIndex"/>.
/// </summary>
internal sealed class ModifiersQueries : IModifiersQueries
{
}
