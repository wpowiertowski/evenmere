using System.Text;
using static Evenmere.Sim.Tests.Scenarios.ScenarioText;

namespace Evenmere.Sim.Tests.Scenarios;

public sealed class GoldenFileTests
{
    private const string NewHash = "fedcba9876543210";
    private const string OldHash = "0123456789abcdef";

    private static string Edit(string json) => Encoding.UTF8.GetString(GoldenFile.WithHash(Encoding.UTF8.GetBytes(json), NewHash));

    private static ScenarioResult Run(string path, bool update) => ScenarioRunner.RunFile(path, new ScenarioRunOptions(update));

    [Fact]
    public void WithHash_ExistingHash_ReplacesOnlyThatValue()
    {
        const string Before = """
            {
              "description": "keep this exactly — éàü, trailing   spaces",
              "content": "c",   "seed": 1,
              "ticks": 5,
              "expect": {
                "events": [ { "type": "A", "atLeast": 1 } ],
                "hash": "0123456789abcdef",
                "stock": { "timber": ">= 1" }
              }
            }
            """;

        string after = Edit(Before);

        after.ShouldBe(Before.Replace(OldHash, NewHash, StringComparison.Ordinal));
    }

    [Fact]
    public void WithHash_NullHash_ReplacesTheNull()
    {
        const string Before = """{ "content": "c", "seed": 1, "ticks": 5, "expect": { "hash": null } }""";

        Edit(Before).ShouldBe("""{ "content": "c", "seed": 1, "ticks": 5, "expect": { "hash": "fedcba9876543210" } }""");
    }

    [Fact]
    public void WithHash_ExpectWithoutHash_AddsItFirstInTheFilesOwnStyle()
    {
        const string Before = """
            {
              "content": "c",
              "seed": 1,
              "ticks": 5,
              "expect": {
                "events": [ { "type": "A", "atLeast": 1 } ]
              }
            }
            """;
        const string Expected = """
            {
              "content": "c",
              "seed": 1,
              "ticks": 5,
              "expect": {
                "hash": "fedcba9876543210",
                "events": [ { "type": "A", "atLeast": 1 } ]
              }
            }
            """;

        Edit(Before).ShouldBe(Expected);
    }

    [Fact]
    public void WithHash_NullExpect_ReplacesItWithABlockHoldingTheHash()
    {
        const string Before = "{\n  \"content\": \"c\",\n  \"expect\": null,\n  \"seed\": 1,\n  \"ticks\": 5\n}\n";

        Edit(Before).ShouldBe("{\n  \"content\": \"c\",\n  \"expect\": {\n    \"hash\": \"fedcba9876543210\"\n  },\n  \"seed\": 1,\n  \"ticks\": 5\n}\n");
    }

    [Fact]
    public void WithHash_ExpectOnOneLineWithoutHash_AddsItOnThatLine()
    {
        const string Before = """{ "content": "c", "seed": 1, "ticks": 5, "expect": { "stock": { "wood": ">= 1" } } }""";

        Edit(Before).ShouldBe("""{ "content": "c", "seed": 1, "ticks": 5, "expect": { "hash": "fedcba9876543210", "stock": { "wood": ">= 1" } } }""");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ }")]
    [InlineData("{\n    }")]
    public void WithHash_EmptyExpect_ExpandsItWithTwoSpaceIndentation(string emptyExpect)
    {
        string before = $"{{\n  \"content\": \"c\",\n  \"seed\": 1,\n  \"ticks\": 5,\n  \"expect\": {emptyExpect}\n}}\n";

        Edit(before).ShouldBe("{\n  \"content\": \"c\",\n  \"seed\": 1,\n  \"ticks\": 5,\n  \"expect\": {\n    \"hash\": \"fedcba9876543210\"\n  }\n}\n");
    }

    [Fact]
    public void WithHash_NoExpect_AppendsOneAfterTheLastProperty()
    {
        const string Before = "{\n  \"content\": \"c\",\n  \"seed\": 1,\n  \"ticks\": 5,\n  \"commands\": [\n    { \"tick\": 1, \"type\": \"A\" }\n  ]\n}\n";

        Edit(Before).ShouldBe(
            "{\n  \"content\": \"c\",\n  \"seed\": 1,\n  \"ticks\": 5,\n  \"commands\": [\n    { \"tick\": 1, \"type\": \"A\" }\n  ],\n"
            + "  \"expect\": {\n    \"hash\": \"fedcba9876543210\"\n  }\n}\n");
    }

