using Evenmere.Content;
using Evenmere.Content.Definitions;

namespace Evenmere.Game.Rendering.Overlays;

/// <summary>Which invisible rule an overlay layer shows. Parameterized layers carry their kind in <see cref="OverlaySpec.Parameter"/>.</summary>
public enum OverlayLayer
{
    /// <summary>Tiles a new building could be supplied from (some storehouse's reach).</summary>
    StorehouseReach,

    /// <summary>Tiles a house could be covered by a market from.</summary>
    MarketCoverage,

    /// <summary>Tiles a house could be covered by a service from; the parameter is the <see cref="ServiceKind"/> value.</summary>
    ServiceCoverage,

    /// <summary>Tiles fertile for a crop; the parameter is the <see cref="CropKind"/> value.</summary>
    Fertility,

    /// <summary>Tiles with a mineral deposit, coloured per kind.</summary>
    Deposits,

    /// <summary>Tiles a building could stand on (buildable, gentle, unlocked, revealed).</summary>
    Buildable,
}

/// <summary>Which panel group a layer is listed under.</summary>
public enum OverlayGroup
{
    /// <summary>Storehouse reach and service coverage.</summary>
    Coverage,

    /// <summary>Fertility per crop.</summary>
    Fertility,

    /// <summary>Deposits and the buildable mask.</summary>
    Resources,
}

/// <summary>The locale keys of the panel group headers in <c>ui-overlays.json</c>.</summary>
public static class OverlayGroups
{
    /// <summary>Gets the header key for a group.</summary>
    /// <param name="group">The group.</param>
    public static string Key(OverlayGroup group) => group switch
    {
        OverlayGroup.Coverage => "ui.overlays.group.coverage",
        OverlayGroup.Fertility => "ui.overlays.group.fertility",
        OverlayGroup.Resources => "ui.overlays.group.resources",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "The overlay group is not known."),
    };
}

/// <summary>
/// One overlay a panel button or the placement preview can show: a layer plus its parameter. It is a plain value the
/// view model and the renderer pass around. Display text lives in <c>content/locale/en/ui-overlays.json</c> and is
/// read through <see cref="OverlaySpec.LabelKey"/>; the panel resolves it with <c>UiStrings</c> like the other panels.
/// </summary>
/// <param name="Layer">The layer.</param>
/// <param name="Parameter">The service or crop for parameterized layers; ignored otherwise.</param>
public readonly record struct OverlaySpec(OverlayLayer Layer, int Parameter = 0)
{
    /// <summary>Gets the storehouse-reach layer.</summary>
    public static OverlaySpec Reach => new(OverlayLayer.StorehouseReach);

    /// <summary>Gets the market-coverage layer.</summary>
    public static OverlaySpec Market => new(OverlayLayer.MarketCoverage);

    /// <summary>Gets a service-coverage layer.</summary>
    /// <param name="service">The service.</param>
    public static OverlaySpec Service(ServiceKind service) => new(OverlayLayer.ServiceCoverage, (int)service);

    /// <summary>Gets a fertility layer.</summary>
    /// <param name="crop">The crop.</param>
    public static OverlaySpec Fertile(CropKind crop) => new(OverlayLayer.Fertility, (int)crop);

    /// <summary>Gets the deposits layer.</summary>
    public static OverlaySpec Deposits => new(OverlayLayer.Deposits);

    /// <summary>Gets the buildable layer.</summary>
    public static OverlaySpec Buildable => new(OverlayLayer.Buildable);

    /// <summary>
    /// Lists the layers the panel offers: reach, market, every service, fertility for every crop, deposits and
    /// buildable, in that order.
    /// </summary>
    public static OverlaySpec[] All()
    {
        var specs = new List<OverlaySpec> { Reach, Market };
        foreach (ServiceKind service in Enum.GetValues<ServiceKind>())
        {
            if (service != ServiceKind.Market)
            {
                specs.Add(Service(service));
            }
        }

        foreach (CropKind crop in Enum.GetValues<CropKind>())
        {
            specs.Add(Fertile(crop));
        }

        specs.Add(Deposits);
        specs.Add(Buildable);
        return [.. specs];
    }

    /// <summary>Gets the locale key the panel button shows (resolved with <c>UiStrings</c>).</summary>
    public string LabelKey() => Layer switch
    {
        OverlayLayer.StorehouseReach => "ui.overlays.reach",
        OverlayLayer.MarketCoverage => "ui.overlays.market",
        OverlayLayer.ServiceCoverage => "ui.overlays.service." + ContentNames.NameOf((ServiceKind)Parameter),
        OverlayLayer.Fertility => "ui.overlays.fertility." + ContentNames.NameOf((CropKind)Parameter),
        OverlayLayer.Deposits => "ui.overlays.deposits",
        OverlayLayer.Buildable => "ui.overlays.buildable",
        _ => throw new ArgumentOutOfRangeException(nameof(Layer), Layer, "The overlay layer is not known."),
    };

    /// <summary>Gets the panel group the layer is listed under.</summary>
    public OverlayGroup Group() => Layer switch
    {
        OverlayLayer.Fertility => OverlayGroup.Fertility,
        OverlayLayer.Deposits => OverlayGroup.Resources,
        OverlayLayer.Buildable => OverlayGroup.Resources,
        _ => OverlayGroup.Coverage,
    };

    /// <summary>Parses the <c>--overlay-shot</c> debug value (<c>reach</c>, <c>market</c>, <c>service:shrine</c>, <c>fertility:grain</c>, <c>deposits</c>, <c>buildable</c>).</summary>
    /// <param name="value">The value after the <c>=</c>.</param>
    /// <param name="spec">The layer, when it parses.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> names a layer.</returns>
    public static bool TryParseShot(string value, out OverlaySpec spec)
    {
        spec = Reach;
        if (string.Equals(value, "reach", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "market", StringComparison.OrdinalIgnoreCase))
        {
            spec = Market;
            return true;
        }

        if (string.Equals(value, "deposits", StringComparison.OrdinalIgnoreCase))
        {
            spec = Deposits;
            return true;
        }

        if (string.Equals(value, "buildable", StringComparison.OrdinalIgnoreCase))
        {
            spec = Buildable;
            return true;
        }

        string[] parts = value.Split(':');
        if (parts.Length == 2)
        {
            if (string.Equals(parts[0], "service", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<ServiceKind>(parts[1], ignoreCase: true, out ServiceKind service))
            {
                spec = Service(service);
                return true;
            }

            if (string.Equals(parts[0], "fertility", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<CropKind>(parts[1], ignoreCase: true, out CropKind crop))
            {
                spec = Fertile(crop);
                return true;
            }
        }

        return false;
    }
}
