using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The progression system: unlocks, effects, the points ledger and milestone counters. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-04 fills it.
/// </summary>
internal sealed class ProgressionSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Progression;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IProgressionQueries Queries { get; } = new ProgressionQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-04 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-04 implements this system.
    }
}
