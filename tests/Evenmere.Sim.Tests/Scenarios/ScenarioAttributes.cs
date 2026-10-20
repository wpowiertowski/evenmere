namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// Makes a <see cref="Command"/> record in the test assembly available to scenario files by its simple type name. The
/// harness registers every command in <c>Evenmere.Sim</c> automatically; this attribute is for test-only commands that
/// prove the command path before the real commands exist, or that stand in for a system a scenario does not need.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class ScenarioCommandAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ScenarioCommandAttribute"/> class for a command with no handler of its own.</summary>
    public ScenarioCommandAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScenarioCommandAttribute"/> class for a command the harness serves.
    /// </summary>
    /// <param name="handlerType">
    /// A class with a public parameterless constructor that implements <c>ICommandHandler&lt;T&gt;</c> for the annotated
    /// command. The runner registers a new instance on each simulation, and only for scenarios that use the command.
    /// </param>
    public ScenarioCommandAttribute(Type handlerType)
    {
        HandlerType = handlerType;
    }

    /// <summary>Gets the handler class, or <see langword="null"/> when the command is registered by name only.</summary>
    public Type? HandlerType { get; }
}
