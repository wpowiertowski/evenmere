using Evenmere.Tools.Balance;
using Evenmere.Tools.Balance.MultiSeed;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Runs the multi-seed runner over two starter seeds: parsing, the summary table and determinism.</summary>
public sealed class MultiSeedTests
{
    [Fact]
    public void TryParseSeeds_Range_ParsesInclusive()
    {
        MultiSeedRunner.TryParseSeeds("1..5", out IReadOnlyList<ulong> seeds).ShouldBeTrue();

        seeds.ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void TryParseSeeds_Single_ParsesOne()
    {
        MultiSeedRunner.TryParseSeeds("3", out IReadOnlyList<ulong> seeds).ShouldBeTrue();

        seeds.ShouldBe([3]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("seeds")]
    [InlineData("5..1")]
    [InlineData("1..200")]
    [InlineData("1...5")]
    public void TryParseSeeds_Invalid_ReturnsFalse(string text)
    {
        MultiSeedRunner.TryParseSeeds(text, out IReadOnlyList<ulong> seeds).ShouldBeFalse();

        seeds.ShouldBeEmpty();
    }

    [Fact]
    public void Run_TwoStarterSeeds_DeterministicJsonMatchesByteForByte()
    {
        string first = RunShort([7, 8]);
        string second = RunShort([7, 8]);

        first.ShouldBe(second);
    }

    [Fact]
    public void Run_TwoStarterSeeds_SummaryMatchesStandaloneRuns()
    {
        MultiSeedReport report = RunReport([7, 8]);

        report.Script.ShouldBe("era1");
        report.Entries.Length.ShouldBe(2);
        report.Entries[0].Seed.ShouldBe(7UL);
        report.Entries[1].Seed.ShouldBe(8UL);
        report.Reports.Count.ShouldBe(2);
        report.ToText().ShouldContain("multi-seed: script era1, 2 seeds");
    }

    private static string RunShort(IReadOnlyList<ulong> seeds) => RunReport(seeds).ToJson();

    private static MultiSeedReport RunReport(IReadOnlyList<ulong> seeds)
    {
        var script = BotScript.ParseFile(ScriptPath());
        script.ValidateContent(BotWorld.Content);
        return MultiSeedRunner.Run(
            BotWorld.Content,
            _ => BotWorld.Map,
            script,
            1500,
            seeds);
    }

    private static string ScriptPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return Path.Combine(directory.FullName, "tools", "bots", "era1.json");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
