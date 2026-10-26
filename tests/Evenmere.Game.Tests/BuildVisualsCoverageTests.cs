using Evenmere.Content;
using Evenmere.Game.Data.Visuals;

namespace Evenmere.Game.Tests;

/// <summary>
/// The M2-36 grey-swatches guard: the build menu merges every placeholder mapping (the slice, production and
/// trees files), so every content building needs an entry in at least one of them. A building named nowhere
/// falls back to the neutral grey swatch.
/// </summary>
public sealed class BuildVisualsCoverageTests
{
    private static readonly string[] _visualFiles =
    [
        "era1-slice.json",
        "era1-production.json",
        "era1-trees.json",
    ];

    [Fact]
    public void EveryBuilding_HasAPlaceholderMapping()
    {
        ContentDb content = TestContent.Db;
        var merged = new Dictionary<string, BuildingVisual>(StringComparer.Ordinal);
        foreach (string file in _visualFiles)
        {
            string path = Path.Combine(FindRepoRoot(), "game", "data", "visuals", file);
            File.Exists(path).ShouldBeTrue($"The build menu merges '{file}'.");
            var map = BuildingVisualMap.Load(File.ReadAllText(path), _ => { });
            foreach (string name in content.Buildings.Names)
            {
                if (!merged.ContainsKey(name) && map.TryGet(name, out BuildingVisual visual))
                {
                    merged[name] = visual;
                }
            }
        }

        var missing = new List<string>();
        foreach (string name in content.Buildings.Names)
        {
            if (!merged.ContainsKey(name))
            {
                missing.Add(name);
            }
        }

        missing.ShouldBeEmpty($"These buildings would show a grey swatch: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void EveryMapping_HasAColour()
    {
        foreach (string file in _visualFiles)
        {
            string path = Path.Combine(FindRepoRoot(), "game", "data", "visuals", file);
            var map = BuildingVisualMap.Load(File.ReadAllText(path), _ => { });
            foreach (string name in TestContent.Db.Buildings.Names)
            {
                if (map.TryGet(name, out BuildingVisual visual))
                {
                    visual.Colour.Length.ShouldBeGreaterThan(0, $"'{name}' in '{file}' needs a colour.");
                }
            }
        }
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
