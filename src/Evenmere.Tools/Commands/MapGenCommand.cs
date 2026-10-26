using System.Text.Json;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Tools.MapGen;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Commands;

/// <summary>
/// <c>evenmere mapgen</c>: previews generated islands without opening Godot. Renders one island's layer to a PNG,
/// prints its ASCII terrain grid for agents, or writes a labelled contact sheet of a seed range.
/// </summary>
internal static class MapGenCommand
{
    /// <summary>Renders the preview.</summary>
    /// <remarks>
    /// Stdout is machine-readable: the ASCII grid when <paramref name="ascii"/> is set without an output file,
    /// otherwise a one-line JSON summary. Everything human-readable — load diagnostics and what was written — goes
    /// to <paramref name="error"/>.
    /// </remarks>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="seed">The generator seed for a single island.</param>
    /// <param name="size">The island size: 256, 320 or 384.</param>
    /// <param name="outPath">Where to write the PNG (or the ASCII text with <paramref name="ascii"/>); required except for ASCII to stdout.</param>
    /// <param name="layer">The layer: <c>terrain</c>, <c>elevation</c>, <c>regions</c>, <c>deposits</c> or <c>fertility:&lt;crop&gt;</c>.</param>
    /// <param name="ascii">Whether to write the ASCII terrain grid instead of a PNG.</param>
    /// <param name="gallery">A seed range like <c>1..20</c> for a contact sheet, or <see langword="null"/> for one island.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <returns><see cref="ExitCodes.Success"/>, <see cref="ExitCodes.ContentErrors"/> or <see cref="ExitCodes.Usage"/>.</returns>
    public static int Execute(
        IReadOnlyList<string> roots,
        int seed,
        int size,
        string? outPath,
        string layer,
        bool ascii,
        string? gallery,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return ExitCodes.ContentErrors;
        }

        if (gallery is not null)
        {
            return ExecuteGallery(result.Db, size, outPath, layer, gallery, output, error);
        }

        if (!ascii && !IsLayerName(layer))
        {
            error.WriteLine($"evenmere mapgen: unknown layer \"{layer}\": terrain, elevation, regions, deposits or fertility:<crop>.");
            return ExitCodes.Usage;
        }

        MapDef map;
        try
        {
            map = IslandGenerator.Generate(seed, size, result.Db);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            error.WriteLine($"evenmere mapgen: {exception.Message}");
            return ExitCodes.Usage;
        }
        catch (InvalidOperationException exception)
        {
            error.WriteLine($"evenmere mapgen: {exception.Message}");
            return ExitCodes.ContentErrors;
        }

        if (ascii)
        {
            string text = MapGenRenderer.ToAscii(map, result.Db);
            if (outPath is null)
            {
                output.Write(text);
                output.Flush();
            }
            else
            {
                WriteFile(outPath, text, error);
                output.WriteLine(SummaryJson(seed, size, "ascii", outPath));
                output.Flush();
            }

            error.WriteLine($"evenmere mapgen: seed {seed} at {size}x{size} is {map.Width}x{map.Height}; {map.Discoveries.Length} discoveries.");
            return ExitCodes.Success;
        }

