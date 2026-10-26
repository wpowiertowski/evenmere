using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable stand-in for <see cref="IOverlayQueries"/>. Each layer is set by the test; an unset layer reads as
/// all zeros at the fake map's size. Width and height default to the 32 × 32 fake-map size and follow it when set.
/// </summary>
internal sealed class FakeOverlayQueries : IOverlayQueries
{
    private readonly Dictionary<string, byte[]> _layers = [];

    /// <summary>Gets or sets the layer width in tiles.</summary>
    public int Width { get; set; } = 32;

    /// <summary>Gets or sets the layer height in tiles.</summary>
    public int Height { get; set; } = 32;

    /// <summary>Sets a layer.</summary>
    /// <param name="key">The layer key (see the <c>Layer*</c> helpers).</param>
    /// <param name="layer">The bytes in row-major order.</param>
    /// <returns>This fake.</returns>
    public FakeOverlayQueries SetLayer(string key, byte[] layer)
    {
        _layers[key] = layer;
        return this;
    }

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.StorehouseReach"/>.</summary>
    public static string LayerReach => "reach";

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.MarketCoverage"/>.</summary>
    public static string LayerMarket => "market";

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.ServiceCoverage"/>.</summary>
    /// <param name="service">The service.</param>
    public static string LayerService(ServiceKind service) => "service:" + service;

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.Fertility"/>.</summary>
    /// <param name="crop">The crop.</param>
    public static string LayerFertility(CropKind crop) => "fertility:" + crop;

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.Deposits"/>.</summary>
    public static string LayerDeposits => "deposits";

    /// <summary>Gets the layer key for <see cref="IOverlayQueries.BuildableWithAccess"/>.</summary>
    public static string LayerBuildable => "buildable";

    /// <inheritdoc />
    public byte[] StorehouseReach() => Layer(LayerReach);

    /// <inheritdoc />
    public byte[] MarketCoverage() => Layer(LayerMarket);

    /// <inheritdoc />
    public byte[] ServiceCoverage(ServiceKind service) => Layer(LayerService(service));

    /// <inheritdoc />
    public byte[] Fertility(CropKind crop) => Layer(LayerFertility(crop));

    /// <inheritdoc />
    public byte[] Deposits() => Layer(LayerDeposits);

    /// <inheritdoc />
    public byte[] BuildableWithAccess() => Layer(LayerBuildable);

    private byte[] Layer(string key) =>
        _layers.TryGetValue(key, out byte[]? layer) ? (byte[])layer.Clone() : new byte[Width * Height];
}
