using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// The map service: tile grid, terrain and the building registry. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It may register command handlers.
/// It is a stub in M0; M1-01 fills it.
/// </summary>
internal sealed class MapSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Map;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IMapQueries Queries { get; } = new MapQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-01 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through command handlers and the calls other systems make into it.
    }
}
