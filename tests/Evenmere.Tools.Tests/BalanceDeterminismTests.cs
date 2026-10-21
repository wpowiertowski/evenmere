using Evenmere.Tools.Balance;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Runs the bot twice on a short script: the deterministic report section must match byte for byte.</summary>
public sealed class BalanceDeterminismTests
{
    private const string ShortScript = """
        {
          "name": "short",
          "goals": [
            { "research": "cultivation", "queue": ["animal_husbandry"] },
            { "civic": "hearth_and_shrine" },
            { "build": "market", "count": 1, "near": "founders_hall" },
            { "build": "settler_house", "count": 2, "near": "market" },
            { "lockHouses": 1 }
          ]
        }
        """;

    [Fact]
    public void Run_TwiceOnShortScript_DeterministicSectionMatchesByteForByte()
    {
        string first = RunShort(7);
        string second = RunShort(7);

        first.ShouldBe(second);
    }

    [Fact]
    public void Run_DifferentSeed_Differs()
    {
        string first = RunShort(7);
        string other = RunShort(8);

        first.ShouldNotBe(other);
    }

    private static string RunShort(ulong seed)
    {
        var script = BotScript.Parse(ShortScript);
        script.ValidateContent(BotWorld.Content);
        BalanceReport report = BalanceBot.Run(
            BotWorld.Content, BotWorld.Map, script, new BalanceOptions(seed, 1500));
        return report.ToDeterministicJson();
    }
}
