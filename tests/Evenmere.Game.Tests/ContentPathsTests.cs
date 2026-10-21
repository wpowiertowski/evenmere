using Evenmere.Game.Host;

namespace Evenmere.Game.Tests;

public sealed class ContentPathsTests
{
    [Fact]
    public void Resolve_EnvironmentVariable_Wins()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            ContentRoot root = ContentPaths.Resolve(dir, null, "/nowhere");
            root.Path.ShouldBe(Path.GetFullPath(dir));
            root.Source.ShouldContain("EVENMERE_CONTENT");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void Resolve_ExportedContent_WhenNoEnv()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            ContentRoot root = ContentPaths.Resolve(null, dir, "/nowhere");
            root.Path.ShouldBe(Path.GetFullPath(dir));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void Resolve_RepositoryContent_WhenErasExists()
    {
        string repo = FindRepoRoot();
        ContentRoot root = ContentPaths.Resolve(null, null, Path.Combine(repo, "game"));
        root.Path.ShouldBe(Path.Combine(repo, "content"));
    }

    [Fact]
    public void Resolve_MissingEnvironmentPath_Throws()
    {
        Should.Throw<DirectoryNotFoundException>(() => ContentPaths.Resolve("/definitely/not/here", null, "/nowhere"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
