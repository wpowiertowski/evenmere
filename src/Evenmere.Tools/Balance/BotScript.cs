using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content;

namespace Evenmere.Tools.Balance;

/// <summary>Thrown when a bot script cannot be parsed or names content that does not exist. The message names the goal index.</summary>
public sealed class BotScriptException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the script.</param>
    public BotScriptException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// One goal of a bot script. Goals run in file order; each blocks until it is satisfied or its timeout passes.
/// <see cref="TimeoutTicks"/> is the number of ticks the goal may take before it is marked failed and the bot moves on.
/// </summary>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public abstract record BotGoal(int TimeoutTicks)
{
    /// <summary>Gets the default timeout in game minutes when a goal names none.</summary>
    public const int DefaultTimeoutMinutes = 30;
}

/// <summary>Builds houses, producers and services: ensures at least <see cref="Count"/> buildings of <see cref="Building"/> exist, placed near <see cref="Near"/>.</summary>
/// <param name="Building">The building type to build, by content name.</param>
/// <param name="Count">How many must exist (sites count); at least 1.</param>
/// <param name="Near">The anchor building type to spiral out from, by content name.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record BuildGoal(string Building, int Count, string Near, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Researches a technology: sets it as the target (and the queue) and waits until it is done.</summary>
/// <param name="Tech">The technology to research, by content name.</param>
/// <param name="Queue">The technologies to research after the target, in order.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record ResearchGoal(string Tech, ImmutableArray<string> Queue, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Adopts a civic: sets it as the target (and the queue) and waits until it is adopted.</summary>
/// <param name="Civic">The civic to adopt, by content name.</param>
/// <param name="Queue">The civics to adopt after the target, in order.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record CivicGoal(string Civic, ImmutableArray<string> Queue, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Waits until a good's island-wide fulfilment reaches <see cref="TargetPerMille"/> per-mille.</summary>
/// <param name="Good">The good, by content name.</param>
/// <param name="TargetPerMille">The fulfilment to reach, from 1 to 1000.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record EnsureSupplyGoal(string Good, int TargetPerMille, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Builds houses until <see cref="Residents"/> residents live in <see cref="Tier"/> houses.</summary>
/// <param name="Tier">The tier, by content name.</param>
/// <param name="Residents">How many residents to reach; at least 1.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record HousesUntilGoal(string Tier, int Residents, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Locks the oldest <see cref="Count"/> houses against advancing, so they stay in their tier and keep working.</summary>
/// <param name="Count">How many houses to lock; at least 1.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record LockHousesGoal(int Count, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Waits until the simulation reaches <see cref="Tick"/>, for timing a later goal (like a wave of houses) to land inside a supply gap.</summary>
/// <param name="Tick">The tick to wait for; at least 1.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record WaitUntilTickGoal(long Tick, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>Unlocks the <see cref="Count"/> newest locked houses, so they advance again. The oldest locked houses (the workforce reserve) stay locked.</summary>
/// <param name="Count">How many houses to unlock; at least 1.</param>
/// <param name="TimeoutTicks">The ticks the goal may take; always positive.</param>
public sealed record UnlockHousesGoal(int Count, int TimeoutTicks) : BotGoal(TimeoutTicks);

/// <summary>
/// A parsed bot script (<c>tools/bots/&lt;name&gt;.json</c>): an ordered list of goals the bot works through.
/// See <c>tools/bots/README.md</c> for the format. Parsing is strict: an unknown key is an error naming the goal index.
/// </summary>
/// <param name="Name">The script's display name, or <see langword="null"/> when it names none.</param>
/// <param name="Goals">The goals in file order; at least one.</param>
public sealed record BotScript(string? Name, ImmutableArray<BotGoal> Goals)
{
    /// <summary>Gets how many ticks make one game minute (240: 4 ticks a second).</summary>
    public const int TicksPerGameMinute = 240;

