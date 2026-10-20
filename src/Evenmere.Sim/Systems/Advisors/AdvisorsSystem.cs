using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// The advisors system: problem detection and advice. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-15 fills it.
/// </summary>
internal sealed class AdvisorsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Advisors;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IAdvisorsQueries Queries { get; } = new AdvisorsQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-15 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-15 implements this system.
    }
}
