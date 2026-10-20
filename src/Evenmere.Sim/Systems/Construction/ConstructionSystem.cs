using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The construction system: construction sites and placement validation. Its cadence is <see cref="Cadence.EveryTick"/>.
/// It is a stub in M0; M1-06 fills it.
/// </summary>
internal sealed class ConstructionSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Construction;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IConstructionQueries Queries { get; } = new ConstructionQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-06 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-06 implements this system.
    }
}
