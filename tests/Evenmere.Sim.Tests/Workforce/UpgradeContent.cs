using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Workforce;

/// <summary>
/// The minimal content pack plus a <c>test_upgraded_storehouse</c>: an upgrade of the storehouse staffed by two
/// villagers instead of two settlers, mirroring <c>stone_storehouse</c>. Built once, from a copy of the minimal pack
/// with one extra building file, so the real content stays untouched and the overlay loads even while Era I content is
/// still landing.
/// </summary>
internal static class UpgradeContent
{
    private const string UpgradedStorehouse = """
        {
          "id": "test_upgraded_storehouse",
          "category": "infrastructure",
          "era": 1,
          "footprint": [2, 2],
          "cost": { "timber": 1 },
          "buildDays": 1,
          "workforce": { "tier": "villagers", "count": 2 },
          "storage": { "reach": 26, "carts": 3 },
          "upgradeOf": "storehouse"
        }
        """;

    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the minimal pack with the extra upgraded storehouse.</summary>
    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-upgrade-content-");
        try
        {
            CopyDirectory(WorldBuilder.MinimalPackPath, copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "test_upgraded_storehouse.json"), UpgradedStorehouse);
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
