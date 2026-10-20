using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// The modifiers service: effective-stat index. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It may register command handlers.
/// It is a stub in M0; M1-04 fills it.
/// </summary>
internal sealed class ModifiersSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Modifiers;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IModifiersQueries Queries { get; } = new ModifiersQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-04 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through command handlers and the calls other systems make into it.
    }
}
