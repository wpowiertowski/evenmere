using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// Holds the simulation context for the parts of the progression system that are built before it exists (the queries, which
/// the roster creates first). <see cref="SimContext"/> binds the effect applier when it is created, and the applier hands the
/// context to this holder.
/// </summary>
internal sealed class SimContextHolder
{
    private SimContext? _context;

    /// <summary>Gets a value indicating whether a context has been bound.</summary>
    public bool IsBound => _context is not null;

    /// <summary>Stores the context.</summary>
    /// <param name="context">The simulation's context.</param>
    public void Bind(SimContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets the context.</summary>
    /// <returns>The bound context.</returns>
    /// <exception cref="InvalidOperationException">No context has been bound, which means the progression system is being used outside a simulation.</exception>
    public SimContext Require() =>
        _context ?? throw new InvalidOperationException("The progression system is not bound to a simulation context yet; create a SimContext over its services first.");
}
