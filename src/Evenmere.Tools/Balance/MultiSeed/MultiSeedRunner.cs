using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Tools.Reporting;

namespace Evenmere.Tools.Balance.MultiSeed;

/// <summary>One seed's line in a multi-seed run.</summary>
/// <param name="Seed">The world seed.</param>
/// <param name="Ticks">How many ticks ran.</param>
/// <param name="EraReached">Whether Era II began (Era I bot) or the script target was reached (v1 bot).</param>
/// <param name="EraMinutes">The game minutes when Era II began or the target was reached, or null.</param>
/// <param name="Villagers">Final Villager residents (Era I bot) or final target-tier residents (v1 bot).</param>
/// <param name="Stalls">How many stalls were recorded.</param>
/// <param name="Hash">The final state hash, 16 lowercase hex digits.</param>
public sealed record MultiSeedEntry(ulong Seed, long Ticks, bool EraReached, double? EraMinutes, int Villagers, int Stalls, string Hash);

/// <summary>What a <c>--seeds</c> run saw: one entry per seed plus each seed's deterministic report.</summary>
/// <param name="Script">The script's name.</param>
/// <param name="Entries">One entry per seed, in seed order.</param>
/// <param name="Reports">Each seed's deterministic report node, keyed by seed.</param>
public sealed record MultiSeedReport(string Script, ImmutableArray<MultiSeedEntry> Entries, ImmutableSortedDictionary<ulong, JsonObject> Reports)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>Serialises the whole report: the summary plus every seed's deterministic section.</summary>
    /// <returns>The JSON with two-space indent and one trailing newline, byte-identical for the same run.</returns>
    public string ToJson()
    {
        var summary = new JsonArray();
        foreach (MultiSeedEntry entry in Entries)
        {
            summary.Add(new JsonObject
            {
                ["seed"] = entry.Seed,
                ["ticks"] = entry.Ticks,
                ["eraReached"] = entry.EraReached,
                ["eraMinutes"] = entry.EraMinutes is double minutes ? JsonValue.Create(minutes) : null,
                ["villagers"] = entry.Villagers,
                ["stalls"] = entry.Stalls,
                ["hash"] = entry.Hash,
            });
        }

        var reports = new JsonObject();
        foreach ((ulong seed, JsonObject node) in Reports)
        {
            reports[seed.ToString(CultureInfo.InvariantCulture)] = node;
        }

        var root = new JsonObject
        {
            ["script"] = Script,
            ["summary"] = summary,
            ["reports"] = reports,
        };
        return root.ToJsonString(_json) + "\n";
    }

    /// <summary>Renders the human-readable summary table: one line per seed.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"multi-seed: script {Script}, {Entries.Length} seeds\n");
        text.Append("seed  era-II(min)  villagers  stalls  hash\n");
        foreach (MultiSeedEntry entry in Entries)
        {
            string era = entry.EraReached && entry.EraMinutes is double minutes
                ? minutes.ToString("F1", CultureInfo.InvariantCulture)
                : "missed";
            text.Append(CultureInfo.InvariantCulture, $"{entry.Seed,-5} {era,-12} {entry.Villagers,-10} {entry.Stalls,-7} {entry.Hash}\n");
        }

        return text.ToString();
    }
}

