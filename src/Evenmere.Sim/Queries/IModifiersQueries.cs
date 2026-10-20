namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the effective-stat index. Empty in M1 on purpose: the stat queries (<c>BonusPercent</c>,
/// <c>ListModifiers</c>) are on <see cref="IProgressionQueries"/>, which M1-04 implements together with the modifier index.
/// </summary>
public interface IModifiersQueries
{
}
