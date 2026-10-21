using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// Gives the research, civics, toast and toggle controls the shared game theme. The HUD gets it from its scene;
/// these panels are built in code, so they load the same <c>evenmere_theme.tres</c> here.
/// </summary>
public static class PanelTheme
{
    private const string ThemePath = "res://ui/theme/evenmere_theme.tres";

    /// <summary>Applies the shared theme to a panel root, so it looks like the HUD.</summary>
    /// <param name="root">The control to theme.</param>
    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (ResourceLoader.Load<Theme>(ThemePath) is { } theme)
        {
            root.Theme = theme;
        }
    }
}
