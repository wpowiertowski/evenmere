using Evenmere.Content.Loading;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL007: every item has its display name, <c>&lt;type&gt;.&lt;id&gt;.name</c>, in <c>locale/en</c> (an error), and ideally
/// a description, <c>&lt;type&gt;.&lt;id&gt;.desc</c> (a warning). The type is the singular name, such as <c>building</c>.
/// The rules file has no display text.
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
