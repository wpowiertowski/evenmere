using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>
/// The culture system: civics, traditions, festivals and great works. Its cadence is <see cref="Cadence.Daily"/>.
/// It is a stub in M0; M1-11 fills it.
/// </summary>
internal sealed class CultureSystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Culture;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public ICultureQueries Queries { get; } = new CultureQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M1-11 replaces each placeholder with its real handler.
        router.Register(new NotImplementedCommandHandler<SetCivicTarget>("M1-11"));
        router.Register(new NotImplementedCommandHandler<SetCivicQueue>("M1-11"));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M1-11 implements this system.
    }
}
