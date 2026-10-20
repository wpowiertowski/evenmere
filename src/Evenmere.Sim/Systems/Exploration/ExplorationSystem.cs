using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Exploration;

/// <summary>
/// The exploration system: fog, expeditions and discoveries. Its cadence is <see cref="Cadence.EveryTick"/>.
/// It is a stub in M0; M2-06 fills it.
/// </summary>
internal sealed class ExplorationSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Exploration;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IExplorationQueries Queries { get; } = new ExplorationQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M2-06 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M2-06 implements this system.
    }
}