        RenderedImage image;
        try
        {
            image = MapGenRenderer.Render(map, result.Db, layer);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"evenmere mapgen: {exception.Message}");
            return ExitCodes.Usage;
        }

        if (outPath is null)
        {
            error.WriteLine("evenmere mapgen: --out <file> is required for PNG output (omit it only with --ascii).");
            return ExitCodes.Usage;
        }

        WriteFile(outPath, PngWriter.Encode(image.Width, image.Height, image.Rgb), error);
        output.WriteLine(SummaryJson(seed, size, layer, outPath));
        output.Flush();
        error.WriteLine($"evenmere mapgen: seed {seed} at {size}x{size}, layer {layer}, wrote {image.Width}x{image.Height} to \"{outPath}\".");
        return ExitCodes.Success;
    }

    private static int ExecuteGallery(
        ContentDb content,
        int size,
        string? outPath,
        string layer,
        string gallery,
        TextWriter output,
        TextWriter error)
    {
        if (!layer.Equals("terrain", StringComparison.Ordinal))
        {
            error.WriteLine("evenmere mapgen: --gallery always renders the terrain layer; drop --layer.");
            return ExitCodes.Usage;
        }

        if (!TryParseGallery(gallery, out int first, out int last))
        {
            error.WriteLine($"evenmere mapgen: --gallery needs a range like 1..20 (got \"{gallery}\").");
            return ExitCodes.Usage;
        }

        if (outPath is null)
        {
            error.WriteLine("evenmere mapgen: --gallery needs --out <file>.");
            return ExitCodes.Usage;
        }

        if (size != IslandGenerator.MinSize && size != IslandGenerator.DefaultSize && size != IslandGenerator.MaxSize)
        {
            error.WriteLine($"evenmere mapgen: size {size} is not 256, 320 or 384.");
            return ExitCodes.Usage;
        }

        var maps = new List<MapDef>(last - first + 1);
        var seeds = new List<int>(last - first + 1);
        try
        {
            for (int seed = first; seed <= last; seed++)
            {
                maps.Add(IslandGenerator.Generate(seed, size, content));
                seeds.Add(seed);
            }
        }
        catch (InvalidOperationException exception)
        {
            error.WriteLine($"evenmere mapgen: {exception.Message}");
            return ExitCodes.ContentErrors;
        }

        RenderedImage sheet = MapGenRenderer.RenderGallery(maps, content, seeds);
        WriteFile(outPath, PngWriter.Encode(sheet.Width, sheet.Height, sheet.Rgb), error);
        output.WriteLine($"{{\"gallery\": \"{first}..{last}\", \"size\": {size}, \"out\": {JsonSerializer.Serialize(outPath)}}}");
        output.Flush();
        error.WriteLine($"evenmere mapgen: gallery seeds {first}..{last} at {size}x{size} wrote {sheet.Width}x{sheet.Height} to \"{outPath}\".");
        return ExitCodes.Success;
    }

    private static bool IsLayerName(string layer) =>
        layer.Equals("terrain", StringComparison.Ordinal)
        || layer.Equals("elevation", StringComparison.Ordinal)
        || layer.Equals("regions", StringComparison.Ordinal)
        || layer.Equals("deposits", StringComparison.Ordinal)
        || layer.StartsWith("fertility:", StringComparison.Ordinal);

    /// <summary>Parses a gallery range like <c>1..20</c>.</summary>
    /// <param name="text">The range text.</param>
    /// <param name="first">The first seed, when parsing succeeded.</param>
    /// <param name="last">The last seed, when parsing succeeded.</param>
    /// <returns><see langword="true"/> for <c>first..last</c> with <c>first &lt;= last</c> and at most 100 seeds.</returns>
    internal static bool TryParseGallery(string text, out int first, out int last)
    {
        first = 0;
        last = 0;
        string[] parts = text.Split("..", StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out first) || !int.TryParse(parts[1], out last))
        {
            return false;
        }

        return first <= last && last - first < 100;
    }

    private static string SummaryJson(int seed, int size, string layer, string outPath) =>
        $"{{\"seed\": {seed}, \"size\": {size}, \"layer\": {JsonSerializer.Serialize(layer)}, \"out\": {JsonSerializer.Serialize(outPath)}}}";

    private static void WriteFile(string outPath, string text, TextWriter error)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outPath, text);
        error.WriteLine($"evenmere mapgen: wrote {text.Length} characters to \"{outPath}\".");
    }

    private static void WriteFile(string outPath, byte[] bytes, TextWriter error)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(outPath, bytes);
        error.WriteLine($"evenmere mapgen: wrote {bytes.Length} bytes to \"{outPath}\".");
    }
}
