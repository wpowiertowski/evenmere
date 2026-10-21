using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Production;

/// <summary>
/// The vertical-slice content plus two buildings that exercise the rules the slice does not use yet: a <c>lookout</c> with
/// two <c>near</c> site bonuses, and a <c>test_quarry</c> with a footprint-deposit yield. Built once, from a copy of the real
/// content directory with two extra building files, so the real content stays untouched.
/// </summary>
internal static class ExtraContent
{
    private const string Lookout = """
        {
          "id": "lookout", "category": "production", "era": 1, "footprint": [2, 2],
          "cost": { "timber": 1 }, "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 1 },
          "recipe": "shear_wool",
          "siteBonus": [
            { "when": { "near": { "building": "storehouse", "radius": 4 } }, "stat": "productivity", "percent": 10 },
            { "when": { "near": { "tag": "farm", "radius": 3 } }, "stat": "productivity", "percent": -5 }
          ]
        }
        """;

    private const string Quarry = """
        {
          "id": "test_quarry", "category": "production", "era": 1, "footprint": [2, 4],
          "placement": { "deposit": "stone", "areaYield": { "kind": "footprint_deposit" } },
          "cost": { "timber": 1 }, "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 1 },
          "recipe": "cut_wood"
        }
        """;

    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the slice content with the two extra buildings.</summary>
    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-production-content-");
        try
        {
            CopyDirectory(Path.Combine(WorldBuilder.RepoRoot, "content"), copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "lookout.json"), Lookout);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "test_quarry.json"), Quarry);
            return ContentLoader.Load(copy.FullName).GetDbOrThrow();
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
