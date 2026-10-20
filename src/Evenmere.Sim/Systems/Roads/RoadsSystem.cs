using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The roads service: road layer, connectivity and the reach and coverage fields. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It may register command handlers.
/// It is a stub in M0; M1-05 fills it.
/// </summary>
internal sealed class RoadsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Roads;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IRoadsQueries Queries { get; } = new RoadsQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-05 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through command handlers and the calls other systems make into it.
    }
}
