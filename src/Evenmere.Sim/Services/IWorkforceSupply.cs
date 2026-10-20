using Evenmere.Core.Ids;

namespace Evenmere.Sim.Services;

/// <summary>
/// How Population tells Workforce how many workers each tier supplies. Population sets the supply of every tier once a
/// day; Workforce scales it by the <c>workforce</c> modifiers when it allocates. Implemented by the workforce system
/// (M1-07).
/// </summary>
internal interface IWorkforceSupply
{
    /// <summary>Sets how many workers a tier supplies until the next call.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="workers">The number of workers, as a whole count; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workers"/> is negative.</exception>
    void SetSupply(TierId tier, int workers);
}
