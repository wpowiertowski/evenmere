using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Happenings;

/// <summary>
/// The happenings system: gentle choice events. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M2-12 fills it.
/// </summary>
internal sealed class HappeningsSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Happenings;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IHappeningsQueries Queries { get; } = new HappeningsQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M2-12 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M2-12 implements this system.
    }
}
