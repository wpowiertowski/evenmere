using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;

using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;

namespace Evenmere.Game.Tests;

public sealed class ProgressTests
{
    [Fact]
    public void EtaDays_RoundsUp()
    {
        ProgressMath.EtaDays(Amount.FromUnits(10), Amount.Zero, Amount.FromUnits(3)).ShouldBe(4);
    }

    [Fact]
    public void EtaDays_NoGain_ReturnsNull()
    {
        ProgressMath.EtaDays(Amount.FromUnits(10), Amount.Zero, Amount.Zero).ShouldBeNull();
    }

    [Fact]
    public void Fraction_ClampsBetweenZeroAndOne()
    {
        ProgressMath.Fraction(Amount.Zero, Amount.FromUnits(10)).ShouldBe(0.0);
        ProgressMath.Fraction(Amount.FromUnits(5), Amount.FromUnits(10)).ShouldBe(0.5);
        ProgressMath.Fraction(Amount.FromUnits(20), Amount.FromUnits(10)).ShouldBe(1.0);
        ProgressMath.Fraction(Amount.Zero, Amount.Zero).ShouldBe(1.0);
    }

    [Fact]
    public void ConditionText_FormatsRoadTiles()
    {
        var strings = new UiStrings(TestLocales.Table());
        var names = new FakeNames();
        string text = ConditionText.Format(new RoadTilesCondition(null, 5), strings, names);
        text.ShouldContain("5");
    }

    [Fact]
    public void TreeModel_GroupsTargetQueueAndAvailable()
    {
        var strings = new UiStrings(TestLocales.Table());
        var nodes = new List<NodeInput<TechId>>
        {
            new(new TechId(1), "A", 0, "Era", [], null, Amount.FromUnits(10), Amount.FromUnits(10), Amount.Zero),
            new(new TechId(2), "B", 0, "Era", [], null, Amount.FromUnits(10), Amount.FromUnits(10), Amount.Zero),
        };
        var target = new TechId(1);
        var queue = new List<TechId> { new(2) };

        TreeSnapshot<TechId> snapshot = TreeModel.Build(
            nodes,
            target,
            queue,
            _ => false,
            _ => true,
            _ => false,
            _ => false,
            Amount.FromUnits(1),
            strings,
            _ => "cond",
            0);

        snapshot.Rows.Count.ShouldBe(2);
        snapshot.Rows[0].Id.ShouldBe(target);
    }

    private sealed class FakeNames : IConditionNames
    {
        public string Good(GoodId good) => good.Value.ToString();
        public string Building(BuildingTypeId building) => building.Value.ToString();
        public string Tier(TierId tier) => tier.Value.ToString();
        public string Tech(TechId tech) => tech.Value.ToString();
        public string Civic(CivicId civic) => civic.Value.ToString();
        public string Era(EraId era) => era.Value.ToString();
        public string Terrain(TerrainId terrain) => terrain.Value.ToString();
        public string Tag(string tag) => tag;
        public string Category(BuildingCategory category) => category.ToString();
        public string Season(Evenmere.Core.Time.Season season) => season.ToString();
        public string Discovery(DiscoveryKind kind) => kind.ToString();
        public string GreatWorkType(GreatWorkTypeId type) => type.Value.ToString();
    }
}
