namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// A scenario file that cannot be run: it is malformed, names an unknown command, event or good, or uses a feature that
/// does not exist yet. These are authoring mistakes, so they fail the test with a message that names the file and the
/// offending property or command index. An expectation that is merely not met is not an exception; it is a failed
/// <see cref="ScenarioResult"/>.
/// </summary>
internal sealed class ScenarioException : Exception
{
    public ScenarioException(string message)
        : base(message)
    {
    }

    public ScenarioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
