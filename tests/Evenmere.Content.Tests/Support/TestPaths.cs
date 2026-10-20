namespace Evenmere.Content.Tests.Support;

/// <summary>Finds files of the repository from the test's output directory.</summary>
internal static class TestPaths
{
    private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);

    /// <summary>Gets the repository root, the folder that holds <c>Evenmere.sln</c>.</summary>
    public static string RepoRoot => _repoRoot.Value;

    /// <summary>Gets the minimal fixture pack, <c>tests/fixtures/content/minimal</c>.</summary>
    public static string MinimalPack => Path.Combine(RepoRoot, "tests", "fixtures", "content", "minimal");

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
