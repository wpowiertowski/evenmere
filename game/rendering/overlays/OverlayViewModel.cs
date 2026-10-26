using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;

namespace Evenmere.Game.Rendering.Overlays;

/// <summary>
/// The overlay numbers as plain data: which layer a building wants during placement, and the layer bytes for a shown
/// spec. It has no Godot types so it is unit-tested; the renderer draws what it fetches and the panel lists what it
/// labels.
/// </summary>
public sealed class OverlayViewModel
{
    private readonly ContentDb _content;
    private readonly IOverlayQueries _overlays;

    /// <summary>Initializes a new instance of the <see cref="OverlayViewModel"/> class.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="overlays">The overlay queries.</param>
    public OverlayViewModel(ContentDb content, IOverlayQueries overlays)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(overlays);
        _content = content;
        _overlays = overlays;
    }

    /// <summary>
    /// Lists the layers the panel offers: only layers whose building or crop is unlocked, so locked content never
    /// spoils a future era, in panel order (reach, market, services, fertility, deposits, buildable). Buildable
    /// always stays: it is the generic placement aid and the fallback <see cref="SuggestFor"/> returns.
    /// </summary>
    /// <param name="isUnlocked">Whether a building type is unlocked.</param>
    /// <returns>The offered layers, grouped by <see cref="OverlaySpec.Group"/>.</returns>
    public OverlaySpec[] Available(Func<BuildingTypeId, bool> isUnlocked)
    {
        ArgumentNullException.ThrowIfNull(isUnlocked);
        bool reach = false;
        bool market = false;
        bool deposits = false;
        bool[] services = new bool[Enum.GetValues<ServiceKind>().Length];
        bool[] crops = new bool[Enum.GetValues<CropKind>().Length];
        foreach (BuildingDef def in _content.Buildings.Items)
        {
            if (!isUnlocked(def.Id))
            {
                continue;
            }

            if (def.Storage is not null)
            {
                reach = true;
            }

            if (def.Service is { } service)
            {
                if (service.Kind == ServiceKind.Market)
                {
                    market = true;
                }
                else
                {
                    services[(int)service.Kind] = true;
                }
            }

            if (def.Placement.Fertility is { } fertility)
            {
                crops[(int)fertility.Crop] = true;
            }

            if (def.Placement.Deposit is not null || def.Placement.AreaYield?.Kind == AreaYieldKind.FootprintDeposit)
            {
                deposits = true;
            }
        }

        var specs = new List<OverlaySpec>();
        if (reach)
        {
            specs.Add(OverlaySpec.Reach);
        }

        if (market)
        {
            specs.Add(OverlaySpec.Market);
        }

        for (int i = 0; i < services.Length; i++)
        {
            if (services[i])
            {
                specs.Add(OverlaySpec.Service((ServiceKind)i));
            }
        }

        for (int i = 0; i < crops.Length; i++)
        {
            if (crops[i])
            {
                specs.Add(OverlaySpec.Fertile((CropKind)i));
            }
        }

        if (deposits)
        {
            specs.Add(OverlaySpec.Deposits);
        }

        specs.Add(OverlaySpec.Buildable);
        return [.. specs];
    }

    /// <summary>Fetches the layer bytes for a shown spec.</summary>
    /// <param name="spec">The layer.</param>
    /// <returns>The bytes in row-major order.</returns>
    public byte[] Fetch(OverlaySpec spec) => spec.Layer switch
    {
        OverlayLayer.StorehouseReach => _overlays.StorehouseReach(),
        OverlayLayer.MarketCoverage => _overlays.MarketCoverage(),
        OverlayLayer.ServiceCoverage => _overlays.ServiceCoverage((ServiceKind)spec.Parameter),
        OverlayLayer.Fertility => _overlays.Fertility((CropKind)spec.Parameter),
        OverlayLayer.Deposits => _overlays.Deposits(),
        OverlayLayer.Buildable => _overlays.BuildableWithAccess(),
        _ => throw new ArgumentOutOfRangeException(nameof(spec), spec, "The overlay layer is not known."),
    };

    /// <summary>
    /// Picks the layer placement shows for a building: fertility when it needs a crop, market or service coverage
    /// when it provides one, reach when it stores, and <see langword="null"/> when it wants nothing specific.
    /// A null answer restores the pre-tool layer (see <see cref="PlacementLayer"/>); it never shows buildable on
    /// its own. It reads the building's specs, never its ID, so content stays data.
    /// </summary>
    /// <param name="type">The building type being placed.</param>
    /// <returns>The layer to show while the ghost is out, or <see langword="null"/> for no specific layer.</returns>
    public OverlaySpec? SuggestFor(BuildingTypeId type)
    {
        if (type.Value >= _content.Buildings.Count)
        {
            return null;
        }

        BuildingDef def = _content.Get(type);
        if (def.Placement.Fertility is { } fertility)
        {
            return OverlaySpec.Fertile(fertility.Crop);
        }

        if (def.Service is { } service)
        {
            return service.Kind == ServiceKind.Market
                ? OverlaySpec.Market
                : OverlaySpec.Service(service.Kind);
        }

        if (def.Storage is not null)
        {
            return OverlaySpec.Reach;
        }

        return null;
    }

    /// <summary>
    /// Decides what the sheet shows when the build tool (re)targets: the suggested layer when the building wants
    /// one, otherwise whatever the player had open before the tool started (usually nothing, which hides it).
    /// </summary>
    /// <param name="suggested">The suggestion for the newly targeted building, if any.</param>
    /// <param name="beforeTool">The layer open before the tool started, if any.</param>
    /// <returns>The layer to show, or <see langword="null"/> to hide the sheet.</returns>
    public static OverlaySpec? PlacementLayer(OverlaySpec? suggested, OverlaySpec? beforeTool) => suggested ?? beforeTool;
}
