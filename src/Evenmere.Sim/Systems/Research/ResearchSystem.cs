using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// The research system: knowledge accrual, techs and inspirations. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-11 fills it.
/// </summary>
internal sealed class ResearchSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Research;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IResearchQueries Queries { get; } = new ResearchQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-11 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-11 implements this system.
    }
}
