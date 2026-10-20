using Evenmere.Content;

namespace Evenmere.Sim.Tests.Fixtures;

/// <summary>
/// Builds simulations for tests from the minimal content pack (<c>tests/fixtures/content/minimal</c>). The pack is
/// loaded once per test run and shared, because <see cref="ContentDb"/> is immutable. Later tasks extend the builder
/// with maps and buildings.
/// </summary>
internal sealed class WorldBuilder
{
    private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);
    private static readonly Lazy<ContentDb> _content = new(LoadMinimalContent);

    private ulong _seed = 42;

    /// <summary>Gets the repository root, the folder that holds <c>Evenmere.sln</c>.</summary>
    public static string RepoRoot => _repoRoot.Value;

    /// <summary>Gets the folder of the minimal content pack.</summary>
    public static string MinimalPackPath => Path.Combine(RepoRoot, "tests", "fixtures", "content", "minimal");

    /// <summary>Gets the content of the minimal pack, loaded once.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Sets the world seed; the default is 42.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>This builder.</returns>
    public WorldBuilder WithSeed(ulong seed)
    {
        _seed = seed;
        return this;
    }

    /// <summary>Creates a new empty world on the minimal content.</summary>
    /// <returns>The simulation, at tick zero.</returns>
    public Simulation Build() => Simulation.CreateNew(Content, new NewGameOptions { Seed = _seed });

    private static ContentDb LoadMinimalContent() => ContentLoader.Load(MinimalPackPath).GetDbOrThrow();

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
