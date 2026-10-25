using System.Text.RegularExpressions;

namespace Evenmere.Game.Tests;

public sealed partial class KitTests
{
    private static readonly Regex _hexPattern = HexPattern();

    [Fact]
    public void Palette_TokensAreValidHex()
    {
        Rendering.Buildings.Kit.KitPalette.Count.ShouldBeGreaterThan(30);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (byte slot = 0; slot < Rendering.Buildings.Kit.KitPalette.Count; slot++)
        {
            string name = Rendering.Buildings.Kit.KitPalette.NameOf(slot);
            names.Add(name).ShouldBeTrue($"duplicate token {name}");
            _hexPattern.IsMatch(Rendering.Buildings.Kit.KitPalette.HexOf(slot)).ShouldBeTrue($"bad hex for {name}");
            Rendering.Buildings.Kit.KitPalette.TryGetSlot(name, out byte found).ShouldBeTrue();
            found.ShouldBe(slot);
        }
    }

    [Fact]
    public void Palette_MatchesArtBible()
    {
        Dictionary<string, string> bible = ParseBiblePalette();
        bible.ShouldNotBeEmpty();
        for (byte slot = 0; slot < Rendering.Buildings.Kit.KitPalette.Count; slot++)
        {
            string name = Rendering.Buildings.Kit.KitPalette.NameOf(slot);
            bible.ShouldContainKey(name, $"kit token {name} left the bible");
            bible[name].ShouldBe(Rendering.Buildings.Kit.KitPalette.HexOf(slot).ToUpperInvariant(), $"drift in {name}");
        }
    }

    private static Dictionary<string, string> ParseBiblePalette()
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        string? root = RepoRoot();
        if (root is null)
        {
            return tokens;
        }

        // Bible rows look like: | `token.name` | `#RRGGBB` | ... — the token and hex are always the first cells.
        Regex pattern = TokenRowPattern();
        foreach (string line in File.ReadLines(Path.Combine(root, "docs", "art", "palette.md")))
        {
            Match match = pattern.Match(line);
            if (match.Success)
            {
                tokens[match.Groups[1].Value] = match.Groups[2].Value.ToUpperInvariant();
            }
        }

