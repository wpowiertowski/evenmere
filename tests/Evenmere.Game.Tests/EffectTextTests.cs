using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;

namespace Evenmere.Game.Tests;

/// <summary>M2-35: the research and culture trees share one effect formatter, with unchanged output.</summary>
public sealed class EffectTextTests
{
    [Fact]
    public void Describe_MatchesCivicGraphBuilder()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        ImmutableArray<Effect> effects =
        [
            new UnlockBuildingEffect(content.IdOf<BuildingTypeId>("shrine")),
            new UnlockTierEffect(content.TiersByOrder[0]),
            new TraditionSlotEffect(TraditionSlotType.Hearth),
            new ModifierEffect(Stat.Productivity, new IslandTarget(), 20, null),
        ];

        IReadOnlyList<GraphUnlock> civic = CivicGraphBuilder.DescribeEffects(content, strings, effects, static _ => null);
        IReadOnlyList<GraphUnlock> shared = EffectText.Describe(content, strings, effects, static _ => null, EffectText.StatName);

        shared.ShouldBe(civic);
    }

    [Fact]
    public void Describe_ResearchStatSpelling_KeepsContentNames()
    {
        // The research tree keeps its exact output: modifier stats read as the content spells them.
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        ImmutableArray<Effect> effects =
        [
            new ModifierEffect(Stat.OutputCulture, new IslandTarget(), 15, null),
        ];

        IReadOnlyList<GraphUnlock> shared = EffectText.Describe(content, strings, effects, static _ => null, ContentNames.NameOf);

        shared.Count.ShouldBe(1);
        shared[0].Text.ShouldBe("+15% output.culture");
    }
}
