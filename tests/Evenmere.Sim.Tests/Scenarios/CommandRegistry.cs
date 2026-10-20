using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Evenmere.Sim.Commands;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>One command a scenario file may name.</summary>
/// <param name="Name">The simple type name, which is what the file's <c>type</c> holds.</param>
/// <param name="CommandType">The concrete <see cref="Command"/> record.</param>
/// <param name="HandlerType">A test-only handler the runner registers, or <see langword="null"/> when the simulation serves the command itself.</param>
internal sealed record CommandRegistration(string Name, Type CommandType, Type? HandlerType)
{
    private static readonly MethodInfo _registerTyped = typeof(CommandRegistration).GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CommandRegistration.RegisterTyped is missing.");

    /// <summary>Registers a new instance of the test-only handler, if the command has one.</summary>
    /// <param name="router">The simulation's router.</param>
    public void RegisterHandler(CommandRouter router)
    {
        if (HandlerType is null)
        {
            return;
        }

        object handler = Activator.CreateInstance(HandlerType)
            ?? throw new InvalidOperationException($"{HandlerType.Name} could not be created.");
        _registerTyped.MakeGenericMethod(CommandType).Invoke(null, [router, handler]);
    }

    private static void RegisterTyped<TCommand>(CommandRouter router, object handler)
        where TCommand : Command => router.Register((ICommandHandler<TCommand>)handler);
}

/// <summary>
/// Maps a scenario file's command <c>type</c> to a <see cref="Command"/> record. Built by reflection over every concrete
/// command in <c>Evenmere.Sim</c> (so new commands need no registration), plus the test-assembly records marked with
/// <see cref="ScenarioCommandAttribute"/>. Names are simple type names and are case-sensitive.
/// </summary>
internal sealed class CommandRegistry
{
    private static readonly Lazy<CommandRegistry> _default = new(BuildDefault);

    private readonly SortedDictionary<string, CommandRegistration> _byName;

    private CommandRegistry(SortedDictionary<string, CommandRegistration> byName)
    {
        _byName = byName;
    }

    /// <summary>Gets the registry of the real commands and the marked test-only commands.</summary>
    public static CommandRegistry Default => _default.Value;

    /// <summary>Gets the registered names in ordinal order.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>Builds a registry, rejecting two commands with the same simple name.</summary>
    /// <param name="registrations">The commands.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="InvalidOperationException">Two registrations share a name.</exception>
    public static CommandRegistry Create(IEnumerable<CommandRegistration> registrations)
    {
        var byName = new SortedDictionary<string, CommandRegistration>(StringComparer.Ordinal);
        foreach (CommandRegistration registration in registrations)
        {
            if (!byName.TryAdd(registration.Name, registration))
            {
                throw new InvalidOperationException(
                    $"The command name \"{registration.Name}\" is ambiguous: it names both {byName[registration.Name].CommandType.FullName} and {registration.CommandType.FullName}. Rename one of them.");
            }
        }

        return new CommandRegistry(byName);
    }

    /// <summary>Finds a command by the name a scenario file uses.</summary>
    /// <param name="name">The simple type name.</param>
    /// <param name="registration">The command, when found.</param>
    /// <returns><see langword="true"/> if the name is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out CommandRegistration? registration) =>
        _byName.TryGetValue(name, out registration);

    /// <summary>Explains an unknown name: the names that exist, and a near miss when only the case differs.</summary>
    /// <param name="name">The unknown name.</param>
    /// <returns>A sentence for the error message.</returns>
    public string DescribeUnknown(string name)
    {
        string? nearMiss = _byName.Keys.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
        string hint = nearMiss is null ? string.Empty : $" Did you mean \"{nearMiss}\"? Names are case-sensitive.";
        string known = _byName.Count == 0 ? "none" : string.Join(", ", _byName.Keys);
        return $"unknown command type \"{name}\".{hint} Known command types: {known}.";
    }

    /// <summary>Deserialises a command's <c>args</c> into its record's constructor parameters.</summary>
    /// <param name="registration">The command.</param>
    /// <param name="args">The <c>args</c> object.</param>
    /// <param name="options">The options from <see cref="ScenarioJson.CreateOptions"/>.</param>
    /// <returns>The command.</returns>
    /// <exception cref="JsonException">The args do not fit the record.</exception>
    public static Command Deserialize(CommandRegistration registration, JsonElement args, JsonSerializerOptions options)
    {
        object? command = args.Deserialize(registration.CommandType, options);
        return command as Command ?? throw new JsonException($"The args produced no {registration.Name}.");
    }

    private static CommandRegistry BuildDefault()
    {
        Assembly simAssembly = typeof(Command).Assembly;
        Assembly testAssembly = typeof(CommandRegistry).Assembly;

        var registrations = new List<CommandRegistration>();
        foreach (Type type in simAssembly.GetTypes().Where(IsConcreteCommand))
        {
            registrations.Add(new CommandRegistration(type.Name, type, null));
        }

        foreach (Type type in testAssembly.GetTypes().Where(IsConcreteCommand))
        {
            if (type.GetCustomAttribute<ScenarioCommandAttribute>() is { } marker)
            {
                registrations.Add(FromMarkedType(type, marker.HandlerType));
            }
        }

        return Create(registrations);
    }

    private static bool IsConcreteCommand(Type type) =>
        type is { IsAbstract: false, ContainsGenericParameters: false } && type.IsSubclassOf(typeof(Command));

    /// <summary>Creates the registration for a test-assembly command marked with <see cref="ScenarioCommandAttribute"/>.</summary>
    /// <param name="command">The command record.</param>
    /// <param name="handler">The handler class named by the attribute, if any.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="InvalidOperationException">The handler does not serve the command.</exception>
    public static CommandRegistration FromMarkedType(Type command, Type? handler)
    {
        if (handler is not null && !typeof(ICommandHandler<>).MakeGenericType(command).IsAssignableFrom(handler))
        {
            throw new InvalidOperationException($"{handler.Name} must implement ICommandHandler<{command.Name}> to serve {command.Name}.");
        }

        return new CommandRegistration(command.Name, command, handler);
    }
}
