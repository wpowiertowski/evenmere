namespace Evenmere.Tools.MapGen;

/// <summary>A rendered preview image: flat RGB pixels, top row first.</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Rgb">The pixels: <c>Width × Height × 3</c> bytes in R, G, B order.</param>
internal sealed record RenderedImage(int Width, int Height, byte[] Rgb);
