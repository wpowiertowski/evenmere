using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Research;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The one place that turns unlock and bonus effects into the card lines the research and culture trees show
/// (M2-35): <see cref="CivicGraphBuilder.DescribeEffects"/> and the research view-model both read these words,
/// so a tech and a civic granting the same effect name it the same way. It has no Godot types, so the game
/// test project exercises it on its own.
/// </summary>
public static class EffectText
{
    /// <summary>
    /// Turns effects into the unlock lines a tree card shows, in content order.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="effects">The effects to describe.</param>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <param name="statName">How a modifier stat reads inside an effect line; the callers pass their own spelling so the shared lines keep each tree's exact output.</param>
    /// <returns>The unlock lines.</returns>
    public static IReadOnlyList<GraphUnlock> Describe(
        ContentDb content,
        UiStrings strings,
        ImmutableArray<Effect> effects,
        Func<string, string?> iconFor,
        Func<Stat, string> statName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(iconFor);
        ArgumentNullException.ThrowIfNull(statName);

        var unlocks = new List<GraphUnlock>(effects.Length);
        foreach (Effect effect in effects)
        {
            switch (effect)
            {
                case UnlockBuildingEffect unlocked:
                    {
                        string name = content.Buildings.NameOf(unlocked.Building);
                        unlocks.Add(new GraphUnlock(iconFor(name) ?? "\u2022", strings.Get($"building.{name}.name")));
                        break;
                    }

                case UnlockRecipeEffect unlocked:
                    {
                        string name = content.Recipes.NameOf(unlocked.Recipe);
                        unlocks.Add(new GraphUnlock(string.Empty, strings.Get($"recipe.{name}.name")));
                        break;
                    }

                case UnlockTierEffect unlocked:
                    {
                        string name = content.Tiers.NameOf(unlocked.Tier);
                        unlocks.Add(new GraphUnlock("\u25b2", strings.TierName(name)));
                        break;
                    }

                case UnlockTraditionEffect unlocked:
                    {
                        string name = content.Traditions.NameOf(unlocked.Tradition);
                        unlocks.Add(new GraphUnlock("\u2726", strings.Get($"tradition.{name}.name")));
                        break;
                    }

                case UnlockFestivalEffect unlocked:
                    {
                        string name = content.Festivals.NameOf(unlocked.Festival);
                        unlocks.Add(new GraphUnlock("\u2726", strings.Get($"festival.{name}.name")));
                        break;
                    }

                case TraditionSlotEffect slot:
                    unlocks.Add(new GraphUnlock("+", SlotName(strings, slot.Slot)));
                    break;

                case ModifierEffect modifier:
                    unlocks.Add(new GraphUnlock(
                        string.Empty,
                        $"+{modifier.Percent}% {statName(modifier.Stat)}"));
                    break;

                case GrantGoodsEffect granted:
                    {
                        var parts = new List<string>(granted.Goods.Length);
                        foreach (GoodAmount goods in granted.Goods)
                        {
                            parts.Add($"+{NumberFormat.Units(goods.Amount)} {strings.GoodName(content.Goods.NameOf(goods.Good))}");
                        }

                        unlocks.Add(new GraphUnlock(string.Empty, string.Join(", ", parts)));
                        break;
                    }

                case GrantPointsEffect granted:
                    {
                        var parts = new List<string>(granted.Points.Length);
                        foreach (PointAmount points in granted.Points)
                        {
                            parts.Add($"+{NumberFormat.Units(points.Amount)} {ContentNames.NameOf(points.Kind)}");
                        }

                        unlocks.Add(new GraphUnlock(string.Empty, string.Join(", ", parts)));
                        break;
                    }

                default:
                    break;
            }
        }

        return unlocks;
    }

    /// <summary>
    /// Humanizes a modifier stat's JSON name for display: the content spelling (<c>output.culture</c>,
    /// <c>constructionSpeed</c>) reads as calm words (<c>culture output</c>, <c>construction speed</c>).
    /// Output stats name what they make first, so the good comes first.
    /// </summary>
    /// <param name="stat">The stat.</param>
    /// <returns>The display name, all lowercase to sit inside effect lines.</returns>
    public static string StatName(Stat stat)
    {
        string raw = ContentNames.NameOf(stat);
        const string OutputPrefix = "output.";
        string reordered = raw.StartsWith(OutputPrefix, StringComparison.Ordinal)
            ? raw[OutputPrefix.Length..] + " output"
            : raw;
        var words = new System.Text.StringBuilder(reordered.Length + 4);
        foreach (char letter in reordered)
        {
            if (letter is '_' or '.' or ' ')
            {
                words.Append(' ');
            }
            else if (char.IsUpper(letter))
            {
                words.Append(' ');
                words.Append(char.ToLowerInvariant(letter));
            }
            else
            {
                words.Append(letter);
            }
        }

        return words.ToString().TrimStart(' ');
    }

    /// <summary>
    /// Names a tradition slot type from the locale table (<c>ui.traditions.slot.*</c>).
    /// </summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slot">The slot type.</param>
    /// <returns>The display name.</returns>
    public static string SlotName(UiStrings strings, TraditionSlotType slot)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return strings.Get($"ui.traditions.slot.{slot.ToString().ToLowerInvariant()}");
    }
}
