using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Events;
using Evenmere.Game.Tests;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Game.Tests;

/// <summary>The happening dialog rules: the pending card shows its options with effect previews and a wait marker, and choosing sends the answer while deciding later sends nothing.</summary>
public sealed class HappeningDialogModelTests
{
    private static UiStrings Strings => new(TestLocales.Table());

    [Fact]
    public void NoPending_ShowsQuietEmpty()
    {
        var model = new HappeningDialogModel(new FakeHappenings(null), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.HasPending.ShouldBeFalse();
        model.Options.ShouldBeEmpty();
        model.Body.ShouldBe(Strings.Get("ui.events.happening.empty"));
        model.DaysLeftText.ShouldBe(string.Empty);
        model.ChooseCommand(0).ShouldBeNull();
    }

    [Fact]
    public void Pending_ShowsTitleBodyAndDaysLeft()
    {
        var model = new HappeningDialogModel(new FakeHappenings(Card(daysLeft: 3)), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.HasPending.ShouldBeTrue();
        model.Title.ShouldBe("Travelling scholar");
        model.Body.ShouldBe("A scholar asks for bread and offers stories.");
        model.DaysLeftText.ShouldBe(Strings.Format("ui.events.happening.days_left.other", ("count", "3")));
    }

    [Fact]
    public void Pending_OneDayLeft_UsesSingular()
    {
        var model = new HappeningDialogModel(new FakeHappenings(Card(daysLeft: 1)), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.DaysLeftText.ShouldBe(Strings.Get("ui.events.happening.days_left.one"));
    }

    [Fact]
    public void Options_PreviewEachEffectKind()
    {
        var card = new HappeningCardFacts(
            HappeningId.From(0),
            "Bountiful catch",
            "Nets full.",
            [
                new HappeningOptionFacts(0, [new GrantGoodsFact("Fish", Amount.FromUnits(10))], IsDefault: false),
                new HappeningOptionFacts(1, [new ModifierFact("productivity", 20, "fisheries")], IsDefault: true),
                new HappeningOptionFacts(2, [new FestivalRequestFact("Harvest Fair", 25)], IsDefault: false),
                new HappeningOptionFacts(3, [new GrantPointsFact("knowledge", Amount.FromUnits(6))], IsDefault: false),
            ],
            4);
        var model = new HappeningDialogModel(new FakeHappenings(card), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.Options.Count.ShouldBe(4);
        model.Options[0].PreviewText.ShouldBe(Strings.Format(
            "ui.events.preview.grant", ("amount", "10"), ("good", "Fish")));
        model.Options[1].PreviewText.ShouldBe(Strings.Format(
            "ui.events.preview.modifier", ("percent", "20"), ("stat", "productivity"), ("target", "fisheries")));
        model.Options[2].PreviewText.ShouldBe(Strings.Format(
            "ui.events.preview.request", ("discount", "25"), ("festival", "Harvest Fair")));
        model.Options[3].PreviewText.ShouldBe(Strings.Format(
            "ui.events.preview.points", ("amount", "6"), ("kind", "knowledge")));
    }

    [Fact]
    public void DefaultOption_MarkedOnlyWhenAChoiceExists()
    {
        var model = new HappeningDialogModel(new FakeHappenings(Card(daysLeft: 3)), Strings);

        model.Advance(0.0).ShouldBeTrue();

        HappeningOptionRow first = model.Options[0];
        HappeningOptionRow second = model.Options[1];
        first.IsDefault.ShouldBeFalse();
        first.DefaultMark.ShouldBeNull();
        second.IsDefault.ShouldBeTrue();
        second.DefaultMark.ShouldBe(Strings.Format("ui.events.happening.default_mark", ("option", second.PreviewText)));
    }

    [Fact]
    public void SingleOption_HasNoWaitMarker()
    {
        var card = new HappeningCardFacts(
            HappeningId.From(0),
            "Stone find",
            "Loose stone.",
            [new HappeningOptionFacts(0, [new GrantGoodsFact("Stone", Amount.FromUnits(5))], IsDefault: true)],
            4);
        var model = new HappeningDialogModel(new FakeHappenings(card), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.Options.ShouldHaveSingleItem().DefaultMark.ShouldBeNull();
    }

    [Fact]
    public void EmptyOption_FallsBackToQuietBenefit()
    {
        var card = new HappeningCardFacts(
            HappeningId.From(0),
            "Fair weather",
            "A calm day.",
            [new HappeningOptionFacts(0, [], IsDefault: true)],
            4);
        var model = new HappeningDialogModel(new FakeHappenings(card), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.Options.ShouldHaveSingleItem().PreviewText.ShouldBe(Strings.Get("ui.events.effect.unknown"));
    }

    [Fact]
    public void ChooseCommand_AnswersThePendingHappening()
    {
        HappeningCardFacts card = Card(daysLeft: 3);
        var model = new HappeningDialogModel(new FakeHappenings(card), Strings);

        model.Advance(0.0).ShouldBeTrue();

        model.ChooseCommand(1).ShouldBe(new ChooseHappeningOption(card.Id, 1));
    }

    private static HappeningCardFacts Card(int daysLeft) => new(
        HappeningId.From(0),
        "Travelling scholar",
        "A scholar asks for bread and offers stories.",
        [
            new HappeningOptionFacts(0, [new GrantGoodsFact("Bread", Amount.FromUnits(2))], IsDefault: false),
            new HappeningOptionFacts(1, [new GrantPointsFact("knowledge", Amount.FromUnits(6))], IsDefault: true),
        ],
        daysLeft);

    private sealed class FakeHappenings(HappeningCardFacts? card) : IHappeningData
    {
        public HappeningCardFacts? Card => card;
    }
}
