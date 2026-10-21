using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Needs;

/// <summary>
/// The vertical-slice content with one addition the slice does not use yet: <c>wool</c> as a villagers comfort need,
/// so the comfort multiplier and the comfort-never-declines rule can be tested. Built once, from a copy of the real
/// content directory with a patched tier file, so the real content stays untouched.
/// </summary>
internal static class ComfortContent
{
    private const string Villagers = """
        {
          "id": "villagers",
          "order": 2,
          "era": 1,
          "capacity": 10,
          "advanceFrom": "settlers",
          "upgradeCost": { "timber": 2 },
          "basicNeeds": {
            "goods": { "fish": 3, "homespun": 2, "bread": 4 },
            "services": ["market", "shrine"]
          },
          "comfortNeeds": { "wool": 2 },
          "outputs": { "knowledge": 10 },
          "start": true
        }
        """;

    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the slice content with the comfort need, loaded once.</summary>
    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-comfort-content-");
        try
        {
            CopyDirectory(Path.Combine(WorldBuilder.RepoRoot, "content"), copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "tiers", "villagers.json"), Villagers);
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
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}
