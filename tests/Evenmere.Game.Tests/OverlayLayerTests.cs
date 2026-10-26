using Evenmere.Content.Definitions;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Tests;

/// <summary>M2-20: the overlay picker lists every layer once, labels come from locale keys, and the screenshot flag parses.</summary>
public sealed class OverlayLayerTests
{
    [Fact]
    public void All_ListsReachMarketServicesCropsDepositsAndBuildableOnce()
    {
        OverlaySpec[] all = OverlaySpec.All();

        all.Select(static s => s.LabelKey()).ShouldBeUnique();
        all.Count(static s => s.Layer == OverlayLayer.StorehouseReach).ShouldBe(1);
        all.Count(static s => s.Layer == OverlayLayer.MarketCoverage).ShouldBe(1);
        all.Count(static s => s.Layer == OverlayLayer.Deposits).ShouldBe(1);
        all.Count(static s => s.Layer == OverlayLayer.Buildable).ShouldBe(1);
        all.Count(static s => s.Layer == OverlayLayer.ServiceCoverage).ShouldBe(Enum.GetValues<ServiceKind>().Length - 1);
        all.Count(static s => s.Layer == OverlayLayer.Fertility).ShouldBe(Enum.GetValues<CropKind>().Length);
    }

    [Fact]
    public void LabelKeys_PointAtUiOverlays()
    {
        OverlaySpec.Reach.LabelKey().ShouldBe("ui.overlays.reach");
        OverlaySpec.Market.LabelKey().ShouldBe("ui.overlays.market");
        OverlaySpec.Service(ServiceKind.Shrine).LabelKey().ShouldBe("ui.overlays.service.shrine");
        OverlaySpec.Fertile(CropKind.Grain).LabelKey().ShouldBe("ui.overlays.fertility.grain");
        OverlaySpec.Deposits.LabelKey().ShouldBe("ui.overlays.deposits");
        OverlaySpec.Buildable.LabelKey().ShouldBe("ui.overlays.buildable");
    }

    [Fact]
    public void Groups_SplitCoverageFertilityAndResources()
    {
        OverlaySpec.Reach.Group().ShouldBe(OverlayGroup.Coverage);
        OverlaySpec.Market.Group().ShouldBe(OverlayGroup.Coverage);
        OverlaySpec.Service(ServiceKind.Shrine).Group().ShouldBe(OverlayGroup.Coverage);
        OverlaySpec.Fertile(CropKind.Grain).Group().ShouldBe(OverlayGroup.Fertility);
        OverlaySpec.Deposits.Group().ShouldBe(OverlayGroup.Resources);
        OverlaySpec.Buildable.Group().ShouldBe(OverlayGroup.Resources);

        OverlayGroups.Key(OverlayGroup.Coverage).ShouldBe("ui.overlays.group.coverage");
        OverlayGroups.Key(OverlayGroup.Fertility).ShouldBe("ui.overlays.group.fertility");
        OverlayGroups.Key(OverlayGroup.Resources).ShouldBe("ui.overlays.group.resources");
    }

    [Fact]
    public void Locale_ResolvesEveryLayerGroupAndLegendKey()
    {
        var strings = new UiStrings(TestLocales.Table());
        var keys = new HashSet<string>();
        foreach (OverlaySpec spec in OverlaySpec.All())
        {
            keys.Add(spec.LabelKey());
            keys.Add(OverlayGroups.Key(spec.Group()));
            foreach (OverlayLegendEntry entry in OverlayPalette.LegendFor(spec))
            {
                keys.Add(entry.LabelKey);
            }
        }

        foreach (string key in keys.Order())
        {
            strings.Get(key).ShouldNotBe(key, $"locale key {key}");
        }
    }

    [Fact]
    public void TryParseShot_ParsesEverySpelling()
    {
        OverlaySpec.TryParseShot("reach", out OverlaySpec reach).ShouldBeTrue();
        reach.ShouldBe(OverlaySpec.Reach);

        OverlaySpec.TryParseShot("market", out OverlaySpec market).ShouldBeTrue();
        market.ShouldBe(OverlaySpec.Market);

        OverlaySpec.TryParseShot("service:shrine", out OverlaySpec shrine).ShouldBeTrue();
        shrine.ShouldBe(OverlaySpec.Service(ServiceKind.Shrine));

        OverlaySpec.TryParseShot("fertility:grain", out OverlaySpec grain).ShouldBeTrue();
        grain.ShouldBe(OverlaySpec.Fertile(CropKind.Grain));

        OverlaySpec.TryParseShot("deposits", out OverlaySpec deposits).ShouldBeTrue();
        deposits.ShouldBe(OverlaySpec.Deposits);

        OverlaySpec.TryParseShot("buildable", out OverlaySpec buildable).ShouldBeTrue();
        buildable.ShouldBe(OverlaySpec.Buildable);

        OverlaySpec.TryParseShot("service:bakery", out _).ShouldBeFalse();
        OverlaySpec.TryParseShot("nothing", out _).ShouldBeFalse();
    }
}
