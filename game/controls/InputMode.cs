namespace Evenmere.Game.Controls;

/// <summary>Which kind of device the player used last. It decides how the pointer works and which prompts to show.</summary>
public enum InputMode
{
    /// <summary>The player is using the mouse and keyboard (or a trackpad): the system pointer is the cursor and edge scrolling applies.</summary>
    MouseKeyboard = 0,

    /// <summary>The player is using a gamepad: the right stick moves a virtual tile cursor and edge scrolling is off.</summary>
    Gamepad = 1,
}
