using System.Text;

namespace Evenmere.Game.Tests;

/// <summary>
/// Budget guards for the ART-04 hand-drawn-look textures: every PNG is a
/// power of two no larger than 1024 x 1024, and the art track's images stay
/// under the 25 MB total. The checks parse PNG headers directly so they run
/// without Godot.
/// </summary>
public sealed class TextureBudgetTests
{
    private static readonly string[] _expectedTextures =
    [
        "grain/paper_grain.png",
        "grain/wash.png",
        "water/ripples.png",
        "terrain/plains.png",
        "terrain/forest.png",
        "terrain/beach.png",
        "terrain/hills.png",
        "terrain/highlands.png",
        "terrain/peaks.png",
        "terrain/fen.png",
        "terrain/marsh.png",
        "terrain/reef.png",
        "terrain/sea.png",
        "terrain/deep.png",
        "terrain/river.png",
        "materials/palette_atlas.png",
    ];

    private const long TextureDirectoryBudgetBytes = 8L * 1024L * 1024L;
    private const long ArtTrackBudgetBytes = 25L * 1024L * 1024L;

    [Fact]
    public void Manifest_EveryBriefedTextureExists()
    {
        string root = TextureRoot();
        var missing = new List<string>();
        foreach (string rel in _expectedTextures)
        {
            if (!File.Exists(Path.Combine(root, rel)))
            {
                missing.Add(rel);
            }
        }

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Textures_ArePowerOfTwo_AtMost1024()
    {
        string root = TextureRoot();
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            (int width, int height) = ReadDimensions(file);
            if (!IsPowerOfTwo(width) || !IsPowerOfTwo(height) || width > 1024 || height > 1024)
            {
                failures.Add($"{Path.GetRelativePath(root, file)}: {width}x{height}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Textures_FitArtTrackBudgets()
    {
        string root = TextureRoot();
        long texturesBytes = Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        texturesBytes.ShouldBeLessThanOrEqualTo(TextureDirectoryBudgetBytes);

        long trackBytes = TrackImageBytes(FindRepoRoot());
        trackBytes.ShouldBeLessThanOrEqualTo(ArtTrackBudgetBytes);
    }

    private static string TextureRoot()
    {
        return Path.Combine(FindRepoRoot(), "game", "assets", "textures");
    }

    private static long TrackImageBytes(string repoRoot)
    {
        long total = 0;
        string[] imageDirs = ["game" + Path.DirectorySeparatorChar + "assets",
            "docs" + Path.DirectorySeparatorChar + "art"];
        foreach (string dir in imageDirs)
        {
            string full = Path.Combine(repoRoot, dir);
            if (Directory.Exists(full))
            {
                total += Directory.EnumerateFiles(full, "*.png", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
        }

        return total;
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes.Length.ShouldBeGreaterThan(24, $"Truncated PNG: {path}");
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        bytes[..8].ShouldBe(signature, $"Not a PNG: {path}");
        Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("IHDR", $"Missing IHDR: {path}");
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
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

        throw new DirectoryNotFoundException("Could not find the repository root (Evenmere.sln).");
    }
}
