using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Production.Commands;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// The production system: producers, recipes and their buffers. Its cadence is <see cref="Cadence.EveryTick"/>.
/// It is a stub in M0; M1-08 fills it.
/// </summary>
internal sealed class ProductionSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Production;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IProductionQueries Queries { get; } = new ProductionQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-08 replaces the placeholder with its real handler.
        router.Register(new NotImplementedCommandHandler<SetBuildingPaused>("M1-08"));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-08 implements this system.
    }
}
