using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// The needs system: consumption and service coverage. Its cadence is <see cref="Cadence.DailyBucketed"/>.
/// It is a stub in M0; M1-10 fills it.
/// </summary>
internal sealed class NeedsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Needs;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public INeedsQueries Queries { get; } = new NeedsQueries();

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
