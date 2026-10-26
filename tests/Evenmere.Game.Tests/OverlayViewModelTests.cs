using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Sim.Queries;

namespace Evenmere.Game.Tests;

/// <summary>M2-20: the view model fetches each layer from the matching query and suggests the layer a placement wants.</summary>
public sealed class OverlayViewModelTests
{
    [Fact]
    public void Fetch_CallsTheMatchingQuery()
    {
        var stub = new StubOverlayQueries();
        var model = new OverlayViewModel(TestContent.Db, stub);

        model.Fetch(OverlaySpec.Reach);
        stub.Calls.ShouldBe(["reach"]);
        model.Fetch(OverlaySpec.Service(ServiceKind.Shrine));
        stub.Calls.ShouldBe(["reach", "service:Shrine"]);
        model.Fetch(OverlaySpec.Fertile(CropKind.Grain));
        stub.Calls.ShouldBe(["reach", "service:Shrine", "fertility:Grain"]);
        model.Fetch(OverlaySpec.Deposits);
        model.Fetch(OverlaySpec.Buildable);
        stub.Calls.ShouldBe(["reach", "service:Shrine", "fertility:Grain", "deposits", "buildable"]);
    }

    [Fact]
    public void SuggestFor_PicksTheLayerThePlacementWants()
    {
        var model = new OverlayViewModel(TestContent.Db, new StubOverlayQueries());
        ContentDb content = TestContent.Db;

        model.SuggestFor(content.Buildings.IdOf("grain_farm")).ShouldBe(OverlaySpec.Fertile(CropKind.Grain));
        model.SuggestFor(content.Buildings.IdOf("market")).ShouldBe(OverlaySpec.Market);
        model.SuggestFor(content.Buildings.IdOf("shrine")).ShouldBe(OverlaySpec.Service(ServiceKind.Shrine));
        model.SuggestFor(content.Buildings.IdOf("storehouse")).ShouldBe(OverlaySpec.Reach);
        model.SuggestFor(content.Buildings.IdOf("settler_house")).ShouldBeNull();
    }

    [Fact]
    public void PlacementLayer_PrefersTheSuggestionThenFallsBack()
    {
        // Farm, then sawpit: the sawpit has no suggestion, so the pre-tool layer comes back.
        OverlayViewModel.PlacementLayer(OverlaySpec.Fertile(CropKind.Grain), null).ShouldBe(OverlaySpec.Fertile(CropKind.Grain));
        OverlayViewModel.PlacementLayer(null, OverlaySpec.Reach).ShouldBe(OverlaySpec.Reach);

        // Farm, then cancel with something open before the tool: the same fallback restores it.
        OverlayViewModel.PlacementLayer(null, OverlaySpec.Market).ShouldBe(OverlaySpec.Market);

        // No layer before the tool and no suggestion: hidden.
        OverlayViewModel.PlacementLayer(null, null).ShouldBeNull();
    }

    [Fact]
    public void Available_ListsOnlyUnlockedLayersPlusBuildable()
    {
        var model = new OverlayViewModel(TestContent.Db, new StubOverlayQueries());
        ContentDb content = TestContent.Db;
        BuildingTypeId GrainFarm() => content.Buildings.IdOf("grain_farm");
        BuildingTypeId Market() => content.Buildings.IdOf("market");

        model.Available(static _ => false).ShouldBe([OverlaySpec.Buildable]);

        OverlaySpec[] farm = model.Available(id => id == GrainFarm());
        farm.ShouldContain(OverlaySpec.Fertile(CropKind.Grain));
        farm.ShouldContain(OverlaySpec.Buildable);
        farm.ShouldNotContain(OverlaySpec.Market);

        OverlaySpec[] market = model.Available(id => id == Market());
        market.ShouldContain(OverlaySpec.Market);
        market.ShouldNotContain(OverlaySpec.Fertile(CropKind.Grain));
    }

    private sealed class StubOverlayQueries : IOverlayQueries
    {
        public readonly List<string> Calls = [];
        public int Width => 4;
        public int Height => 4;
        public byte[] StorehouseReach() => Log("reach");
        public byte[] MarketCoverage() => Log("market");
        public byte[] ServiceCoverage(ServiceKind service) => Log("service:" + service);
        public byte[] Fertility(CropKind crop) => Log("fertility:" + crop);
        public byte[] Deposits() => Log("deposits");
        public byte[] BuildableWithAccess() => Log("buildable");

        private byte[] Log(string call)
        {
            Calls.Add(call);
            return new byte[Width * Height];
        }
    }
}
