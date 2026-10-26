using Evenmere.Content;
using Evenmere.Content.Definitions;

namespace Evenmere.Game.Rendering.Overlays;

/// <summary>A legend colour: linear-space components the renderer turns into a Godot colour. Alpha is always 1: translucency comes from the shader's per-layer uniform, and unlit tiles stay transparent black for the luminance discard. It stays Godot-free so the palette is unit-tested.</summary>
/// <param name="R">The red component, 0 to 1.</param>
/// <param name="G">The green component, 0 to 1.</param>
/// <param name="B">The blue component, 0 to 1.</param>
/// <param name="A">The alpha, 0 to 1.</param>
public readonly record struct OverlaySwatch(float R, float G, float B, float A)
{
    /// <summary>Gets the transparent swatch: the pixel the renderer writes when a layer value means "off".</summary>
    public static OverlaySwatch Clear => new(0f, 0f, 0f, 0f);
}

/// <summary>One legend row: the swatch, the locale key of its label, and the goods icon to show beside it, if any.</summary>
/// <param name="Swatch">The colour.</param>
/// <param name="LabelKey">The <c>ui-overlays.json</c> key, resolved with <c>UiStrings</c>.</param>
/// <param name="IconGoodId">The goods icon id (a crop or deposit name) for <c>IconLibrary</c>, or <see langword="null"/> for swatch only.</param>
public readonly record struct OverlayLegendEntry(OverlaySwatch Swatch, string LabelKey, string? IconGoodId);

/// <summary>
/// The look of every overlay layer: which colour each byte value draws, and the legend rows the panel shows. All
/// colours are calm fills at low alpha, so the island reads through them; value 0 is always transparent.
/// </summary>
public static class OverlayPalette
{
    private static readonly OverlaySwatch _reach = new(0.95f, 0.75f, 0.25f, 1f);
    private static readonly OverlaySwatch _market = new(0.35f, 0.65f, 0.95f, 1f);
    private static readonly OverlaySwatch _service = new(0.55f, 0.80f, 0.55f, 1f);
    private static readonly OverlaySwatch _fertile = new(0.45f, 0.85f, 0.35f, 1f);
    private static readonly OverlaySwatch _buildable = new(0.70f, 0.85f, 0.60f, 1f);

    private static readonly OverlaySwatch[] _deposits =
    [
        new(0.75f, 0.70f, 0.68f, 1f), // Stone
        new(0.85f, 0.55f, 0.30f, 1f), // Clay
        new(0.65f, 0.65f, 0.70f, 1f), // Iron
        new(0.25f, 0.25f, 0.28f, 1f), // Coal
        new(0.85f, 0.55f, 0.25f, 1f), // Copper
    ];

    /// <summary>Gets the blend alpha of a layer: the uniform the renderer sets per shown spec.</summary>
    /// <param name="spec">The layer.</param>
    public static float AlphaFor(OverlaySpec spec) => spec.Layer switch
    {
        OverlayLayer.Deposits => 0.55f,
        OverlayLayer.Fertility => 0.4f,
        OverlayLayer.Buildable => 0.3f,
        _ => 0.35f,
    };

    /// <summary>Gets the swatch a layer value draws in.</summary>
    /// <param name="spec">The layer.</param>
    /// <param name="value">The layer byte.</param>
    /// <returns>Transparent black for 0 (discarded by the shader); the opaque layer colour otherwise.</returns>
    public static OverlaySwatch ColorFor(OverlaySpec spec, byte value)
    {
        if (value == 0)
        {
            return OverlaySwatch.Clear;
        }

        return spec.Layer switch
        {
            OverlayLayer.StorehouseReach => _reach,
            OverlayLayer.MarketCoverage => _market,
            OverlayLayer.ServiceCoverage => _service,
            OverlayLayer.Fertility => _fertile,
            OverlayLayer.Deposits => value - 1 < _deposits.Length ? _deposits[value - 1] : _deposits[0],
            OverlayLayer.Buildable => _buildable,
            _ => OverlaySwatch.Clear,
        };
    }

    /// <summary>Gets the legend rows the panel shows under a layer.</summary>
    /// <param name="spec">The layer.</param>
    /// <returns>One row for on/off layers; one row per deposit kind for deposits.</returns>
    public static OverlayLegendEntry[] LegendFor(OverlaySpec spec) => spec.Layer switch
    {
        OverlayLayer.StorehouseReach => [new OverlayLegendEntry(_reach, "ui.overlays.legend.reach", null)],
        OverlayLayer.MarketCoverage => [new OverlayLegendEntry(_market, "ui.overlays.legend.covered", null)],
        OverlayLayer.ServiceCoverage => [new OverlayLegendEntry(_service, "ui.overlays.legend.covered", null)],
        OverlayLayer.Fertility => [new OverlayLegendEntry(_fertile, "ui.overlays.legend.fertile", ContentNames.NameOf((CropKind)spec.Parameter))],
        OverlayLayer.Buildable => [new OverlayLegendEntry(_buildable, "ui.overlays.legend.buildable", null)],
        OverlayLayer.Deposits => DepositLegend(),
        _ => [],
    };

    private static OverlayLegendEntry[] DepositLegend()
    {
        DepositKind[] kinds = Enum.GetValues<DepositKind>();
        var entries = new OverlayLegendEntry[Math.Min(kinds.Length, _deposits.Length)];
        for (int i = 0; i < entries.Length; i++)
        {
            string name = ContentNames.NameOf(kinds[i]);
            entries[i] = new OverlayLegendEntry(_deposits[i], "ui.overlays.deposit." + name, name);
        }

        return entries;
    }
}
