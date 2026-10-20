using Evenmere.Sim.Commands;

namespace Evenmere.Sim.Pipeline;

/// <summary>
/// One system or service of the simulation. The pipeline calls <see cref="Register"/> once when the simulation is
/// built and <see cref="Run"/> on the system's cadence. A system changes only its own slice of the world state and
/// reads other systems through their query interfaces (architecture.md, State and boundaries).
/// </summary>
public interface ISimSystem
{
    /// <summary>Gets the system's stable name, one of <see cref="SystemNames"/>. It seeds the system's random stream.</summary>
    string Name { get; }

    /// <summary>Registers this system's command handlers with the router.</summary>
    /// <param name="router">The router; registering a command type twice throws.</param>
    void Register(CommandRouter router);

    /// <summary>Runs the system for the current tick. It is called only on the system's <see cref="Cadence"/>.</summary>
    /// <param name="ctx">The simulation context for this tick.</param>
    void Run(SimContext ctx);
}
