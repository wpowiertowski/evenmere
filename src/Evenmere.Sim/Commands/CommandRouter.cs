using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Commands;

/// <summary>
/// Maps each command type to the handler that serves it. Systems register their handlers from
/// <see cref="ISimSystem.Register"/>; the simulation then applies queued commands through the router at the start of
/// every tick. A command is routed by its exact runtime type, so a handler for a base record never serves a subtype.
/// </summary>
public sealed class CommandRouter
{
    // Looked up by exact type and never iterated, so its enumeration order cannot reach state.
    private readonly Dictionary<Type, HandlerEntry> _handlers = [];
    private readonly List<Type> _registrationOrder = [];
    private SystemId? _registeringSystem;

    /// <summary>Gets the command types that have a handler, in ordinal order of their full names.</summary>
    public IReadOnlyList<Type> RegisteredCommandTypes
    {
        get
        {
            var types = new List<Type>(_registrationOrder);
            types.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return types;
        }
    }

    /// <summary>Registers the handler for a command type.</summary>
    /// <typeparam name="TCommand">The exact command type the handler serves.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A handler is already registered for <typeparamref name="TCommand"/>.</exception>
    public void Register<TCommand>(ICommandHandler<TCommand> handler)
        where TCommand : Command
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.ContainsKey(typeof(TCommand)))
        {
            throw new InvalidOperationException($"A handler for {typeof(TCommand).FullName} is already registered.");
        }

        _handlers.Add(typeof(TCommand), new HandlerEntry<TCommand>(handler, _registeringSystem));
        _registrationOrder.Add(typeof(TCommand));
    }

    /// <summary>Tests whether a command type has a handler.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <returns><see langword="true"/> if a handler is registered for exactly that type.</returns>
    public bool IsRegistered<TCommand>()
        where TCommand : Command => _handlers.ContainsKey(typeof(TCommand));

    /// <summary>
    /// Records which system is registering, so the handlers it adds draw random numbers from that system's own
    /// stream. The pipeline calls this around each <see cref="ISimSystem.Register"/>.
    /// </summary>
    /// <param name="system">The registering system, or <see langword="null"/> when a handler has no owner.</param>
    internal void SetRegisteringSystem(SystemId? system) => _registeringSystem = system;

    /// <summary>
    /// Applies the commands queued before this call, oldest first. Commands queued while this runs wait for the next
    /// tick, so a handler can never starve the tick by queueing more work.
    /// </summary>
    /// <param name="queue">The queue to drain.</param>
    /// <param name="ctx">The simulation context.</param>
    internal void ApplyQueued(CommandQueue queue, SimContext ctx)
    {
        int waiting = queue.Count;
        for (int i = 0; i < waiting && queue.TryDequeue(out QueuedCommand queued); i++)
        {
            Dispatch(ctx, queued.Command);
        }
    }

    private void Dispatch(SimContext ctx, Command command)
    {
        if (!_handlers.TryGetValue(command.GetType(), out HandlerEntry? entry))
        {
            ctx.Events.Emit(new CommandRejected(
                ctx.Tick,
                command,
                RejectReason.UnknownCommand,
                $"No handler is registered for {command.GetType().Name}."));
            return;
        }

        if (entry.Owner is { } owner)
        {
            ctx.CurrentSystem = owner;
        }

        CommandResult result = entry.Validate(ctx, command);
        if (result.IsRejected)
        {
            ctx.Events.Emit(new CommandRejected(ctx.Tick, command, result.Reason, result.Detail));
            return;
        }

        entry.Apply(ctx, command);
    }

    private abstract class HandlerEntry(SystemId? owner)
    {
        public SystemId? Owner { get; } = owner;

        public abstract CommandResult Validate(SimContext ctx, Command command);

        public abstract void Apply(SimContext ctx, Command command);
    }

    private sealed class HandlerEntry<TCommand>(ICommandHandler<TCommand> handler, SystemId? owner) : HandlerEntry(owner)
        where TCommand : Command
    {
        // The dictionary key is the exact type, so the cast cannot fail.
        public override CommandResult Validate(SimContext ctx, Command command) => handler.Validate(ctx, (TCommand)command);

        public override void Apply(SimContext ctx, Command command) => handler.Apply(ctx, (TCommand)command);
    }
}
