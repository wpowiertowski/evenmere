using Evenmere.Content;
using Evenmere.Content.Maps;

namespace Evenmere.Tools.Tests;

/// <summary>Loads the real slice content and the starter island for bot tests, mirroring the sim's SiteFinder.</summary>
internal static class BotWorld
{
    private static readonly Lazy<ContentDb> _content = new(static () =>
        ContentLoader.Load(Path.Combine(RepoRoot, "content")).GetDbOrThrow());

    private static readonly Lazy<MapDef> _map = new(static () =>
        MapLoader.Load([Path.Combine(RepoRoot, "content")], "starter", Content).GetMapOrThrow());

    private static string RepoRoot
    {
        get
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

    /// <summary>Gets the real slice content.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the starter island.</summary>
    public static MapDef Map => _map.Value;
}
