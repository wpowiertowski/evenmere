using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// The logistics system: carts and transport jobs. Its cadence is <see cref="Cadence.EveryTick"/>.
/// It is a stub in M0; M1-09 fills it.
/// </summary>
internal sealed class LogisticsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Logistics;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public ILogisticsQueries Queries { get; } = new LogisticsQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-09 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-09 implements this system.
    }
}
