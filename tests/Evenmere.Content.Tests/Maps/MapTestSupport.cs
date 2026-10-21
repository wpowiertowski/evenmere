using System.Text;
using Evenmere.Content.Maps;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Maps;

/// <summary>The fixture content for map tests: the minimal pack plus the eight terrains it lacks, so all twelve exist.</summary>
internal static class MapFixtures
{
    private static readonly Lazy<ContentDb> _content = new(() => ContentLoader.Load(ContentRoots).GetDbOrThrow());

    /// <summary>Gets the fixture pack: extra terrains and the fixture maps (<c>tests/fixtures/maps/pack</c>).</summary>
    public static string Pack => Path.Combine(TestPaths.RepoRoot, "tests", "fixtures", "maps", "pack");

    /// <summary>Gets the content roots the fixture maps load against.</summary>
    public static IReadOnlyList<string> ContentRoots => [TestPaths.MinimalPack, Pack];

    /// <summary>Gets the content with all twelve terrains, loaded once.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Loads a fixture map and fails the test if it has any diagnostic.</summary>
    /// <param name="id">The map ID.</param>
    /// <returns>The map.</returns>
    public static MapDef LoadClean(string id)
    {
        MapLoadResult result = MapLoader.Load(ContentRoots, id, Content);
        result.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        return result.Map ?? throw new ShouldAssertException("No diagnostics but no map either.");
    }
}

/// <summary>A throwaway content root holding one map folder, for tests that need a malformed or unusual map.</summary>
internal sealed class TempMap : IDisposable
{
    private readonly string _mapFolder;

    private TempMap(string id)
    {
        Id = id;
        Root = Path.Combine(Path.GetTempPath(), "evenmere-map-tests-" + Guid.NewGuid().ToString("N"));
        _mapFolder = Path.Combine(Root, "maps", id);
        Directory.CreateDirectory(_mapFolder);
    }

    /// <summary>Gets the map ID.</summary>
    public string Id { get; }

    /// <summary>Gets the temporary content root.</summary>
    public string Root { get; }

    /// <summary>
    /// Creates a valid 8 × 8 map of plains with the start site at (2, 2): only <c>map.json</c> and <c>terrain.txt</c>.
    /// Tests then overwrite or add the files they want to break.
    /// </summary>
    /// <param name="id">The map ID.</param>
    /// <returns>The temporary map.</returns>
    public static TempMap Plain(string id = "temp_map")
    {
        var map = new TempMap(id);
        map.Write("map.json", """{ "width": 8, "height": 8, "startSite": { "x": 2, "y": 2 } }""");
        map.Write("terrain.txt", Rows(8, 8, '.'));
        return map;
    }

    /// <summary>Builds a layer of identical characters.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="c">The character.</param>
    /// <returns>The text, rows ended by LF.</returns>
    public static string Rows(int width, int height, char c)
    {
        var text = new StringBuilder();
        for (int y = 0; y < height; y++)
        {
            text.Append(c, width).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Replaces one character of a layer of 8-wide rows (8 characters and a line feed per row).</summary>
    /// <param name="layer">The layer text.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="c">The new character.</param>
    /// <returns>The changed text.</returns>
    public static string With(string layer, int x, int y, char c)
    {
        int index = (y * 9) + x;
        return layer[..index] + c + layer[(index + 1)..];
    }

    /// <summary>Writes a file into the map folder, replacing it.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="text">The text.</param>
    public void Write(string name, string text) => File.WriteAllText(Path.Combine(_mapFolder, name), text, new UTF8Encoding(false));

    /// <summary>Writes a file's bytes into the map folder, replacing it.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="bytes">The bytes.</param>
    public void WriteBytes(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(_mapFolder, name), bytes);

    /// <summary>Loads the map against the fixture content, with this folder as the last root.</summary>
    /// <returns>The result.</returns>
    public MapLoadResult Load() => MapLoader.Load([.. MapFixtures.ContentRoots, Root], Id, MapFixtures.Content);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
