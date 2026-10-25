using System.Collections.Immutable;
using Evenmere.Content.Lint;
using E = Evenmere.Content.Tests.Lint.Expected;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// Every committed fixture in <c>tests/fixtures/content/lint</c> is a full copy of the minimal pack with one thing changed.
/// This table says exactly which diagnostics each one produces, so a fixture that breaks more than its name says fails here.
/// A fixture named <c>*-ok</c> is a valid variation that must lint clean.
/// </summary>
public sealed class FixtureCaseTests
{
    private const string Sawpit = "buildings/sawpit.json";
    private const string Villagers = "tiers/villagers.json";
    private const string Township = "eras/township.json";

    private static readonly SortedDictionary<string, E[]> _expected = new(StringComparer.Ordinal)
    {
        ["EVL001-bad-quantity"] = [E.Error(DiagnosticCodes.Evl001, Sawpit, "/cost/timber", "more than 3 decimal places")],
        ["EVL001-duplicate-locale-key"] = [E.Error(DiagnosticCodes.Evl001, "locale/en/tail.json", "/good.fish.name", "duplicate locale key")],
        ["EVL001-extra-property"] = [E.Error(DiagnosticCodes.Evl001, Sawpit, "/colour", "unknown property 'colour'")],

        ["EVL002-unknown-recipe"] = [E.Error(DiagnosticCodes.Evl002, Sawpit, "/recipe", "saw_planks")],
        ["EVL002-unknown-unlock-target"] = [E.Error(DiagnosticCodes.Evl002, "techs/net_weaving.json", "/effects/0/unlockBuilding", "windmill")],

        ["EVL003-civic-cycle-downstream"] =
        [
            E.Error(LintCodes.Evl003, "civics/moot_a.json", "/requires/0", "moot_a -> moot_b -> moot_a"),
            E.Error(LintCodes.Evl003, "civics/moot_c.json", "/requires/0", "can never be reached from a root"),
        ],
        ["EVL003-tech-cycle"] = [E.Error(LintCodes.Evl003, "techs/ring_a.json", "/requires/0", "ring_a -> ring_b -> ring_c -> ring_a")],

        ["EVL004-no-consumer"] = [E.Error(LintCodes.Evl004, "goods/pearl.json", string.Empty, "no consumer")],
        ["EVL004-no-producer"] = [E.Error(LintCodes.Evl004, "goods/salt.json", string.Empty, "no producer")],
        ["EVL004-no-producer-or-consumer"] =
        [
            E.Error(LintCodes.Evl004, "goods/ghost.json", string.Empty, "no consumer"),
            E.Error(LintCodes.Evl004, "goods/ghost.json", string.Empty, "no producer"),
        ],

        ["EVL005-no-source"] = [E.Error(LintCodes.Evl005, "buildings/windmill.json", string.Empty, "has no unlock source")],
        ["EVL005-recipe-via-building-ok"] = [],
        ["EVL005-start-and-tech"] = [E.Error(LintCodes.Evl005, Sawpit, "/start", "2 unlock sources")],
        ["EVL005-two-sources"] = [E.Error(LintCodes.Evl005, "buildings/windmill.json", string.Empty, "2 unlock sources")],

        ["EVL006-gateway-tier-ok"] = [],
        ["EVL006-good-too-late"] = [E.Error(LintCodes.Evl006, Villagers, "/basicNeeds/goods/bread", "complete only in era 2")],
        ["EVL006-input-too-late"] = [E.Error(LintCodes.Evl006, Villagers, "/basicNeeds/goods/bread", "complete only in era 2")],
        ["EVL006-same-era-ok"] = [],
        ["EVL006-service-too-late"] = [E.Error(LintCodes.Evl006, Villagers, "/basicNeeds/services/0", "'tavern' service")],
        ["EVL006-tier-era-before-source"] =
        [
            E.Error(LintCodes.Evl006, "tiers/townsfolk.json", "/era", "declares era 1, but it is unlocked in era 2 by tech 'brick_laying'"),
        ],

        ["EVL007-missing-desc"] = [E.Warning(LintCodes.Evl007, "goods/fish.json", string.Empty, "good.fish.desc")],
        ["EVL007-missing-name"] = [E.Error(LintCodes.Evl007, Sawpit, string.Empty, "building.sawpit.name")],

        ["EVL008-era-without-requires"] =
        [
            E.Error(LintCodes.Evl008, "civics/village_moot.json", "/gateway", "no era requires it"),
            E.Error(LintCodes.Evl008, Township, string.Empty, "has no \"requires\""),
            E.Error(LintCodes.Evl008, "techs/town_charter.json", "/gateway", "no era requires it"),
        ],
        ["EVL008-gateway-not-flagged"] = [E.Error(LintCodes.Evl008, Township, "/requires/tech", "not a gateway")],
        ["EVL008-requires-later-era"] = [E.Error(LintCodes.Evl008, "techs/fishing_lore.json", "/requires/0", "later era 2")],
        ["EVL008-three-eras-ok"] = [],
        ["EVL008-unreferenced-gateway"] = [E.Error(LintCodes.Evl008, "techs/second_gate.json", "/gateway", "no era requires it")],

        ["EVL011-less-or-equal"] = [E.Error(LintCodes.Evl011, Villagers, "/basicNeeds/goods", "tier 'villagers' needs 3")],
        ["EVL011-ok"] = [],
    };

