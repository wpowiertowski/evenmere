using Evenmere.Content.Definitions;
using Evenmere.Game.Rendering.Overlays;

namespace Evenmere.Game.Tests;

/// <summary>M2-20: off is always transparent, on is always a calm translucent fill, and deposits colour per kind.</summary>
public sealed class OverlayPaletteTests
{
    [Fact]
    public void Zero_IsTransparentOnEveryLayer()
    {
        foreach (OverlaySpec spec in OverlaySpec.All())
        {
            OverlayPalette.ColorFor(spec, 0).ShouldBe(OverlaySwatch.Clear);
        }
    }

    [Fact]
    public void AlphaFor_MatchesTheDesignedTranslucency()
    {
        OverlayPalette.AlphaFor(OverlaySpec.Reach).ShouldBe(0.35f);
        OverlayPalette.AlphaFor(OverlaySpec.Market).ShouldBe(0.35f);
        OverlayPalette.AlphaFor(OverlaySpec.Fertile(CropKind.Grain)).ShouldBe(0.4f);
        OverlayPalette.AlphaFor(OverlaySpec.Deposits).ShouldBe(0.55f);
        OverlayPalette.AlphaFor(OverlaySpec.Buildable).ShouldBe(0.3f);
    }

    [Fact]
    public void Lit_IsOpaqueWithTheLayerColour()
    {
        foreach (OverlaySpec spec in OverlaySpec.All())
        {
            if (spec.Layer == OverlayLayer.Deposits)
            {
                continue;
            }

            OverlayPalette.ColorFor(spec, 1).A.ShouldBe(1f);
        }
    }

    [Fact]
    public void Deposits_ColourEachKindDifferently()
    {
        var swatches = new HashSet<OverlaySwatch>();
        foreach (DepositKind kind in Enum.GetValues<DepositKind>())
        {
            OverlaySwatch swatch = OverlayPalette.ColorFor(OverlaySpec.Deposits, (byte)((int)kind + 1));
            swatch.ShouldNotBe(OverlaySwatch.Clear);
            swatches.Add(swatch);
        }

        swatches.Count.ShouldBe(Enum.GetValues<DepositKind>().Length);
    }

    [Fact]
    public void Legend_CoversEveryLayerWithKeysAndCropDepositIcons()
    {
        foreach (OverlaySpec spec in OverlaySpec.All())
        {
            OverlayLegendEntry[] legend = OverlayPalette.LegendFor(spec);
            legend.Length.ShouldBeGreaterThan(0, spec.LabelKey());
            foreach (OverlayLegendEntry entry in legend)
            {
                entry.LabelKey.ShouldStartWith("ui.overlays.");
            }
        }

        OverlayPalette.LegendFor(OverlaySpec.Deposits).Length.ShouldBe(Enum.GetValues<DepositKind>().Length);

        OverlayLegendEntry grain = OverlayPalette.LegendFor(OverlaySpec.Fertile(CropKind.Grain))[0];
        grain.LabelKey.ShouldBe("ui.overlays.legend.fertile");
        grain.IconGoodId.ShouldBe("grain");

        OverlayLegendEntry stone = OverlayPalette.LegendFor(OverlaySpec.Deposits)[0];
        stone.LabelKey.ShouldBe("ui.overlays.deposit.stone");
        stone.IconGoodId.ShouldBe("stone");

        OverlayPalette.LegendFor(OverlaySpec.Reach)[0].IconGoodId.ShouldBeNull();
    }
}
