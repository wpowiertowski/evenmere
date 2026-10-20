using System.Text;
using Evenmere.Content.Locale;

namespace Evenmere.Game.Ui.Common;

/// <summary>
/// The only way UI code gets display text (AGENTS.md: never hard-code display text). It wraps a <see cref="LocaleTable"/>
/// and adds named placeholders. A key with no text gives back the key itself, so a missing string is visible but never fatal.
/// <para>
/// Placeholders are named, <c>"Day {day}, {season}"</c>, so a translator can reorder them. A <c>{name}</c> with no matching
/// argument is left as written. The class has no Godot types.
/// </para>
/// </summary>
public sealed class UiStrings
{
    private readonly LocaleTable _table;

    /// <summary>Initializes a new instance of the <see cref="UiStrings"/> class.</summary>
    /// <param name="table">The merged locale table, which the <c>SimHost</c> loads from the same content root as the simulation's content.</param>
    public UiStrings(LocaleTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
    }

    /// <summary>Gets the text for a key.</summary>
    /// <param name="key">The locale key, such as <c>ui.hud.speed.x1</c>.</param>
    /// <returns>The text, or <paramref name="key"/> itself when there is none.</returns>
    public string Get(string key) => _table.Get(key);

    /// <summary>Gets the text for a key and fills in its named placeholders.</summary>
    /// <param name="key">The locale key.</param>
    /// <param name="args">The placeholder names (without braces) and their values.</param>
    /// <returns>The text with each <c>{name}</c> replaced; just the key when the key has no text.</returns>
    public string Format(string key, params (string Name, string Value)[] args) => Fill(_table.Get(key), args);

    /// <summary>Gets the display name of a good.</summary>
    /// <param name="goodId">The good's string ID, such as <c>timber</c>.</param>
    /// <returns>The name from <c>good.&lt;id&gt;.name</c>, or that key.</returns>
    public string GoodName(string goodId) => _table.Get($"good.{goodId}.name");

    /// <summary>Gets the display name of a population tier.</summary>
    /// <param name="tierId">The tier's string ID, such as <c>settlers</c>.</param>
    /// <returns>The name from <c>tier.&lt;id&gt;.name</c>, or that key.</returns>
    public string TierName(string tierId) => _table.Get($"tier.{tierId}.name");

    /// <summary>Replaces each <c>{name}</c> in a template in a single pass, so a value that contains braces is never substituted again.</summary>
    /// <param name="template">The template.</param>
    /// <param name="args">The placeholder names and their values.</param>
    /// <returns>The filled text.</returns>
    internal static string Fill(string template, (string Name, string Value)[] args)
    {
        if (args.Length == 0 || !template.Contains('{', StringComparison.Ordinal))
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
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
            string name = template.Substring(open + 1, close - open - 1);
            if (TryFind(args, name, out string value))
            {
                result.Append(value);
            }
            else
            {
                result.Append(template, open, close - open + 1);
            }

            position = close + 1;
        }

        return result.ToString();
    }

    private static bool TryFind((string Name, string Value)[] args, string name, out string value)
    {
        foreach ((string argName, string argValue) in args)
        {
            if (string.Equals(argName, name, StringComparison.Ordinal))
            {
                value = argValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
