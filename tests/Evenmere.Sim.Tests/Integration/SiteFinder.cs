using Evenmere.Content;
using Evenmere.Content.Maps;

namespace Evenmere.Sim.Tests.Integration;

/// <summary>
/// Loads the real slice content and the starter island and starts a new game on it, the same way the CLI and the game
/// host do. Integration tests use this instead of <c>WorldBuilder</c> (which builds the empty world on the minimal pack).
/// </summary>
internal static class SiteFinder
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

    /// <summary>Starts a new game on the starter island.</summary>
    /// <param name="seed">The world seed.</param>
    /// <returns>The simulation at tick zero.</returns>
    public static Simulation NewGame(ulong seed = 1) =>
        Simulation.CreateNew(Content, new NewGameOptions { Seed = seed, Map = Map });
}