/// <summary>
/// Runs one bot script across several seeds in sequence and tabulates pacing: time to Era II (or the v1
/// target), stalls and hashes per seed. Each seed runs exactly like a single <c>balance</c> run with the same
/// map settings, so a seed's line matches its standalone report.
/// </summary>
public static class MultiSeedRunner
{
    /// <summary>Runs the seeds in order.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="mapFor">Builds the island for a seed (generated per seed, or the same loaded map).</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="maxTicks">How many ticks each seed runs at most.</param>
    /// <param name="seeds">The seeds in run order.</param>
    /// <returns>The summary plus each seed's deterministic report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static MultiSeedReport Run(
        ContentDb content,
        Func<ulong, MapDef> mapFor,
        BotScript script,
        long maxTicks,
        IReadOnlyList<ulong> seeds)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mapFor);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(seeds);

        ImmutableArray<MultiSeedEntry>.Builder entries = ImmutableArray.CreateBuilder<MultiSeedEntry>();
        ImmutableSortedDictionary<ulong, JsonObject>.Builder reports = ImmutableSortedDictionary.CreateBuilder<ulong, JsonObject>();
        foreach (ulong seed in seeds)
        {
            MapDef map = mapFor(seed);
            var options = new BalanceOptions(seed, maxTicks);
            if (string.Equals(script.Name, "era1", StringComparison.Ordinal))
            {
                EraOne.EraOneReport era = EraOne.EraOneBot.Run(content, map, script, options);
                JsonObject? node = JsonNode.Parse(era.ToDeterministicJson())?.AsObject() ?? [];
                reports[seed] = node;
                era.Base.Residents.TryGetValue("villagers", out int villagers);
                entries.Add(new MultiSeedEntry(
                    seed,
                    era.Base.Ticks,
                    era.Era.Reached,
                    era.Era.ReachedMinutes,
                    villagers,
                    era.Base.Stalls.Length,
                    era.Base.Hash));
            }
            else
            {
                BalanceReport report = BalanceBot.Run(content, map, script, options);
                JsonObject? node = JsonNode.Parse(report.ToDeterministicJson())?.AsObject() ?? [];
                reports[seed] = node;
                report.Residents.TryGetValue(report.Target.Tier, out int tier);
                entries.Add(new MultiSeedEntry(
                    seed,
                    report.Ticks,
                    report.Target.Reached,
                    report.Target.ReachedMinutes,
                    tier,
                    report.Stalls.Length,
                    report.Hash));
            }
        }

        return new MultiSeedReport(script.Name ?? "unnamed", [.. entries], reports.ToImmutable());
    }

    /// <summary>Loads content, the script and the per-seed maps, then runs every seed.</summary>
    /// <param name="roots">The content roots, in load order.</param>
    /// <param name="mapId">The island to settle (a folder under <c>maps/</c>), when <paramref name="size"/> is 0.</param>
    /// <param name="botPath">The bot script file.</param>
    /// <param name="seeds">The seeds in run order.</param>
    /// <param name="maxMinutes">The cap on game minutes per seed.</param>
    /// <param name="error">Human-readable diagnostics and the summary table.</param>
    /// <param name="size">When not 0, generates an island this many tiles across per seed instead of loading <paramref name="mapId"/>.</param>
    /// <returns>The report, or null when the inputs are invalid (diagnostics on <paramref name="error"/>).</returns>
    public static MultiSeedReport? RunFromCli(
        IReadOnlyList<string> roots,
        string mapId,
        string botPath,
        IReadOnlyList<ulong> seeds,
        long maxMinutes,
        TextWriter error,
        int size = 0)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(botPath);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(error);

        ContentLoadResult result = ContentLoader.Load(roots);
        TextReport.WriteDiagnostics(result.Diagnostics, error);
        if (result.Db is null)
        {
            TextReport.WriteSummary(ContentSummary.Create(result, result.Diagnostics), error);
            return null;
        }

        MapDef? fixedMap = null;
        if (size == 0)
        {
            MapLoadResult loaded = MapLoader.Load(roots, mapId, result.Db);
            TextReport.WriteDiagnostics(loaded.Diagnostics, error);
            if (loaded.Map is null)
            {
                error.WriteLine($"evenmere balance: cannot load map \"{mapId}\".");
                return null;
            }

            fixedMap = loaded.Map;
        }
        else if (size != IslandGenerator.MinSize && size != IslandGenerator.DefaultSize && size != IslandGenerator.MaxSize)
        {
            error.WriteLine($"evenmere balance: --size must be 256, 320 or 384 (got {size}).");
            return null;
        }

        BotScript script;
        try
        {
            script = BotScript.ParseFile(botPath);
            script.ValidateContent(result.Db);
        }
        catch (BotScriptException ex)
        {
            error.WriteLine($"evenmere balance: {ex.Message}");
            return null;
        }

        long maxTicks = checked(maxMinutes * BotScript.TicksPerGameMinute);
        ContentDb db = result.Db;
        MapDef MapFor(ulong seed) => fixedMap ?? IslandGenerator.Generate(unchecked((int)seed), size, db);
        return Run(db, MapFor, script, maxTicks, seeds);
    }

    /// <summary>Parses a <c>--seeds</c> value: <c>1..5</c> runs seeds 1 to 5 inclusive; a single number runs one seed.</summary>
    /// <param name="text">The option text.</param>
    /// <param name="seeds">The seeds in run order.</param>
    /// <returns>True when the text parsed; otherwise false with an empty list.</returns>
    public static bool TryParseSeeds(string text, out IReadOnlyList<ulong> seeds)
    {
        seeds = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int dots = text.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong single))
            {
                seeds = [single];
                return true;
            }

            return false;
        }

        string first = text[..dots];
        string last = text[(dots + 2)..];
        if (!ulong.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out ulong from)
            || !ulong.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out ulong to)
            || to < from
            || to - from > 99)
        {
            return false;
        }

        var parsed = new List<ulong>();
        for (ulong seed = from; seed <= to; seed++)
        {
            parsed.Add(seed);
        }

        seeds = parsed;
        return true;
    }
}
