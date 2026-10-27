using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Tools.Balance;

namespace Evenmere.Tools.Balance.EraOne;


/// <summary>Whether the Era I run reached the Township era.</summary>
/// <param name="Reached">Whether Era II began before the tick budget ran out.</param>
/// <param name="ReachedTick">The tick Era II began, or <see langword="null"/>.</param>
/// <param name="ResidentsAtEra">Total residents when Era II began, or <see langword="null"/>.</param>
/// <param name="VillagersAtEra">Villager residents when Era II began, or <see langword="null"/>.</param>
/// <param name="ResidentsAtFirst">Total residents when the first Villager appeared, or <see langword="null"/>.</param>
/// <param name="ResidentsAtHundred">Total residents when the 100th Villager appeared, or <see langword="null"/>.</param>
public sealed record EraOneEraResult(bool Reached, long? ReachedTick, int? ResidentsAtEra, int? VillagersAtEra, int? ResidentsAtFirst, int? ResidentsAtHundred)
{
    /// <summary>Gets the game minutes when Era II began, or <see langword="null"/>.</summary>
    public double? ReachedMinutes => ReachedTick is long tick ? tick / (double)BotScript.TicksPerGameMinute : null;
}

/// <summary>
/// What the Era I bot saw: the v1 report as its base plus the Township-era milestone. The <c>deterministic</c>
/// section is byte-identical for one seed and script; the <c>timings</c> section is not.
/// </summary>
/// <param name="Base">The underlying bot report: milestones, stalls, commands, residents, stock, notes and hash.</param>
/// <param name="Era">Whether and when the Township era began, with residents then.</param>
/// <param name="TechGatewayDone">Whether the tech gateway was complete at the end.</param>
/// <param name="CivicGatewayDone">Whether the civic gateway was complete at the end.</param>
public sealed record EraOneReport(
    BalanceReport Base,
    EraOneEraResult Era,
    bool TechGatewayDone,
    bool CivicGatewayDone)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Serialises the deterministic section: everything except the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline, byte-identical for the same run.</returns>
    public string ToDeterministicJson() => DeterministicNode().ToJsonString(_json) + "\n";
    /// <summary>Serialises the whole report: the deterministic section plus the wall-clock timings.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline.</returns>
    public string ToJson()
    {
        var deterministic = JsonNode.Parse(DeterministicNode().ToJsonString());
        var root = new JsonObject
        {
            ["deterministic"] = deterministic,
            ["timings"] = new JsonObject
            {
                ["ticksRun"] = Base.Timings.TicksRun,
                ["meanMsPerTick"] = Base.Timings.MeanMsPerTick,
                ["peakMsPerTick"] = Base.Timings.PeakMsPerTick,
            },
        };
        return root.ToJsonString(_json) + "\n";
    }

    /// <summary>Renders the human-readable text report.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var text = new StringBuilder(Base.ToText());
        text.Append(CultureInfo.InvariantCulture, $"era II: {(Era.Reached ? $"began at tick {Era.ReachedTick} ({Era.ReachedMinutes:F1} min) with {Era.ResidentsAtEra} residents ({Era.VillagersAtEra} villagers)" : "not reached")}\n");
        text.Append(CultureInfo.InvariantCulture, $"gateways: tech {(TechGatewayDone ? "done" : "open")}, civic {(CivicGatewayDone ? "done" : "open")}\n");
        return text.ToString();
    }

    private JsonObject DeterministicNode()
    {
        var baseNode = JsonNode.Parse(Base.ToDeterministicJson());
        JsonObject root = baseNode?.AsObject() ?? [];
        root["era"] = new JsonObject
        {
            ["reached"] = Era.Reached,
            ["reachedTick"] = Era.ReachedTick is long tick ? JsonValue.Create(tick) : null,
            ["residentsAtEra"] = Era.ResidentsAtEra is int residents ? JsonValue.Create(residents) : null,
            ["villagersAtEra"] = Era.VillagersAtEra is int villagers ? JsonValue.Create(villagers) : null,
            ["residentsAtFirst"] = Era.ResidentsAtFirst is int first ? JsonValue.Create(first) : null,
            ["residentsAtHundred"] = Era.ResidentsAtHundred is int hundred ? JsonValue.Create(hundred) : null,
            ["techGatewayDone"] = TechGatewayDone,
            ["civicGatewayDone"] = CivicGatewayDone,
        };
        return root;
    }
}
