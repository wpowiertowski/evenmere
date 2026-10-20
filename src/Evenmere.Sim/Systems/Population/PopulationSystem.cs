using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// The population system: houses, residents and tiers. Its cadence is <see cref="Cadence.DailyBucketed"/>.
/// It is a stub in M0; M1-10 fills it.
/// </summary>
internal sealed class PopulationSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Population;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IPopulationQueries Queries { get; } = new PopulationQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-10 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-10 implements this system.
    }
}
