using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Systems.Economy;

/// <summary>
/// The economy system: coin taxes, spending and upkeep. Its cadence is <see cref="Cadence.DailyBucketed"/>.
/// It is a stub in M0; M3-01 fills it.
/// </summary>
internal sealed class EconomySystem : ISimSystem
{
    /// <inheritdoc />
    public string Name => SystemNames.Economy;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IEconomyQueries Queries { get; } = new EconomyQueries();

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // M3-01 registers this system's command handlers here.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // M3-01 implements this system.
    }
}
