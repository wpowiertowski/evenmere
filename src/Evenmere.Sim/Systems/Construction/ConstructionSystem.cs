using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The construction system: sites, building placement, demolition and in-place upgrades. Its cadence is <see cref="Cadence.EveryTick"/>.
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
        // M1-06 replaces each placeholder with its real handler.
        router.Register(new NotImplementedCommandHandler<PlaceBuilding>("M1-06"));
        router.Register(new NotImplementedCommandHandler<Demolish>("M1-06"));
        router.Register(new NotImplementedCommandHandler<UpgradeBuilding>("M1-06"));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-06 implements this system.
    }
}
