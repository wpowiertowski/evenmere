using Godot;

namespace Evenmere.Game.Ui.Common;

/// <summary>The kind of icon: which <c>game/assets/icons/&lt;kind&gt;/</c> folder it lives in.</summary>
public enum IconKind
{
    /// <summary>A good in the HUD strip: <c>game/assets/icons/goods/&lt;id&gt;.png</c>.</summary>
    Good,

    /// <summary>A build-menu entry: <c>game/assets/icons/buildings/&lt;id&gt;.png</c>.</summary>
    Building,

    /// <summary>A population-tier badge: <c>game/assets/icons/tiers/&lt;id&gt;.png</c>.</summary>
    Tier,

    /// <summary>A build-menu tab or knowledge/culture chip: <c>game/assets/icons/ui/&lt;name&gt;.png</c>.</summary>
    Ui,
}

/// <summary>
/// Resolves a content ID and a kind to its ART-02 icon texture, by convention from
/// <c>game/assets/icons/</c>: the file name is the content ID exactly (no code path
/// branches on a content ID). A missing icon returns <see langword="null"/> instead
/// of failing, so the caller falls back to the placeholder glyph and a menu never blanks.
/// </summary>
public static class IconLibrary
{
    /// <summary>The Godot root the icons load from.</summary>
    public const string ResRoot = "res://assets/icons";

    /// <summary>The folder under <see cref="ResRoot"/> for a kind.</summary>
    /// <param name="kind">The icon kind.</param>
    /// <returns>The folder name, such as <c>goods</c>.</returns>
    public static string FolderFor(IconKind kind) => kind switch
    {
        IconKind.Good => "goods",
        IconKind.Building => "buildings",
        IconKind.Tier => "tiers",
        IconKind.Ui => "ui",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown icon kind."),
    };

    /// <summary>The <c>res://</c> texture path for an icon, whether the file exists or not.</summary>
    /// <param name="kind">The icon kind.</param>
    /// <param name="id">The content ID (or UI name) exactly as the file is named.</param>
    /// <returns>For example <c>res://assets/icons/goods/bread.png</c>.</returns>
    public static string ResPathFor(IconKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return $"{ResRoot}/{FolderFor(kind)}/{id}.png";
    }

    /// <summary>Loads a good icon, or <see langword="null"/> when it is missing.</summary>
    /// <param name="id">The content good ID, such as <c>bread</c>.</param>
    /// <returns>The cached texture, or <see langword="null"/> for the glyph fallback.</returns>
    public static Texture2D? LoadGood(string id) => Load(IconKind.Good, id);

    /// <summary>Loads a building icon, or <see langword="null"/> when it is missing.</summary>
    /// <param name="id">The content building ID, such as <c>bakery</c>.</param>
    /// <returns>The cached texture, or <see langword="null"/> for the glyph fallback.</returns>
    public static Texture2D? LoadBuilding(string id) => Load(IconKind.Building, id);

    /// <summary>Loads a tier badge, or <see langword="null"/> when it is missing.</summary>
    /// <param name="id">The content tier ID, such as <c>settlers</c>.</param>
    /// <returns>The cached texture, or <see langword="null"/> for the glyph fallback.</returns>
    public static Texture2D? LoadTier(string id) => Load(IconKind.Tier, id);

    /// <summary>Loads a UI icon (a build-menu tab, knowledge or culture), or <see langword="null"/> when missing.</summary>
    /// <param name="name">The UI name, such as <c>production</c>.</param>
    /// <returns>The cached texture, or <see langword="null"/> for the glyph fallback.</returns>
    public static Texture2D? LoadUi(string name) => Load(IconKind.Ui, name);

    /// <summary>Loads an icon by kind, or <see langword="null"/> when it is missing.</summary>
    /// <param name="kind">The icon kind.</param>
    /// <param name="id">The content ID (or UI name) exactly as the file is named.</param>
    /// <returns>The cached texture (Godot shares it between callers), or <see langword="null"/>.</returns>
    public static Texture2D? Load(IconKind kind, string id)
    {
        string path = ResPathFor(kind, id);
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }

        return GD.Load<Texture2D>(path);
    }
}