    public static TheoryData<string> Cases => [.. _expected.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Run_Fixture_ReportsExactlyTheExpectedDiagnostics(string name)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintFixtures.Lint(name);

        diagnostics.ShouldBeExactly(_expected[name]);
    }

    [Fact]
    public void Fixtures_EveryFolderHasAnExpectationAndEveryExpectationAFolder()
    {
        LintFixtures.Names.ShouldBe([.. _expected.Keys]);
    }

    [Fact]
    public void Fixtures_NamesStartWithTheRuleTheyExercise()
    {
        foreach (string name in _expected.Keys)
        {
            string code = name[..6];
            bool ok = name.EndsWith("-ok", StringComparison.Ordinal);
            bool reportsThatCode = _expected[name].Any(e => e.Code == code);
            (ok ? _expected[name].Length == 0 : reportsThatCode).ShouldBeTrue($"Fixture {name} should {(ok ? "lint clean" : "report " + code)}.");
        }
    }

    [Theory]
    [InlineData("EVL001")]
    [InlineData("EVL002")]
    [InlineData("EVL003")]
    [InlineData("EVL004")]
    [InlineData("EVL005")]
    [InlineData("EVL006")]
    [InlineData("EVL007")]
    [InlineData("EVL008")]
    public void Fixtures_EveryRuleHasAtLeastTwoFailingFixtures(string code)
    {
        int failing = _expected.Count(pair => pair.Value.Any(e => e.Code == code));

        failing.ShouldBeGreaterThanOrEqualTo(2, $"{code} needs at least two failing fixtures.");
    }

    [Theory]
    [InlineData("EVL005-two-sources", "tech 'net_weaving' (techs/net_weaving.json#/effects/0)", "civic 'village_moot' (civics/village_moot.json#/effects/2)")]
    [InlineData("EVL005-start-and-tech", "\"start\": true (buildings/sawpit.json#/start)", "tech 'net_weaving' (techs/net_weaving.json#/effects/0)")]
    public void Run_TwoUnlockSources_ListsEveryOne(string name, string first, string second)
    {
        ContentDiagnostic diagnostic = LintFixtures.Lint(name).Single();

        diagnostic.Message.ShouldContain(first);
        diagnostic.Message.ShouldContain(second);
    }

    [Fact]
    public void Fixtures_OnlyTheShapeAndReferenceFixturesFailLoading()
    {
        // A fixture the loader rejects has no database, so none of the rules EVL003 to EVL008 can run on it; every other
        // fixture must load, so that its rule really ran.
        string[] rejectedByLoader =
        [
            "EVL001-bad-quantity", "EVL001-extra-property", "EVL002-unknown-recipe", "EVL002-unknown-unlock-target",
        ];

        foreach (string name in LintFixtures.Names)
        {
            ContentLoadResult result = ContentLoader.Load(LintFixtures.PathOf(name));
            bool rejected = rejectedByLoader.Contains(name);
            (result.Db is null).ShouldBe(rejected, $"Fixture {name} should {(rejected ? "fail" : "pass")} loading.");
        }
    }
}
