namespace Evenmere.Content.Lint;

/// <summary>
/// The rule codes the linter adds to the loader's <see cref="DiagnosticCodes.Evl001"/> (schema and shape errors) and
/// <see cref="DiagnosticCodes.Evl002"/> (unresolved references). EVL009 and EVL010 arrive with later milestones.
/// </summary>
public static class LintCodes
{
    /// <summary>EVL003: a tech or civic tree has a cycle, or a node that can never be reached from the roots.</summary>
    public const string Evl003 = "EVL003";

    /// <summary>EVL004: a good has no producer or no consumer.</summary>
    public const string Evl004 = "EVL004";

    /// <summary>EVL005: a building, recipe, tier, tradition or festival has no unlock source, or more than one.</summary>
    public const string Evl005 = "EVL005";

    /// <summary>EVL006: a tier's basic needs cannot be met with items unlocked by its era or earlier.</summary>
    public const string Evl006 = "EVL006";

    /// <summary>EVL007: an item has no display name (an error) or no description (a warning) in <c>locale/en</c>.</summary>
    public const string Evl007 = "EVL007";

    /// <summary>EVL008: an era gateway is missing or wrong, or a node requires a node of a later era.</summary>
    public const string Evl008 = "EVL008";

    /// <summary>EVL011: a tier with an advanceFrom tier must need strictly more basic goods per resident than that tier.</summary>
    public const string Evl011 = "EVL011";
}
