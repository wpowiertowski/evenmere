using Evenmere.Content.Definitions;
using Evenmere.Content.Loading;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL007: every item has its display name, <c>&lt;type&gt;.&lt;id&gt;.name</c>, in <c>locale/en</c> (an error), and ideally
/// a description, <c>&lt;type&gt;.&lt;id&gt;.desc</c> (a warning). The type is the singular name, such as <c>building</c>.
/// Every happening option that names a <c>label</c> locale key must have that key in <c>locale/en</c> (an error),
/// so the happening dialog never shows a raw key. The rules file has no display text.
/// </summary>
internal static class LocaleRule
{
    public static void Run(LintContext context, RawContent raw)
    {
        foreach (RawItem item in raw.Items)
        {
            if (item.Type == ContentType.Rules)
            {
                continue;
            }

            string prefix = $"{item.Type.SingularName()}.{item.Id}";
            Check(context, item, $"{prefix}.name", isError: true);
            Check(context, item, $"{prefix}.desc", isError: false);
        }

        CheckHappeningLabels(context);
    }

    // An option label is a locale key the dialog shows on the button, so a missing key is an error like a
    // missing name. Options without a label fall back to the effect preview and need nothing.
    private static void CheckHappeningLabels(LintContext context)
    {
        for (int h = 0; h < context.Db.Happenings.Count; h++)
        {
            string name = context.Db.Happenings.Names[h];
            foreach ((int index, string? label) in LabelsOf(context.Db.Happenings.Items[h]))
            {
                if (label is null)
                {
                    continue;
                }

                string file = context.FileOf(ContentType.Happenings, name);
                string pointer = LintContext.Pointer("options", index.ToString(System.Globalization.CultureInfo.InvariantCulture), "label");
                if (!context.Locale.ContainsKey(label))
                {
                    context.Error(
                        LintCodes.Evl007,
                        file,
                        pointer,
                        $"happening '{name}' option {index} names label \"{label}\", which is missing: add \"{label}\" to a file in locale/en");
                }
                else if (!context.Locale.TryGet(label, out string text) || string.IsNullOrWhiteSpace(text))
                {
                    context.Error(
                        LintCodes.Evl007,
                        file,
                        pointer,
                        $"happening '{name}' option {index} label \"{label}\" in locale/en must be a single non-empty string");
                }
            }
        }
    }

    private static IEnumerable<(int Index, string? Label)> LabelsOf(HappeningDef happening)
    {
        for (int i = 0; i < happening.Options.Length; i++)
        {
            yield return (i, happening.Options[i].Label);
        }
    }

    private static void Check(LintContext context, RawItem item, string key, bool isError)
    {
        string what = isError ? "display name" : "description";
        string? problem = null;
        if (!context.Locale.ContainsKey(key))
        {
            problem = $"{what} is missing: add \"{key}\" to a file in locale/en";
        }
        else if (!context.Locale.TryGet(key, out string text))
        {
            problem = $"{what} \"{key}\" in locale/en is a list of names; it must be a single string";
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            problem = $"{what} \"{key}\" in locale/en is empty";
        }

        if (problem is null)
        {
            return;
        }

        string message = $"{item.Type.SingularName()} '{item.Id}' {problem}";
        if (isError)
        {
            context.Error(LintCodes.Evl007, item.File, string.Empty, message);
        }
        else
        {
            context.Warning(LintCodes.Evl007, item.File, string.Empty, message);
        }
    }
}
