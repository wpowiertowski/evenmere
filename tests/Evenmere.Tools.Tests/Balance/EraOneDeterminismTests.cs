using Evenmere.Sim;
using Evenmere.Tools.Balance;
using Evenmere.Tools.Balance.EraOne;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Runs the Era I bot script twice on a short slice: the deterministic report section matches byte for byte.</summary>
public sealed class EraOneDeterminismTests
{
    private const string ScriptName = "era1";

    [Fact]
    public void EraOneScript_ValidatesAgainstContent()
    {
        var script = BotScript.ParseFile(ScriptPath());

        script.Name.ShouldBe(ScriptName);
        script.ValidateContent(BotWorld.Content);
    }

    [Fact]
    public void Run_TwiceOnShortEraOneRun_DeterministicSectionMatchesByteForByte()
    {
        string first = RunShort(11);
        string second = RunShort(11);

        first.ShouldBe(second);
    }

    [Fact]
    public void RunEraOne_TwiceOnShortRun_DeterministicSectionMatchesByteForByte()
    {
        string first = RunEraOneShort(11);
        string second = RunEraOneShort(11);

        first.ShouldBe(second);
    }

    [Fact]
    public void RunEraOne_ReservesOrchardSites()
    {
        string first = RunEraOneShort(11);

        first.ShouldContain("reserved orchard site");
    }

    [Fact]
    public void FindExpedition_WithoutLodge_ReturnsNull()
    {
        ISimulation simulation = Simulation.CreateNew(
            BotWorld.Content, new NewGameOptions { Seed = 11, Map = BotWorld.Map });

        ExpeditionPlanner.FindExpedition(BotWorld.Content, simulation).ShouldBeNull();
    }

    private static string RunShort(ulong seed)
    {
        var script = BotScript.ParseFile(ScriptPath());
        script.ValidateContent(BotWorld.Content);
        BalanceReport report = BalanceBot.Run(
            BotWorld.Content, BotWorld.Map, script, new BalanceOptions(seed, 1500));
        return report.ToDeterministicJson();
    }

    private static string RunEraOneShort(ulong seed)
    {
        var script = BotScript.ParseFile(ScriptPath());
        script.ValidateContent(BotWorld.Content);
        EraOneReport report = EraOneBot.Run(
            BotWorld.Content, BotWorld.Map, script, new BalanceOptions(seed, 1500));
        return report.ToDeterministicJson();
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
