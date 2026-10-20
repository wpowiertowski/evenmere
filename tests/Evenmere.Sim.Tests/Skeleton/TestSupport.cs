using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;

namespace Evenmere.Sim.Tests.Skeleton;

/// <summary>A command with a handler in the tests that register one.</summary>
internal sealed record PingCommand(string Label) : Command;

/// <summary>A command whose handler rejects it when <see cref="Value"/> is negative.</summary>
internal sealed record PickyCommand(int Value) : Command;

/// <summary>A command nobody registers a handler for.</summary>
internal sealed record UnhandledCommand : Command;

/// <summary>A command type that has a handler, and a subtype that does not (routing is by exact type).</summary>
internal record BaseCommand : Command;

/// <summary>A subtype of <see cref="BaseCommand"/> with no handler of its own.</summary>
internal sealed record DerivedCommand : BaseCommand;

/// <summary>An event the tests emit to check ordering.</summary>
internal sealed record TestEvent(long Tick, string Label) : SimEvent(Tick);

/// <summary>A command handler built from two lambdas.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
internal sealed class LambdaHandler<TCommand>(Func<SimContext, TCommand, CommandResult>? validate, Action<SimContext, TCommand>? apply)
    : ICommandHandler<TCommand>
    where TCommand : Command
{
    public CommandResult Validate(SimContext ctx, TCommand command) => validate?.Invoke(ctx, command) ?? CommandResult.Ok;

    public void Apply(SimContext ctx, TCommand command) => apply?.Invoke(ctx, command);
}

/// <summary>
/// A system that appends <c>"name@tick"</c> to a log each time it runs, optionally does more, and optionally registers
/// command handlers. Tests swap it in for a real stub with <c>SystemPipeline.Replace</c>.
/// </summary>
internal sealed class SpySystem(SystemId id, List<string> log, Action<SimContext>? onRun = null, Action<CommandRouter>? onRegister = null)
    : ISimSystem
{
    public string Name => SystemNames.Of(id);

    public void Register(CommandRouter router) => onRegister?.Invoke(router);

    public void Run(SimContext ctx)
    {
        log.Add($"{Name}@{ctx.Tick}");
        onRun?.Invoke(ctx);
    }
}
