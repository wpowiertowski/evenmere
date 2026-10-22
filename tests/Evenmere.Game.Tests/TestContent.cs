using Evenmere.Content;

namespace Evenmere.Game.Tests;

/// <summary>The real content pack and locale for view-model tests, loaded once per test run.</summary>
internal static class TestContent
{
    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the real content database.</summary>
    internal static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return ContentLoader.Load(Path.Combine(directory.FullName, "content")).GetDbOrThrow();
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
