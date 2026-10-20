namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// The event type names a scenario's <c>expect.events</c> may use: the simple names of every concrete
/// <see cref="SimEvent"/> record in <c>Evenmere.Sim</c> and in the test assembly. Checking names up front means a
/// misspelt type fails with a list of the real ones, instead of counting zero events and passing an <c>exactly: 0</c>.
/// </summary>
internal sealed class EventRegistry
{
    private static readonly Lazy<EventRegistry> _default = new(BuildDefault);

    private readonly SortedSet<string> _names;

    private EventRegistry(SortedSet<string> names)
    {
        _names = names;
    }

    /// <summary>Gets the registry of every event type in the simulation and the tests.</summary>
    public static EventRegistry Default => _default.Value;

    /// <summary>Gets the registered names in ordinal order.</summary>
    public IReadOnlyCollection<string> Names => _names;

    /// <summary>Builds a registry from event types, rejecting two with the same simple name.</summary>
    /// <param name="types">The event types.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="InvalidOperationException">Two types share a simple name.</exception>
    public static EventRegistry Create(IEnumerable<Type> types)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in types)
        {
            if (!seen.TryAdd(type.Name, type))
            {
                throw new InvalidOperationException(
                    $"The event name \"{type.Name}\" is ambiguous: it names both {seen[type.Name].FullName} and {type.FullName}. Rename one of them.");
            }

            names.Add(type.Name);
        }

        return new EventRegistry(names);
    }

    /// <summary>Tests whether a name is an event type.</summary>
    /// <param name="name">The simple type name.</param>
    /// <returns><see langword="true"/> if it is registered.</returns>
    public bool Contains(string name) => _names.Contains(name);

    /// <summary>Explains an unknown name: the names that exist, and a near miss when only the case differs.</summary>
    /// <param name="name">The unknown name.</param>
    /// <returns>A sentence for the error message.</returns>
    public string DescribeUnknown(string name)
    {
        string? nearMiss = _names.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
        string hint = nearMiss is null ? string.Empty : $" Did you mean \"{nearMiss}\"? Names are case-sensitive.";
        return $"unknown event type \"{name}\".{hint} Known event types: {string.Join(", ", _names)}.";
    }

    private static EventRegistry BuildDefault() => Create(
        typeof(SimEvent).Assembly.GetTypes()
            .Concat(typeof(EventRegistry).Assembly.GetTypes())
            .Where(static type => type is { IsAbstract: false, ContainsGenericParameters: false } && type.IsSubclassOf(typeof(SimEvent))));
}
