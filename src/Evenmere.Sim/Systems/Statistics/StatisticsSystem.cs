using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>
/// The statistics system: rolling history for graphs. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-12 fills it.
/// </summary>
internal sealed class StatisticsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Statistics;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IStatisticsQueries Queries { get; } = new StatisticsQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-12 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-12 implements this system.
    }
}
