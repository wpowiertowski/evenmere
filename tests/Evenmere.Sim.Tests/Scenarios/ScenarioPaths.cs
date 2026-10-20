using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>Where scenario files and the content they name live, relative to the repository root.</summary>
internal static class ScenarioPaths
{
    /// <summary>Gets the repository root, the folder that holds <c>Evenmere.sln</c>.</summary>
    public static string RepoRoot => WorldBuilder.RepoRoot;

    /// <summary>Gets the folder that discovery searches, <c>tests/fixtures/scenarios</c>.</summary>
    public static string ScenarioDirectory => Path.Combine(RepoRoot, "tests", "fixtures", "scenarios");

    /// <summary>Resolves a path written in a scenario file, which is relative to the repository root.</summary>
    /// <param name="repoRelativePath">The path from the file.</param>
    /// <returns>The absolute path.</returns>
    public static string Resolve(string repoRelativePath) => Path.GetFullPath(Path.Combine(RepoRoot, repoRelativePath));

    /// <summary>
    /// Shortens a path for messages: relative to the repository root with forward slashes when it is inside the
    /// repository, otherwise the full path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path to show.</returns>
    public static string Display(string path)
    {
        string full = Path.GetFullPath(path);
        string root = RepoRoot + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal)
            ? full[root.Length..].Replace(Path.DirectorySeparatorChar, '/')
            : full;
    }
}
