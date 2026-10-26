using Evenmere.Tools.Commands;

namespace Evenmere.Tools.Tests.MapGen;

/// <summary>Unit tests for the mapgen gallery ranges and the command's usage errors (no islands are generated here).</summary>
public sealed class MapGenCommandTests
{
    [Theory]
    [InlineData("1..20", 1, 20)]
    [InlineData("5..5", 5, 5)]
    [InlineData("0..3", 0, 3)]
    public void TryParseGallery_ValidRanges_Parses(string text, int first, int last)
    {
        MapGenCommand.TryParseGallery(text, out int parsedFirst, out int parsedLast).ShouldBeTrue();
        parsedFirst.ShouldBe(first);
        parsedLast.ShouldBe(last);
    }

    [Theory]
    [InlineData("20..1")]
    [InlineData("1-20")]
    [InlineData("1.20")]
    [InlineData("abc")]
    [InlineData("1..")]
    [InlineData("..20")]
    [InlineData("1..200")]
    public void TryParseGallery_BadRanges_Rejects(string text)
    {
        MapGenCommand.TryParseGallery(text, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Execute_UnknownLayer_IsUsageWithoutGenerating()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = MapGenCommand.Execute(
            [RepoContent()],
            seed: 1,
            size: 320,
            outPath: null,
            layer: "roads",
            ascii: false,
            gallery: null,
            output,
            error);

        exit.ShouldBe(2);
    }

    [Fact]
    public void Execute_BadSize_IsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = MapGenCommand.Execute(
            [RepoContent()],
            seed: 1,
            size: 100,
            outPath: Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            layer: "terrain",
            ascii: false,
            gallery: null,
            output,
            error);

        exit.ShouldBe(2);
    }

    [Fact]
    public void Execute_GalleryWithoutOut_IsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = MapGenCommand.Execute(
            [RepoContent()],
            seed: 1,
            size: 320,
            outPath: null,
            layer: "terrain",
            ascii: false,
            gallery: "1..2",
            output,
            error);

        exit.ShouldBe(2);
    }

    [Fact]
    public void Execute_PngWithoutOut_IsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = MapGenCommand.Execute(
            [RepoContent()],
            seed: 1,
            size: 320,
            outPath: null,
            layer: "terrain",
            ascii: false,
            gallery: null,
            output,
            error);

        exit.ShouldBe(2);
    }

    private static string RepoContent()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return Path.Combine(directory.FullName, "content");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
