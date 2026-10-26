using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Chains;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Tests;

/// <summary>The chain view-model annotates the content graph with live numbers: statistics samples become the
/// per-day rates, producers become counts and average productivity, and the explain tree's missing-input chain
/// becomes the bottleneck highlight.</summary>
public sealed class ChainViewModelTests
{
    private static ContentDb Db => TestContent.Db;

    private static UiStrings Strings => new(TestLocales.Table());

    [Fact]
    public void Rows_UseStatisticsSamplesForRates()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        FakeChainData data = new FakeChainData()
            .WithRate(bread, producedMilli: 12_500, consumedMilli: 8_000);
        var model = new ChainViewModel(Db, Strings, data);
        model.Select(bread);

        model.Advance(0.0).ShouldBeTrue();

        ChainRow row = model.Rows.Single(r => r.Good == bread);
        row.RatesLine.ShouldBe("+12.5 −8.0 per day");
        // The acceptance debug label reads the same samples the rows do.
        model.DebugLine.ShouldContain("+12.5");
        model.DebugLine.ShouldContain("−8.0");
        model.DebugLine.ShouldContain("Bread");
    }

    [Fact]
    public void StatsLine_CountsBuildingsAndAveragesProductivity()
    {
        GoodId flour = Db.Goods.IdOf("flour");
        BuildingTypeId windmill = Db.Buildings.IdOf("windmill");
        FakeChainData data = new FakeChainData()
            .WithProducer(flour, new EntityId(5), windmill, productivityPerMille: 400, StallReason.MissingInput)
            .WithProducer(flour, new EntityId(7), windmill, productivityPerMille: 600, StallReason.None);
        var model = new ChainViewModel(Db, Strings, data);
        model.Select(Db.Goods.IdOf("bread"));

        model.Advance(0.0).ShouldBeTrue();

        model.Rows.Single(r => r.Good == flour).StatsLine.ShouldBe("2 × Windmill · 50%");
    }

    [Fact]
    public void StatsLine_NoProducers_NamesIt()
    {
        var model = new ChainViewModel(Db, Strings, new FakeChainData());
        model.Select(Db.Goods.IdOf("bread"));

        model.Advance(0.0).ShouldBeTrue();

        foreach (ChainRow row in model.Rows)
        {
            row.StatsLine.ShouldBe("No producers");
        }

        model.Buildings.ShouldBeEmpty();
        model.WorstBuilding.ShouldBe(EntityId.None);
        model.HasBottleneck.ShouldBeFalse();
    }

    [Fact]
    public void Bottleneck_WalksMissingInputsToTheRootCause()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        GoodId flour = Db.Goods.IdOf("flour");
        GoodId grain = Db.Goods.IdOf("grain");
        FakeChainData data = new FakeChainData().WithShortage(bread, [flour, grain]);
        var model = new ChainViewModel(Db, Strings, data);
        model.Select(bread);

        model.Advance(0.0).ShouldBeTrue();

        model.HasBottleneck.ShouldBeTrue();
        model.Bottleneck.ShouldBe(grain);
        model.ShortagePath.ShouldBe([flour, grain]);
        model.Rows.Single(r => r.Good == bread).IsShortage.ShouldBeFalse();
        model.Rows.Single(r => r.Good == bread).IsBottleneck.ShouldBeFalse();
        ChainRow flourRow = model.Rows.Single(r => r.Good == flour);
        flourRow.IsShortage.ShouldBeTrue();
        flourRow.IsBottleneck.ShouldBeFalse();
        ChainRow grainRow = model.Rows.Single(r => r.Good == grain);
        grainRow.IsShortage.ShouldBeTrue();
        grainRow.IsBottleneck.ShouldBeTrue();
    }

    [Fact]
    public void WorstBuilding_LowestProductivityThenLowestId()
    {
        GoodId flour = Db.Goods.IdOf("flour");
        BuildingTypeId windmill = Db.Buildings.IdOf("windmill");
        FakeChainData data = new FakeChainData()
            .WithProducer(flour, new EntityId(7), windmill, productivityPerMille: 300, StallReason.MissingInput)
            .WithProducer(flour, new EntityId(5), windmill, productivityPerMille: 300, StallReason.MissingInput)
            .WithProducer(flour, new EntityId(9), windmill, productivityPerMille: 200, StallReason.NoWorkforce);
        var model = new ChainViewModel(Db, Strings, data);
        model.Select(flour);

        model.Advance(0.0).ShouldBeTrue();

        model.WorstBuilding.ShouldBe(new EntityId(9));
        model.Buildings.Select(static b => b.Id).ShouldBe([new EntityId(5), new EntityId(7), new EntityId(9)]);
        model.Buildings.Single(b => b.IsWorst).Id.ShouldBe(new EntityId(9));
        model.Buildings.Single(b => b.Id == new EntityId(9)).StallText.ShouldBe("no workforce");
    }

    [Fact]
    public void WorstBuilding_TieBreaksByLowestId()
    {
        GoodId flour = Db.Goods.IdOf("flour");
        BuildingTypeId windmill = Db.Buildings.IdOf("windmill");
        FakeChainData data = new FakeChainData()
            .WithProducer(flour, new EntityId(7), windmill, productivityPerMille: 300, StallReason.MissingInput)
            .WithProducer(flour, new EntityId(5), windmill, productivityPerMille: 300, StallReason.MissingInput);
        var model = new ChainViewModel(Db, Strings, data);
        model.Select(flour);

        model.Advance(0.0).ShouldBeTrue();

        model.WorstBuilding.ShouldBe(new EntityId(5));
    }

    [Fact]
    public void NoSelection_RowsStayEmpty()
    {
        var model = new ChainViewModel(Db, Strings, new FakeChainData());

        model.Advance(0.0).ShouldBeTrue();

        model.HasSelection.ShouldBeFalse();
        model.Rows.ShouldBeEmpty();
        model.Buildings.ShouldBeEmpty();
        model.Title.ShouldBe("Chain");
    }

    [Fact]
    public void Title_NamesTheSelectedGood()
    {
        var model = new ChainViewModel(Db, Strings, new FakeChainData());
        model.Select(Db.Goods.IdOf("bread"));

        model.Advance(0.0).ShouldBeTrue();

        model.Title.ShouldBe("Bread chain");
    }

    /// <summary>An in-memory <see cref="IChainData"/>: rates and producers are set per good, and the shortage path
    /// is scripted as the missing-input chain behind a root.</summary>
    private sealed class FakeChainData : IChainData
    {
        private readonly Dictionary<GoodId, List<ChainProducer>> _producers = new();
        private readonly Dictionary<GoodId, long> _produced = new();
        private readonly Dictionary<GoodId, long> _consumed = new();
        private readonly Dictionary<GoodId, List<GoodId>> _shortages = new();

        public FakeChainData WithRate(GoodId good, long producedMilli, long consumedMilli)
        {
            _produced[good] = producedMilli;
            _consumed[good] = consumedMilli;
            return this;
        }

        public FakeChainData WithProducer(GoodId good, EntityId id, BuildingTypeId type, int productivityPerMille, StallReason stall)
        {
            if (!_producers.TryGetValue(good, out List<ChainProducer>? list))
            {
                list = new List<ChainProducer>();
                _producers[good] = list;
            }

            list.Add(new ChainProducer(id, type, productivityPerMille, stall));
            list.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            return this;
        }

        public FakeChainData WithShortage(GoodId root, IReadOnlyList<GoodId> path)
        {
            _shortages[root] = new List<GoodId>(path);
            return this;
        }

        public IReadOnlyList<ChainProducer> ProducersOf(GoodId good) =>
            _producers.TryGetValue(good, out List<ChainProducer>? list) ? list : [];

        public long ProducedPerDayMilli(GoodId good) => _produced.GetValueOrDefault(good);

        public long ConsumedPerDayMilli(GoodId good) => _consumed.GetValueOrDefault(good);

        public Explanation Explain(GoodId good)
        {
            if (!_shortages.TryGetValue(good, out List<GoodId>? path))
            {
                return new Explanation(ExplanationLine.Leaf("explain.good.supply", ExplainArg.Of(good)));
            }

            ExplanationLine nested = new(
                "explain.good.supply",
                [ExplainArg.Of(path[^1])],
                [SubjectLink.ForGood(path[^1])],
                []);
            for (int i = path.Count - 1; i >= 0; i--)
            {
                GoodId subject = path[i];
                ExplanationLine cause = new(
                    "explain.good.cause.missing_input",
                    [ExplainArg.Of(subject)],
                    [SubjectLink.ForGood(subject)],
                    [nested]);
                nested = i == 0
                    ? new ExplanationLine(
                        "explain.good.supply",
                        [ExplainArg.Of(good)],
                        [SubjectLink.ForGood(good)],
                        [cause])
                    : new ExplanationLine(
                        "explain.good.supply",
                        [ExplainArg.Of(subject)],
                        [SubjectLink.ForGood(subject)],
                        [cause]);
            }

            return new Explanation(nested);
        }
    }
}
