using Godot;

namespace Evenmere.Game.Host;

/// <summary>Where the content root was found, and the rule that chose it.</summary>
/// <param name="Path">The absolute directory to give to <c>ContentLoader</c>.</param>
/// <param name="Source">A short description of the rule, for the log.</param>
internal readonly record struct ContentRoot(string Path, string Source);

/// <summary>
/// Finds the content root the game loads. The rules apply in this order:
/// <list type="number">
/// <item><description><c>$EVENMERE_CONTENT</c>, if set.</description></item>
/// <item><description><c>res://content</c>, if it exists (exported builds).</description></item>
/// <item><description>
/// The repository's <c>content</c> folder, found by walking up from the project folder to <c>Evenmere.sln</c>, if it has
/// <c>eras/</c>; otherwise the minimal fixture pack, <c>tests/fixtures/content/minimal</c>. The fallback lasts until M1-02
/// fills <c>content/</c>.
/// </description></item>
/// </list>
/// </summary>
public static class ContentPaths
{
    /// <summary>The environment variable that overrides every other rule.</summary>
    public const string EnvironmentVariable = "EVENMERE_CONTENT";

    private const string SolutionFileName = "Evenmere.sln";

    private static bool _logged;

    /// <summary>Resolves the content root and logs the choice the first time it is called.</summary>
    /// <returns>The absolute path of the content root.</returns>
    /// <exception cref="DirectoryNotFoundException">The chosen root, or the repository, does not exist.</exception>
    public static string Resolve()
    {
        string? exported = DirAccess.DirExistsAbsolute("res://content") ? ProjectSettings.GlobalizePath("res://content") : null;
        ContentRoot root = Resolve(System.Environment.GetEnvironmentVariable(EnvironmentVariable), exported, ProjectSettings.GlobalizePath("res://"));

        if (!_logged)
        {
            _logged = true;
            GD.Print($"[Evenmere] content root: {root.Path} ({root.Source})");
        }

        return root.Path;
    }

    /// <summary>The resolution rules without any Godot calls, so they can be reasoned about on their own.</summary>
    /// <param name="environmentValue">The value of <c>$EVENMERE_CONTENT</c>, or <see langword="null"/> or empty if unset.</param>
    /// <param name="exportedContentDir">The real path of <c>res://content</c> if that folder exists, otherwise <see langword="null"/>.</param>
    /// <param name="projectDir">The real path of the Godot project folder.</param>
    /// <returns>The chosen root.</returns>
    internal static ContentRoot Resolve(string? environmentValue, string? exportedContentDir, string projectDir)
    {
        if (!string.IsNullOrEmpty(environmentValue))
        {
            string path = Path.GetFullPath(environmentValue);
            return Directory.Exists(path)
                ? new ContentRoot(path, $"${EnvironmentVariable}")
                : throw new DirectoryNotFoundException($"${EnvironmentVariable} points at '{path}', which does not exist.");
        }

        if (exportedContentDir is not null)
        {
            return new ContentRoot(Path.GetFullPath(exportedContentDir), "res://content");
        }

        string repoRoot = FindRepositoryRoot(projectDir);
        string content = Path.Combine(repoRoot, "content");
        if (Directory.Exists(Path.Combine(content, "eras")))
        {
            return new ContentRoot(content, "repository content/");
        }

        string fixture = Path.Combine(repoRoot, "tests", "fixtures", "content", "minimal");
        return Directory.Exists(fixture)
            ? new ContentRoot(fixture, "minimal fixture pack; content/eras does not exist yet")
            : throw new DirectoryNotFoundException($"No content found: '{content}' has no eras/ and '{fixture}' does not exist.");
    }

    private static string FindRepositoryRoot(string projectDir)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(projectDir));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"{SolutionFileName} was not found above '{projectDir}'; set ${EnvironmentVariable} to a content folder.");
    }
}
