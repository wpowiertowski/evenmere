using System.Globalization;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Explain;

/// <summary>
/// Godot-free formatting of <see cref="Explanation"/> trees for <see cref="ExplanationView"/>.
/// It turns each line's locale key and typed arguments into display text, using
/// <see cref="NumberFormat"/> and the content names. The view supplies indentation,
/// icons and clickable links; this class supplies only strings, so it can be unit-tested.
/// </summary>
public static class ExplainFormat
{
    /// <summary>Formats one typed argument by its unit.</summary>
    /// <param name="arg">The argument.</param>
    /// <param name="content">The content database, for ID names.</param>
    /// <param name="strings">The display text.</param>
    /// <returns>The text, such as <c>12.5</c>, <c>50%</c> or <c>Bread</c>.</returns>
    public static string FormatArg(ExplainArg arg, ContentDb content, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        return arg.Kind switch
        {
            ExplainArgKind.Number => NumberFormat.Count(arg.AsNumber()),
            ExplainArgKind.Amount => NumberFormat.Rate(arg.AsAmount()),
            ExplainArgKind.Ratio => FormatPercent(arg.AsRatio()),
            ExplainArgKind.Good => strings.GoodName(content.NameOf(arg.AsGood())),
            ExplainArgKind.BuildingType => BuildingName(content, strings, arg.AsBuildingType()),
            ExplainArgKind.Tier => strings.TierName(content.NameOf(arg.AsTier())),
            ExplainArgKind.Recipe => strings.Get($"recipe.{content.NameOf(arg.AsRecipe())}.name"),
            ExplainArgKind.Tech => strings.Get($"tech.{content.NameOf(arg.AsTech())}.name"),
            ExplainArgKind.Civic => strings.Get($"civic.{content.NameOf(arg.AsCivic())}.name"),
            ExplainArgKind.Entity => $"#{arg.AsEntity().Value}",
            ExplainArgKind.Service => ServiceName(arg.AsService()),
            ExplainArgKind.Season => SeasonName(arg.AsSeason(), strings),
            ExplainArgKind.Stall => StallName(arg.AsStall()),
            _ => arg.Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Formats one line: looks up its locale template and fills <c>{0}</c>, <c>{1}</c>, … with formatted arguments.</summary>
    /// <param name="line">The line.</param>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <returns>The display text; the key itself when the template holds no placeholders.</returns>
    public static string FormatLine(ExplanationLine line, ContentDb content, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        string template = strings.Get(line.Key);
        if (line.Args.IsEmpty)
        {
            return template;
        }

        string[] formatted = new string[line.Args.Length];
        for (int i = 0; i < formatted.Length; i++)
        {
            formatted[i] = FormatArg(line.Args[i], content, strings);
        }

        return FillPositional(template, formatted);
    }

    /// <summary>Formats a per-mille ratio as a percent.</summary>
    /// <param name="ratio">The ratio; 1000 is 100%.</param>
    /// <returns>The text, such as <c>100%</c> or <c>12.5%</c>.</returns>
    public static string FormatPercent(Ratio ratio)
    {
        int perMille = ratio.PerMille;
        if (perMille % 10 == 0)
        {
            return $"{NumberFormat.Count(perMille / 10)}%";
        }

        double percent = perMille / 10.0;
        return $"{percent.ToString("F1", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>Formats a road cost in cost units as whole dirt tiles.</summary>
    /// <param name="cost">The cost; 1000 is one dirt tile.</param>
    /// <returns>The text, such as <c>3 tiles</c>.</returns>
    public static string FormatCost(int cost)
    {
        double tiles = cost / 1000.0;
        return tiles % 1.0 == 0
            ? $"{NumberFormat.Count((long)tiles)} tiles"
            : $"{tiles.ToString("F1", CultureInfo.InvariantCulture)} tiles";
    }

    /// <summary>
    /// Formats a storehouse-reach line as tiles of road: reach is <c>reach × 1000</c> cost units and a
    /// dirt-road tile costs 1000, so the road cost reads as tiles. This is the one place that turns a
    /// cost into tiles, so every reach line in the game reads the same.
    /// </summary>
    /// <param name="cost">The road cost from the storehouse, in cost units (1000 = one dirt tile).</param>
    /// <param name="reach">The storehouse's reach in tiles, from its content storage spec.</param>
    /// <param name="strings">The display text.</param>
    /// <returns>The text, such as <c>Storehouse: about 6 tiles away (reach 20).</c>.</returns>
    public static string FormatReach(int cost, int reach, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        if (cost <= 0)
        {
            return strings.Format("ui.inspector.reach.here", ("reach", NumberFormat.Count(reach)));
        }

        int tiles = Math.Max(1, (cost + 500) / 1000);
        return strings.Format(
            "ui.inspector.reach.detail",
            ("tiles", NumberFormat.Count(tiles)),
            ("reach", NumberFormat.Count(reach)));
    }

    /// <summary>Picks a status glyph for a line from its locale key.</summary>
    /// <param name="key">The locale key.</param>
    /// <returns><c>✓</c> for good news, <c>!</c> for problems, <c>•</c> otherwise.</returns>
    public static string IconForKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Contains(".missing", StringComparison.Ordinal)
            || key.Contains(".shortage", StringComparison.Ordinal)
            || key.Contains(".unmet", StringComparison.Ordinal)
            || key.Contains("no_reach", StringComparison.Ordinal)
            || key.Contains("no_road", StringComparison.Ordinal)
            || key.Contains("no_market", StringComparison.Ordinal)
            || key.Contains(".stall", StringComparison.Ordinal)
            || key.Contains(".waiting_for", StringComparison.Ordinal)
            || key.Contains("blocker.", StringComparison.Ordinal) && !key.EndsWith(".none", StringComparison.Ordinal)
            || key.Contains(".cause.", StringComparison.Ordinal))
        {
            return "!";
        }

        if (key.Contains(".met", StringComparison.Ordinal)
            || key.Contains(".ready", StringComparison.Ordinal)
            || key.Contains(".road", StringComparison.Ordinal) && !key.Contains("no_", StringComparison.Ordinal)
            || key.EndsWith(".none", StringComparison.Ordinal)
            || key.Contains("fix.none", StringComparison.Ordinal))
        {
            return "✓";
        }

        return "•";
    }

    /// <summary>Counts how many lines a depth-limited walk of the tree would show (for tests).</summary>
    /// <param name="line">The root line.</param>
    /// <param name="maxDepth">The deepest level shown, where 0 is the root alone.</param>
    /// <returns>The visible line count.</returns>
    public static int CountVisible(ExplanationLine line, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (maxDepth < 0)
        {
            return 0;
        }

        int count = 1;
        if (maxDepth == 0)
        {
            return count;
        }

        foreach (ExplanationLine child in line.Children)
        {
            count += CountVisible(child, maxDepth - 1);
        }

        return count;
    }

    private static string FillPositional(string template, string[] args)
    {
        var result = new System.Text.StringBuilder(template.Length + 16);
        int position = 0;
        while (position < template.Length)
        {
            int open = template.IndexOf('{', position);
            int close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            result.Append(template, position, open - position);
            string inside = template.Substring(open + 1, close - open - 1);
            if (int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && (uint)index < (uint)args.Length)
            {
                result.Append(args[index]);
            }
            else
            {
                result.Append(template, open, close - open + 1);
            }

            position = close + 1;
        }

        return result.ToString();
    }

    private static string BuildingName(ContentDb content, UiStrings strings, BuildingTypeId type)
    {
        string key = $"building.{content.NameOf(type)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(type) : text;
    }

    private static string ServiceName(ServiceKind service) => service switch
    {
        ServiceKind.Market => "Market",
        ServiceKind.Shrine => "Shrine",
        _ => service.ToString(),
    };

    private static string SeasonName(Core.Time.Season season, UiStrings strings) => season switch
    {
        Core.Time.Season.Spring => strings.Get("ui.hud.season.spring"),
        Core.Time.Season.Summer => strings.Get("ui.hud.season.summer"),
        Core.Time.Season.Autumn => strings.Get("ui.hud.season.autumn"),
        Core.Time.Season.Winter => strings.Get("ui.hud.season.winter"),
        _ => season.ToString(),
    };

    private static string StallName(StallReason reason) => reason switch
    {
        StallReason.None => "working",
        StallReason.Paused => "paused",
        StallReason.NoStorehouseReach => "no storehouse reach",
        StallReason.NoWorkforce => "no workforce",
        StallReason.MissingInput => "missing input",
        StallReason.OutputFull => "output full",
        StallReason.Underfunded => "underfunded",
        StallReason.MissingMaterial => "missing material",
        _ => reason.ToString(),
    };
}
