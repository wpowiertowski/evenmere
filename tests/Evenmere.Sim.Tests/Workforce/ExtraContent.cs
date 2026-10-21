using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Workforce;

/// <summary>
/// The vertical-slice content plus a <c>test_workshop</c> that needs five settlers, so three of them ask for exactly
/// fifteen workers. Built once, from a copy of the real content directory with one extra building file, so the real
/// content stays untouched.
/// </summary>
internal static class ExtraContent
{
    private const string Workshop = """
        {
          "id": "test_workshop", "category": "production", "era": 1, "footprint": [2, 2],
          "cost": { "timber": 1 }, "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 5 },
          "start": true,
          "tags": ["processing"]
        }
        """;

    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the slice content with the extra workshop.</summary>
    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-workforce-content-");
        try
        {
            CopyDirectory(Path.Combine(WorldBuilder.RepoRoot, "content"), copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "test_workshop.json"), Workshop);
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