    /// <summary>Reads and parses a script file.</summary>
    /// <param name="path">The script file.</param>
    /// <returns>The parsed script.</returns>
    /// <exception cref="BotScriptException">The file cannot be read or parsed.</exception>
    public static BotScript ParseFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BotScriptException($"cannot read bot script \"{path}\": {ex.Message}");
        }

        return Parse(text);
    }

    /// <summary>Parses a script.</summary>
    /// <param name="text">The JSON text.</param>
    /// <returns>The parsed script.</returns>
    /// <exception cref="BotScriptException">The text is not a valid script.</exception>
    public static BotScript Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new BotScriptException($"invalid bot script: not JSON ({ex.Message})");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BotScriptException("invalid bot script: the root must be an object.");
            }

            string? name = null;
            ImmutableArray<BotGoal> goals = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "name":
                        name = RequireString(property.Value, "script: \"name\" must be a string.");
                        break;
                    case "goals":
                        goals = ParseGoals(property.Value);
                        break;
                    default:
                        throw new BotScriptException($"invalid bot script: unknown key \"{property.Name}\".");
                }
            }

            if (goals.IsDefaultOrEmpty)
            {
                throw new BotScriptException("invalid bot script: \"goals\" must be a non-empty array.");
            }

            return new BotScript(name, goals);
        }
    }

    /// <summary>Checks that every content name the script mentions exists.</summary>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="BotScriptException">A goal names something the content does not have.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public void ValidateContent(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        for (int i = 0; i < Goals.Length; i++)
        {
            switch (Goals[i])
            {
                case BuildGoal build:
                    RequireBuilding(content, build.Building, i);
                    RequireBuilding(content, build.Near, i);
                    break;
                case ResearchGoal research:
                    RequireTech(content, research.Tech, i);
                    foreach (string queued in research.Queue)
                    {
                        RequireTech(content, queued, i);
                    }

                    break;
                case CivicGoal civic:
                    RequireCivic(content, civic.Civic, i);
                    foreach (string queued in civic.Queue)
                    {
                        RequireCivic(content, queued, i);
                    }

                    break;
                case EnsureSupplyGoal supply:
                    RequireGood(content, supply.Good, i);
                    break;
                case HousesUntilGoal houses:
                    RequireTier(content, houses.Tier, i);
                    break;
                case LockHousesGoal:
                    break;
                case UnlockHousesGoal:
                    break;
                case WaitUntilTickGoal:
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {i} has an unknown goal kind.");
            }
        }
    }

    private static ImmutableArray<BotGoal> ParseGoals(JsonElement goals)
    {
        if (goals.ValueKind != JsonValueKind.Array || goals.GetArrayLength() == 0)
        {
            throw new BotScriptException("invalid bot script: \"goals\" must be a non-empty array.");
        }

        ImmutableArray<BotGoal>.Builder parsed = ImmutableArray.CreateBuilder<BotGoal>();
        int index = 0;
        foreach (JsonElement element in goals.EnumerateArray())
        {
            parsed.Add(ParseGoal(element, index));
            index++;
        }

        return parsed.ToImmutable();
    }

    private static BotGoal ParseGoal(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new BotScriptException($"invalid bot script: goal {index} must be an object.");
        }

        string? kind = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (IsKindKey(property.Name))
            {
                if (kind is not null)
                {
                    throw new BotScriptException($"invalid bot script: goal {index} names two goals (\"{kind}\" and \"{property.Name}\").");
                }

                kind = property.Name;
            }
        }

        if (kind is null)
        {
            throw new BotScriptException($"invalid bot script: goal {index} names no goal (one of \"build\", \"research\", \"civic\", \"ensureSupply\", \"housesUntil\", \"lockHouses\", \"waitUntilTick\").");
        }

        int timeout = ParseTimeout(element, index);
        return kind switch
        {
            "build" => ParseBuild(element, index, timeout),
            "research" => ParseResearch(element, index, timeout),
            "civic" => ParseCivic(element, index, timeout),
            "ensureSupply" => ParseSupply(element, index, timeout),
            "housesUntil" => ParseHouses(element, index, timeout),
            "lockHouses" => ParseLock(element, index, timeout),
            "unlockHouses" => ParseUnlock(element, index, timeout),
            "waitUntilTick" => ParseWait(element, index, timeout),
            _ => throw new BotScriptException($"invalid bot script: goal {index} has an unknown goal kind."),
        };
    }

    private static bool IsKindKey(string key) =>
        key is "build" or "research" or "civic" or "ensureSupply" or "housesUntil" or "lockHouses" or "unlockHouses" or "waitUntilTick";

    private static BuildGoal ParseBuild(JsonElement element, int index, int timeout)
    {
        string building = string.Empty;
        int count = 1;
        string near = "founders_hall";
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "build":
                    building = RequireString(property.Value, $"invalid bot script: goal {index}: \"build\" must be a building name.");
                    break;
                case "timeoutMinutes":
                    break;
                case "count":
                    count = RequirePositiveInt(property.Value, "count", index);
                    break;
                case "near":
                    near = RequireString(property.Value, $"invalid bot script: goal {index}: \"near\" must be a building name.");
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (building.Length == 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"build\" must be a building name.");
        }

        return new BuildGoal(building, count, near, timeout);
    }

    private static ResearchGoal ParseResearch(JsonElement element, int index, int timeout)
    {
        string tech = string.Empty;
        ImmutableArray<string> queue = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "research":
                    tech = RequireString(property.Value, $"invalid bot script: goal {index}: \"research\" must be a technology name.");
                    break;
                case "timeoutMinutes":
                    break;
                case "queue":
                    queue = RequireStringArray(property.Value, $"invalid bot script: goal {index}: \"queue\" must be an array of technology names.");
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (tech.Length == 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"research\" must be a technology name.");
        }

        return new ResearchGoal(tech, queue, timeout);
    }

    private static CivicGoal ParseCivic(JsonElement element, int index, int timeout)
    {
        string civic = string.Empty;
        ImmutableArray<string> queue = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "civic":
                    civic = RequireString(property.Value, $"invalid bot script: goal {index}: \"civic\" must be a civic name.");
                    break;
                case "timeoutMinutes":
                    break;
                case "queue":
                    queue = RequireStringArray(property.Value, $"invalid bot script: goal {index}: \"queue\" must be an array of civic names.");
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (civic.Length == 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"civic\" must be a civic name.");
        }

        return new CivicGoal(civic, queue, timeout);
    }

    private static EnsureSupplyGoal ParseSupply(JsonElement element, int index, int timeout)
    {
        string good = string.Empty;
        int perMille = 1000;
        bool fromObject = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "ensureSupply":
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        good = RequireString(property.Value, $"invalid bot script: goal {index}: \"ensureSupply\" must be a good name.");
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        fromObject = true;
                        foreach (JsonProperty inner in property.Value.EnumerateObject())
                        {
                            switch (inner.Name)
                            {
                                case "good":
                                    good = RequireString(inner.Value, $"invalid bot script: goal {index}: \"good\" must be a good name.");
                                    break;
                                case "targetFulfilment":
                                    perMille = RequirePerMille(inner.Value, index);
                                    break;
                                default:
                                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{inner.Name}\".");
                            }
                        }
                    }
                    else
                    {
                        throw new BotScriptException($"invalid bot script: goal {index}: \"ensureSupply\" must be a good name or an object.");
                    }

                    break;
                case "timeoutMinutes":
                    break;
                case "targetFulfilment":
                    if (fromObject)
                    {
                        throw new BotScriptException($"invalid bot script: goal {index}: duplicate key \"targetFulfilment\".");
                    }

                    perMille = RequirePerMille(property.Value, index);
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (good.Length == 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"ensureSupply\" needs a good name.");
        }

        return new EnsureSupplyGoal(good, perMille, timeout);
    }

    private static HousesUntilGoal ParseHouses(JsonElement element, int index, int timeout)
    {
        string tier = string.Empty;
        int residents = 0;
        bool seen = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "housesUntil":
                    seen = true;
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new BotScriptException($"invalid bot script: goal {index}: \"housesUntil\" must be an object like {{\"tier\": \"villagers\", \"residents\": 100}}.");
                    }

                    foreach (JsonProperty inner in property.Value.EnumerateObject())
                    {
                        switch (inner.Name)
                        {
                            case "tier":
                                tier = RequireString(inner.Value, $"invalid bot script: goal {index}: \"tier\" must be a tier name.");
                                break;
                            case "residents":
                                residents = RequirePositiveInt(inner.Value, "residents", index);
                                break;
                            default:
                                throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{inner.Name}\".");
                        }
                    }

                    break;
                case "timeoutMinutes":
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (!seen || tier.Length == 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"housesUntil\" needs a \"tier\".");
        }

        if (residents <= 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"housesUntil\" needs \"residents\" above 0.");
        }

        return new HousesUntilGoal(tier, residents, timeout);
    }

    private static LockHousesGoal ParseLock(JsonElement element, int index, int timeout)
    {
        int count = 0;
        bool seen = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "lockHouses":
                    seen = true;
                    count = RequirePositiveInt(property.Value, "lockHouses", index);
                    break;
                case "timeoutMinutes":
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (!seen)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"lockHouses\" must be a whole number above 0.");
        }

        return new LockHousesGoal(count, timeout);
    }

    private static UnlockHousesGoal ParseUnlock(JsonElement element, int index, int timeout)
    {
        int count = 0;
        bool seen = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "unlockHouses":
                    seen = true;
                    count = RequirePositiveInt(property.Value, "unlockHouses", index);
                    break;
                case "timeoutMinutes":
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (!seen)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"unlockHouses\" must be a whole number above 0.");
        }

        return new UnlockHousesGoal(count, timeout);
    }

    private static WaitUntilTickGoal ParseWait(JsonElement element, int index, int timeout)
    {
        long tick = 0;
        bool seen = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "waitUntilTick":
                    seen = true;
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out tick) || tick <= 0)
                    {
                        throw new BotScriptException($"invalid bot script: goal {index}: \"waitUntilTick\" must be a whole number above 0.");
                    }

                    break;
                case "timeoutMinutes":
                    break;
                default:
                    throw new BotScriptException($"invalid bot script: goal {index}: unknown key \"{property.Name}\".");
            }
        }

        if (!seen)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"waitUntilTick\" must be a whole number above 0.");
        }

        return new WaitUntilTickGoal(tick, timeout);
    }

    private static int ParseTimeout(JsonElement element, int index)
    {
        // The timeout is read from the goal object, so the caller passes the raw element again through Get.
        // Kept separate so every goal kind shares one rule.
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name == "timeoutMinutes")
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out double minutes) || !(minutes > 0))
                {
                    throw new BotScriptException($"invalid bot script: goal {index}: \"timeoutMinutes\" must be a number above 0.");
                }

                long ticks = (long)Math.Round(minutes * TicksPerGameMinute, MidpointRounding.AwayFromZero);
                if (ticks <= 0 || ticks > int.MaxValue)
                {
                    throw new BotScriptException($"invalid bot script: goal {index}: \"timeoutMinutes\" is out of range.");
                }

                return (int)ticks;
            }
        }

        return BotGoal.DefaultTimeoutMinutes * TicksPerGameMinute;
    }

    private static string RequireString(JsonElement value, string message) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is string text && text.Length > 0
            ? text
            : throw new BotScriptException(message);

    private static int RequirePositiveInt(JsonElement value, string key, int index)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number <= 0)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"{key}\" must be a whole number above 0.");
        }

        return number;
    }

    private static int RequirePerMille(JsonElement value, int index)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double ratio) || !(ratio > 0) || ratio > 1)
        {
            throw new BotScriptException($"invalid bot script: goal {index}: \"targetFulfilment\" must be a number above 0 and at most 1.");
        }

        return Math.Clamp((int)Math.Round(ratio * 1000, MidpointRounding.AwayFromZero), 1, 1000);
    }

    private static ImmutableArray<string> RequireStringArray(JsonElement value, string message)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new BotScriptException(message);
        }

        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            names.Add(RequireString(item, message));
        }

        return names.ToImmutable();
    }

    private static void RequireBuilding(ContentDb content, string name, int index)
    {
        if (!content.Buildings.TryGet(name, out _))
        {
            throw new BotScriptException($"invalid bot script: goal {index}: no building named \"{name}\".");
        }
    }

    private static void RequireTech(ContentDb content, string name, int index)
    {
        if (!content.Techs.TryGet(name, out _))
        {
            throw new BotScriptException($"invalid bot script: goal {index}: no technology named \"{name}\".");
        }
    }

    private static void RequireCivic(ContentDb content, string name, int index)
    {
        if (!content.Civics.TryGet(name, out _))
        {
            throw new BotScriptException($"invalid bot script: goal {index}: no civic named \"{name}\".");
        }
    }

    private static void RequireGood(ContentDb content, string name, int index)
    {
        if (!content.Goods.TryGet(name, out _))
        {
            throw new BotScriptException($"invalid bot script: goal {index}: no good named \"{name}\".");
        }
    }

    private static void RequireTier(ContentDb content, string name, int index)
    {
        if (!content.Tiers.TryGet(name, out _))
        {
            throw new BotScriptException($"invalid bot script: goal {index}: no tier named \"{name}\".");
        }
    }
}
