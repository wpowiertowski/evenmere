using Evenmere.Game.Ui.Exploration;

namespace Evenmere.Game.Tests;

/// <summary>The region-name fallback chain: the map's key, then the generated key, then the numbered fallback.</summary>
public sealed class RegionNamesTests
{
    [Fact]
    public void Resolve_MapKeyWins()
    {
        var mapKeys = new Dictionary<ushort, string> { [2] = "region.starter.landing" };
        string Lookup(string key) => key == "region.starter.landing.name" ? "Landing" : key;

        RegionNames.Resolve(Lookup, mapKeys, 2).ShouldBe("Landing");
    }

    [Fact]
    public void Resolve_MissingMapTextFallsThroughToGeneratedKey()
    {
        var mapKeys = new Dictionary<ushort, string> { [2] = "region.starter.landing" };
        string Lookup(string key) => key switch
        {
            "region.generated.2" => "Greybank",
            "ui.exploration.region.fallback" => "Region {region}",
            _ => key,
        };

        // The map key has no text yet, so the generated name wins.
        RegionNames.Resolve(Lookup, mapKeys, 2).ShouldBe("Greybank");
    }

    [Fact]
    public void Resolve_GeneratedDottedNameKeyWins()
    {
        string Lookup(string key) => key switch
        {
            "region.generated.7.name" => "Ember Reef",
            "ui.exploration.region.fallback" => "Region {region}",
            _ => key,
        };

        RegionNames.Resolve(Lookup, null, 7).ShouldBe("Ember Reef");
    }

    [Fact]
    public void Resolve_NoKeysFallsBackToNumberedRegion()
    {
        string Lookup(string key) => key == "ui.exploration.region.fallback" ? "Region {region}" : key;

        RegionNames.Resolve(Lookup, null, 5).ShouldBe("Region 5");
    }
}