    [Fact]
    public void WithHash_NoExpectAndFourSpaceFile_FollowsTheFilesIndentation()
    {
        const string Before = "{\n    \"content\": \"c\",\n    \"seed\": 1,\n    \"ticks\": 5\n}\n";

        Edit(Before).ShouldBe("{\n    \"content\": \"c\",\n    \"seed\": 1,\n    \"ticks\": 5,\n    \"expect\": {\n        \"hash\": \"fedcba9876543210\"\n    }\n}\n");
    }

    [Fact]
    public void WithHash_CrLfFile_AddsCrLfLines()
    {
        const string Before = "{\r\n  \"content\": \"c\",\r\n  \"seed\": 1,\r\n  \"ticks\": 5\r\n}\r\n";

        Edit(Before).ShouldBe("{\r\n  \"content\": \"c\",\r\n  \"seed\": 1,\r\n  \"ticks\": 5,\r\n  \"expect\": {\r\n    \"hash\": \"fedcba9876543210\"\r\n  }\r\n}\r\n");
    }

    [Fact]
    public void WithHash_FileWithByteOrderMark_KeepsTheMarkAndEditsInPlace()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] json = [.. bom, .. Encoding.UTF8.GetBytes("""{ "content": "é", "seed": 1, "ticks": 5, "expect": { "hash": "0123456789abcdef" } }""")];

        byte[] after = GoldenFile.WithHash(json, NewHash);

        after.Take(3).ShouldBe(bom);
        Encoding.UTF8.GetString(after, 3, after.Length - 3).ShouldBe("""{ "content": "é", "seed": 1, "ticks": 5, "expect": { "hash": "fedcba9876543210" } }""");
    }

    [Fact]
    public void WithHash_NestedHashProperties_OnlyTouchesExpectHash()
    {
        const string Before = """
            { "description": "hash", "content": "c", "seed": 1, "ticks": 5,
              "commands": [ { "tick": 1, "type": "A", "args": { "hash": "0123456789abcdef" } } ],
              "expect": { "stock": { "hash": ">= 1" } } }
            """;

        string after = Edit(Before);

        after.ShouldContain("\"args\": { \"hash\": \"0123456789abcdef\" }");
        after.ShouldContain("\"expect\": { \"hash\": \"fedcba9876543210\", \"stock\": { \"hash\": \">= 1\" } }");
    }

    [Fact]
    public void WithHash_Result_StillParsesAsAScenario()
    {
        string edited = Edit("{\n  \"content\": \"c\",\n  \"seed\": 1,\n  \"ticks\": 5\n}\n");

        ScenarioParser.Parse(edited, "x.json").Expect.Hash.ShouldBe(0xfedcba9876543210UL);
    }

    [Fact]
    public void Run_HashMismatchWithUpdateOn_RewritesOnlyTheHashAndPasses()
    {
        using var temp = new ScenarioTempFolder();
        string before = Make(ticks: 40, seed: 3, expect: $$"""{ "hash": "{{OldHash}}" }""");
        string path = temp.Write("golden.json", before);

        ScenarioResult updated = Run(path, update: true);

        updated.Passed.ShouldBeTrue(updated.Report);
        updated.GoldenUpdated.ShouldBeTrue();
        updated.Report.ShouldContain("EVENMERE_UPDATE_GOLDEN=1: rewrote expect.hash");
        updated.Report.ShouldContain(OldHash + " -> " + HashText.Format(updated.ActualHash));
        File.ReadAllText(path).ShouldBe(before.Replace(OldHash, HashText.Format(updated.ActualHash), StringComparison.Ordinal));
    }

    [Fact]
    public void Run_AfterAGoldenUpdate_TheNextStrictRunPasses()
    {
        using var temp = new ScenarioTempFolder();
        string path = temp.Write("golden.json", Make(ticks: 40, seed: 3, expect: $$"""{ "hash": "{{OldHash}}" }"""));
        Run(path, update: false).Passed.ShouldBeFalse();

        ScenarioResult updated = Run(path, update: true);
        ScenarioResult again = Run(path, update: false);

        updated.Passed.ShouldBeTrue();
        again.Passed.ShouldBeTrue(again.Report);
        again.GoldenUpdated.ShouldBeFalse();
        again.ExpectedHash.ShouldBe(updated.ActualHash);
    }

    [Fact]
    public void Run_HashMismatchWithUpdateOff_FailsAndLeavesTheFileAlone()
    {
        using var temp = new ScenarioTempFolder();
        string before = Make(ticks: 40, seed: 3, expect: $$"""{ "hash": "{{OldHash}}" }""");
        string path = temp.Write("golden.json", before);

        ScenarioResult result = Run(path, update: false);

        result.Passed.ShouldBeFalse();
        result.GoldenUpdated.ShouldBeFalse();
        File.ReadAllText(path).ShouldBe(before);
    }

    [Fact]
    public void Run_MissingHashWithUpdateOn_AddsTheHash()
    {
        using var temp = new ScenarioTempFolder();
        string path = temp.Write("golden.json", "{\n  \"content\": \"tests/fixtures/content/minimal\",\n  \"seed\": 3,\n  \"ticks\": 40\n}\n");

        ScenarioResult result = Run(path, update: true);

        result.Passed.ShouldBeTrue(result.Report);
        result.GoldenUpdated.ShouldBeTrue();
        File.ReadAllText(path).ShouldBe(
            "{\n  \"content\": \"tests/fixtures/content/minimal\",\n  \"seed\": 3,\n  \"ticks\": 40,\n  \"expect\": {\n    \"hash\": \""
            + HashText.Format(result.ActualHash)
            + "\"\n  }\n}\n");
    }

    [Fact]
    public void Run_MissingHashWithUpdateOff_PassesAndLeavesTheFileAlone()
    {
        using var temp = new ScenarioTempFolder();
        string before = Make(ticks: 40, seed: 3);
        string path = temp.Write("golden.json", before);

        ScenarioResult result = Run(path, update: false);

        result.Passed.ShouldBeTrue();
        File.ReadAllText(path).ShouldBe(before);
    }

    [Fact]
    public void Run_MatchingHashWithUpdateOn_DoesNotTouchTheFile()
    {
        using var temp = new ScenarioTempFolder();
        ulong hash = ScenarioText.Run(Make(ticks: 40, seed: 3)).ActualHash;
        string before = Make(ticks: 40, seed: 3, expect: $$"""{ "hash": "{{HashText.Format(hash)}}" }""");
        string path = temp.Write("golden.json", before);
        DateTime stamp = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        ScenarioResult result = Run(path, update: true);

        result.Passed.ShouldBeTrue();
        result.GoldenUpdated.ShouldBeFalse();
        File.GetLastWriteTimeUtc(path).ShouldBe(stamp);
    }

    [Fact]
    public void Run_HashMismatchAndAnotherFailureWithUpdateOn_FailsWithoutRewriting()
    {
        using var temp = new ScenarioTempFolder();
        string before = Make(
            ticks: 40,
            seed: 3,
            expect: $$"""{ "hash": "{{OldHash}}", "stock": { "timber": ">= 99" } }""");
        string path = temp.Write("golden.json", before);

        ScenarioResult result = Run(path, update: true);

        result.Passed.ShouldBeFalse();
        result.GoldenUpdated.ShouldBeFalse();
        result.Failures.Count.ShouldBe(2);
        result.Failures[1].ShouldContain("not rewritten, because another expectation failed");
        File.ReadAllText(path).ShouldBe(before);
    }

    [Fact]
    public void Run_UpdateOnTheSampleWithAnEditedSeed_RewritesTheHashAndKeepsTheRestOfTheFile()
    {
        using var temp = new ScenarioTempFolder();
        string original = File.ReadAllText(Path.Combine(ScenarioPaths.ScenarioDirectory, "empty_world_10k.json"));
        string edited = original.Replace("\"seed\": 42", "\"seed\": 43", StringComparison.Ordinal);
        string path = temp.Write("edited.json", edited);

        ScenarioResult failing = Run(path, update: false);
        ScenarioResult updated = Run(path, update: true);

        failing.Passed.ShouldBeFalse();
        updated.Passed.ShouldBeTrue(updated.Report);
        string rewritten = File.ReadAllText(path);
        rewritten.ShouldBe(edited.Replace(HashText.Format(updated.ExpectedHash), HashText.Format(updated.ActualHash), StringComparison.Ordinal));
        rewritten.ShouldContain("\"seed\": 43");
        Run(path, update: false).Passed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void FromVariable_Value_TurnsUpdatingOnOnlyForOne(string? value, bool expected)
    {
        ScenarioRunOptions.FromVariable(value).UpdateGolden.ShouldBe(expected);
    }
}
