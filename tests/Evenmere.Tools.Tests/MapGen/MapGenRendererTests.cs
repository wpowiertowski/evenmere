using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Tools.MapGen;

namespace Evenmere.Tools.Tests.MapGen;

/// <summary>Unit tests for the mapgen layers: sizes, the terrain palette, discovery markers and label sheets.</summary>
public sealed class MapGenRendererTests
{
    private static readonly (byte R, byte G, byte B) _sea = (0x33, 0x6B, 0x80);

    private static readonly (byte R, byte G, byte B) _river = (0x5E, 0x93, 0xA3);

    private static readonly (byte R, byte G, byte B) _wonderMark = (0xFF, 0xF8, 0xE8);

    private static readonly (byte R, byte G, byte B) _ruinMark = (0xB2, 0x5A, 0x38);

    private static readonly (byte R, byte G, byte B) _wreckMark = (0xF2, 0xEF, 0xE2);

    [Fact]
    public void Render_TerrainSeed1_Is320AndUsesSeaAndRiver()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        RenderedImage image = MapGenRenderer.Render(map, BotWorld.Content, "terrain");

        image.Width.ShouldBe(320);
        image.Height.ShouldBe(320);
        image.Rgb.Length.ShouldBe(320 * 320 * 3);
        Contains(image, _sea).ShouldBeTrue("the island sits in open sea");
        Contains(image, _river).ShouldBeTrue("seed 1 has traced rivers");
    }

    [Theory]
    [InlineData("elevation")]
    [InlineData("regions")]
    [InlineData("deposits")]
    [InlineData("fertility:grain")]
    [InlineData("fertility:sugar_beet")]
    public void Render_KnownLayers_HaveImageSize(string layer)
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        RenderedImage image = MapGenRenderer.Render(map, BotWorld.Content, layer);

        (image.Width, image.Height).ShouldBe((320, 320));
        image.Rgb.Length.ShouldBe(320 * 320 * 3);
    }

    [Fact]
    public void Render_UnknownLayer_Throws()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        Should.Throw<ArgumentException>(() => MapGenRenderer.Render(map, BotWorld.Content, "roads"));
    }

    [Fact]
    public void Render_UnknownCrop_Throws()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        Should.Throw<ArgumentException>(() => MapGenRenderer.Render(map, BotWorld.Content, "fertility:potatoes"));
    }

    [Fact]
    public void Render_Elevation_IsGrayscale()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        RenderedImage image = MapGenRenderer.Render(map, BotWorld.Content, "elevation");

        for (int i = 0; i < map.TileCount; i += 997)
        {
            image.Rgb[i * 3].ShouldBe(image.Rgb[(i * 3) + 1]);
            image.Rgb[i * 3].ShouldBe(image.Rgb[(i * 3) + 2]);
        }
    }

    [Fact]
    public void Render_TerrainSeed1_ShowsEveryPlacedDiscoveryKind()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);
        map.Discoveries.Length.ShouldBeGreaterThan(0);

        RenderedImage image = MapGenRenderer.Render(map, BotWorld.Content, "terrain");

        foreach (IGrouping<Evenmere.Content.Definitions.DiscoveryKind, Evenmere.Content.Maps.MapDiscovery> group in map.Discoveries.GroupBy(static d => d.Kind))
        {
            (byte R, byte G, byte B) mark = group.Key switch
            {
                Evenmere.Content.Definitions.DiscoveryKind.NaturalWonder => _wonderMark,
                Evenmere.Content.Definitions.DiscoveryKind.Ruin => _ruinMark,
                Evenmere.Content.Definitions.DiscoveryKind.Shipwreck => _wreckMark,
                _ => ((byte)0x9B, (byte)0x9B, (byte)0x95),
            };
            Contains(image, mark).ShouldBeTrue($"a {group.Key} marker is drawn");
        }
    }

    [Fact]
    public void ToAscii_Seed1_Is320LinesOfLegendCharacters()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        string ascii = MapGenRenderer.ToAscii(map, BotWorld.Content);

        string[] lines = ascii.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(320);
        foreach (string line in lines)
        {
            line.Length.ShouldBe(320);
        }

        ascii.ShouldMatch(@"^[.=Tw_,MfA~wo^,\r\n]+$");
    }

    [Fact]
    public void RenderGallery_TwoIslands_StacksCellsWithLabels()
    {
        MapDef first = IslandGenerator.Generate(1, 320, BotWorld.Content);
        MapDef second = IslandGenerator.Generate(2, 320, BotWorld.Content);

        RenderedImage sheet = MapGenRenderer.RenderGallery([first, second], BotWorld.Content, [1, 2]);

        sheet.Width.ShouldBe(640);
        sheet.Height.ShouldBe(320 + MapGenRenderer.LabelHeight);
        // The first rows are the label strip: ink background with at least one lit label pixel.
        bool hasInk = false;
        bool hasBack = false;
        for (int y = 0; y < MapGenRenderer.LabelHeight; y++)
        {
            for (int x = 0; x < sheet.Width; x++)
            {
                int i = (y * sheet.Width) + x;
                byte r = sheet.Rgb[i * 3];
                byte g = sheet.Rgb[(i * 3) + 1];
                byte b = sheet.Rgb[(i * 3) + 2];
                hasBack |= r == 0x2E && g == 0x26 && b == 0x20;
                hasInk |= r == 0xF4 && g == 0xEA && b == 0xD2;
            }
        }

        hasBack.ShouldBeTrue("label strips have the ink background");
        hasInk.ShouldBeTrue("labels spell the seed");
    }

    [Fact]
    public void RenderGallery_MismatchedSizes_Throws()
    {
        MapDef first = IslandGenerator.Generate(1, 256, BotWorld.Content);
        MapDef second = IslandGenerator.Generate(2, 320, BotWorld.Content);

        Should.Throw<ArgumentException>(() => MapGenRenderer.RenderGallery([first, second], BotWorld.Content, [1, 2]));
    }

    private static bool Contains(RenderedImage image, (byte R, byte G, byte B) color)
    {
        for (int i = 0; i < image.Rgb.Length; i += 3)
        {
            if (image.Rgb[i] == color.R && image.Rgb[i + 1] == color.G && image.Rgb[i + 2] == color.B)
            {
                return true;
            }
        }

        return false;
    }
}
