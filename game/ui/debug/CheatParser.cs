using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Debug.Commands;

namespace Evenmere.Game.Ui.Debug;

/// <summary>
/// Parses cheat console lines into simulation commands (ADR-0012). Pure logic, so the game tests cover it without
/// the engine: content names resolve through <see cref="ContentDb.TryGet{TId}"/>, amounts through
/// <see cref="Amount.Parse"/> and headcounts through plain integers. Anything it cannot parse comes back as a
/// calm, player-facing error; unknown content names name the kind the console looked in.
/// </summary>
public static class CheatParser
{
    /// <summary>Gets the one-line help the console prints for <c>help</c> and for unknown input.</summary>
    public static string Usage =>
        "cheats: grant <good> <amount> | tech <id> | civic <id> | era <id> | reveal all | reveal <region> | residents <n> | overlay";

    /// <summary>Parses one console line.</summary>
    /// <param name="text">The line, without the leading prompt.</param>
    /// <param name="content">The loaded content, for name resolution.</param>
    /// <param name="command">The command to enqueue, when parsing succeeded.</param>
    /// <param name="error">The player-facing error, when parsing failed.</param>
    /// <returns><see langword="true"/> when the line parsed into a command.</returns>
    public static bool TryParse(string text, ContentDb content, out Command? command, out string error)
    {
        ArgumentNullException.ThrowIfNull(content);
        command = null;
        error = string.Empty;

        string[] parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = Usage;
            return false;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "grant" when parts.Length == 3:
                return Grant(parts[1], parts[2], content, out command, out error);
            case "tech" when parts.Length == 2:
                return CompleteTech(parts[1], content, out command, out error);
            case "civic" when parts.Length == 2:
                return CompleteCivic(parts[1], content, out command, out error);
            case "era" when parts.Length == 2:
                return SetEra(parts[1], content, out command, out error);
            case "reveal" when parts.Length == 2:
                return Reveal(parts[1], out command, out error);
            case "residents" when parts.Length == 2:
                return AddResidents(parts[1], out command, out error);
            default:
                error = $"Unknown cheat '{text}'. {Usage}";
                return false;
        }
    }

    private static bool Grant(string name, string amount, ContentDb content, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (!content.TryGet<GoodId>(name, out GoodId good))
        {
            error = $"No good '{name}'.";
            return false;
        }

        Amount parsed;
        try
        {
            parsed = Amount.Parse(amount);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            error = $"'{amount}' is not an amount: write a decimal like 10 or 2.5.";
            return false;
        }

        if (parsed < Amount.Zero)
        {
            error = "A grant cannot be negative.";
            return false;
        }

        command = new CheatGrantGoods(good, parsed);
        return true;
    }

    private static bool CompleteTech(string name, ContentDb content, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (!content.TryGet<TechId>(name, out TechId tech))
        {
            error = $"No tech '{name}'.";
            return false;
        }

        command = new CheatCompleteNode(tech, null);
        return true;
    }

    private static bool CompleteCivic(string name, ContentDb content, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (!content.TryGet<CivicId>(name, out CivicId civic))
        {
            error = $"No civic '{name}'.";
            return false;
        }

        command = new CheatCompleteNode(null, civic);
        return true;
    }

    private static bool SetEra(string name, ContentDb content, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (!content.TryGet<EraId>(name, out EraId era))
        {
            error = $"No era '{name}'.";
            return false;
        }

        command = new CheatSetEra(era);
        return true;
    }

    private static bool Reveal(string target, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
        {
            command = new CheatRevealAll(CheatRevealAll.AllRegions);
            return true;
        }

        if (int.TryParse(target, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int region) && region >= 0)
        {
            command = new CheatRevealAll(region);
            return true;
        }

        error = $"A reveal names 'all' or a region number, not '{target}'.";
        return false;
    }

    private static bool AddResidents(string count, out Command? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
        {
            command = new CheatAddResidents(parsed);
            return true;
        }

        error = $"'{count}' is not a headcount: write a whole number of at least 1.";
        return false;
    }
}
