using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Workforce.Commands;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// The workforce system: tier workforce and its allocation to workplaces. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-07 fills it.
/// </summary>
internal sealed class WorkforceSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Workforce;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IWorkforceQueries Queries { get; } = new WorkforceQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-07 replaces the placeholder with its real handler.
        router.Register(new NotImplementedCommandHandler<SetWorkforcePriority>("M1-07"));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-07 implements this system.
    }
}