        return tokens;
    }

    private static string? RepoRoot()
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

        return null;
    }

    [GeneratedRegex(@"^\|\s*`([^`]+)`\s*\|\s*`(#(?:[0-9a-fA-F]{6}))`")]
    private static partial Regex TokenRowPattern();

    [Fact]
    public void Palette_UnknownToken_FailsLookup()
    {
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("no.such.token", out _).ShouldBeFalse();
    }

    [Fact]
    public void Specs_AllParseWithoutWarnings()
    {
        foreach (string path in KitSpecPaths())
        {
            string id = Path.GetFileNameWithoutExtension(path);
            var warnings = new List<string>();
            var spec = Rendering.Buildings.Kit.KitSpec.Load(id, File.ReadAllText(path), warnings.Add);
            warnings.ShouldBeEmpty($"in {id}");
            spec.ShouldNotBeNull($"in {id}");
            spec!.Id.ShouldBe(id);
            spec.Width.ShouldBeGreaterThan(0f);
            spec.Depth.ShouldBeGreaterThan(0f);
            spec.Parts.ShouldNotBeEmpty($"in {id}");
        }
    }

    [Fact]
    public void Specs_MatchContentFootprints()
    {
        foreach (string path in KitSpecPaths())
        {
            string id = Path.GetFileNameWithoutExtension(path);
            var warnings = new List<string>();
            var spec = Rendering.Buildings.Kit.KitSpec.Load(id, File.ReadAllText(path), warnings.Add);
            spec.ShouldNotBeNull($"in {id}");
            if (TestContent.Db.Buildings.Names.Contains(id))
            {
                Content.Definitions.BuildingDef def = TestContent.Db.Buildings[TestContent.Db.Buildings.IdOf(id)];
                spec!.Width.ShouldBe((float)def.Footprint.Width, $"footprint drift in {id}");
                spec.Depth.ShouldBe((float)def.Footprint.Height, $"footprint drift in {id}");
            }
        }
    }

    [Fact]
    public void Specs_CoverEveryNonRibbonContentBuilding()
    {
        var missing = new List<string>();
        foreach (string name in TestContent.Db.Buildings.Names)
        {
            Content.Definitions.BuildingDef def = TestContent.Db.Buildings[TestContent.Db.Buildings.IdOf(name)];
            if (def.Tags.Contains("road") || def.Tags.Contains("bridge"))
            {
                continue;
            }

            if (!File.Exists(Path.Combine(KitDir(), name + ".json")))
            {
                missing.Add(name);
            }
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Specs_BadPart_WarnsAndSkips()
    {
        string json = """{"id":"x","footprint":[2,2],"parts":[{"part":"walls","x0":-1,"z0":-1,"x1":1,"z1":1},{"part":"nope"}]}""";
        var warnings = new List<string>();
        var spec = Rendering.Buildings.Kit.KitSpec.Load("x", json, warnings.Add);
        warnings.Count.ShouldBe(1);
        spec.ShouldNotBeNull();
        spec!.Parts.Count.ShouldBe(1);
    }

    [Fact]
    public void Specs_BadToken_FallsBackWithWarning()
    {
        string json = """{"id":"x","footprint":[2,2],"parts":[{"part":"walls","material":"no.such.token","x0":-1,"z0":-1,"x1":1,"z1":1}]}""";
        var warnings = new List<string>();
        var spec = Rendering.Buildings.Kit.KitSpec.Load("x", json, warnings.Add);
        warnings.Count.ShouldBe(1);
        spec.ShouldNotBeNull();
    }

    [Fact]
    public void Meshes_ComposeCleanGeometry()
    {
        var builder = new Rendering.Buildings.Kit.KitBuilder();
        foreach (string path in KitSpecPaths())
        {
            string id = Path.GetFileNameWithoutExtension(path);
            var warnings = new List<string>();
            var spec = Rendering.Buildings.Kit.KitSpec.Load(id, File.ReadAllText(path), warnings.Add);
            spec.ShouldNotBeNull($"in {id}");

            Rendering.Buildings.Kit.KitComposer.Build(builder, spec!);
            builder.VertexCount.ShouldBeGreaterThan(0, $"empty mesh in {id}");
            (builder.IndexCount % 3).ShouldBe(0, $"in {id}");
            builder.MaxY.ShouldBeGreaterThan(0f, $"flat mesh in {id}");
            builder.VertexCount.ShouldBeLessThan(4096, $"budget blown in {id}");

            float marginX = (spec!.Width / 2f) + 0.6f;
            float marginZ = (spec.Depth / 2f) + 0.6f;
            System.ReadOnlySpan<float> positions = builder.Vertices;
            for (int i = 0; i < builder.VertexCount; i++)
            {
                float x = positions[(i * 3) + 0];
                float z = positions[(i * 3) + 2];
                Math.Abs(x).ShouldBeLessThanOrEqualTo(marginX, $"stray vertex in {id}");
                Math.Abs(z).ShouldBeLessThanOrEqualTo(marginZ, $"stray vertex in {id}");
            }

            System.ReadOnlySpan<float> normals = builder.Normals;
            for (int i = 0; i < builder.VertexCount; i++)
            {
                float nx = normals[(i * 3) + 0];
                float ny = normals[(i * 3) + 1];
                float nz = normals[(i * 3) + 2];
                double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                length.ShouldBe(1.0, 0.01, $"bad normal in {id}");
            }

            System.ReadOnlySpan<byte> slots = builder.Slots;
            for (int i = 0; i < builder.VertexCount; i++)
            {
                ((int)slots[i]).ShouldBeLessThan(Rendering.Buildings.Kit.KitPalette.Count, $"bad slot in {id}");
            }
        }
    }

    [Fact]
    public void Meshes_ComposeDeterministically()
    {
        string path = Path.Combine(KitDir(), "market.json");
        var first = new Rendering.Buildings.Kit.KitBuilder();
        var second = new Rendering.Buildings.Kit.KitBuilder();
        var warnings = new List<string>();
        var spec = Rendering.Buildings.Kit.KitSpec.Load("market", File.ReadAllText(path), warnings.Add);
        spec.ShouldNotBeNull();

        Rendering.Buildings.Kit.KitComposer.Build(first, spec!);
        Rendering.Buildings.Kit.KitComposer.Build(second, spec!);
        first.VertexCount.ShouldBe(second.VertexCount);
        first.Vertices.ToArray().ShouldBe(second.Vertices.ToArray());
    }

    [Fact]
    public void Meshes_ScaffoldStandsAtFullHeight()
    {
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("wood.beam.dark", out byte post).ShouldBeTrue();
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("wood.plank.mid", out byte plank).ShouldBeTrue();
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("prop.rope", out byte rope).ShouldBeTrue();
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("wood.timber.light", out byte timber).ShouldBeTrue();
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("stone.light", out byte stone).ShouldBeTrue();
        Rendering.Buildings.Kit.KitPalette.TryGetSlot("prop.tarp", out byte tarp).ShouldBeTrue();
        var builder = new Rendering.Buildings.Kit.KitBuilder();
        builder.Begin();
        builder.Scaffold(2f, 2f, 1.8f, post, plank, rope, timber, stone, tarp);
        builder.VertexCount.ShouldBeGreaterThan(0);
        builder.MaxY.ShouldBe(1.8f, 0.01);
    }

    private static string KitDir() =>
        Path.Combine(RepoRoot() ?? throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory), "game", "data", "visuals", "kit");

    private static string[] KitSpecPaths() => Directory.GetFiles(KitDir(), "*.json");

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();
}
