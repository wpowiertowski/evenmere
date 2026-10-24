using Godot;

namespace Evenmere.Game.Controls;

/// <summary>
/// The names of the input actions declared in <c>project.godot</c>. Game code uses these constants instead of string
/// literals, so a typo is a compile error and the action list has one home. Never test raw key codes (AGENTS.md).
/// <para>
/// Gamepad map (M1-23, global activation in the M1-23 follow-up): the d-pad moves focus (and the left stick the
/// camera) only, never game actions. Y opens the build menu, LB and RB switch its tabs (and rotate the camera
/// everywhere else), A confirms the focused button through the built-in <c>ui_accept</c> (game/project.godot
/// extends it with Joypad Button 0; with no GUI focus the tools still take A as <c>cursor_confirm</c>), B cancels
/// and closes the topmost panel through <c>ui_cancel</c> (Joypad Button 1) and <c>tool_cancel</c>, Start pauses, and
/// Back (Select) moves focus into and out of the HUD, whose speed buttons are then driven with the d-pad and A.
/// There are no direct gamepad speed bindings: keyboard 1-3 keep their speed hotkeys, and the build menu
/// suspends them while it is open so they can pick entries instead.
/// </para>
/// </summary>
public static class InputActions
{
    /// <summary>Pan the camera left (A, left arrow, left stick).</summary>
    public static readonly StringName CameraPanLeft = "camera_pan_left";

    /// <summary>Pan the camera right (D, right arrow, left stick).</summary>
    public static readonly StringName CameraPanRight = "camera_pan_right";

    /// <summary>Pan the camera away from the viewer (W, up arrow, left stick).</summary>
    public static readonly StringName CameraPanUp = "camera_pan_up";

    /// <summary>Pan the camera toward the viewer (S, down arrow, left stick).</summary>
    public static readonly StringName CameraPanDown = "camera_pan_down";

    /// <summary>Zoom in (mouse wheel up, =, +, right trigger).</summary>
    public static readonly StringName CameraZoomIn = "camera_zoom_in";

    /// <summary>Zoom out (mouse wheel down, -, left trigger).</summary>
    public static readonly StringName CameraZoomOut = "camera_zoom_out";

    /// <summary>Turn the view one quarter turn to the left (Q, left shoulder).</summary>
    public static readonly StringName CameraRotateLeft = "camera_rotate_left";

    /// <summary>Turn the view one quarter turn to the right (E, right shoulder).</summary>
    public static readonly StringName CameraRotateRight = "camera_rotate_right";

    /// <summary>Hold to pan by dragging the map (middle mouse button).</summary>
    public static readonly StringName CameraDrag = "camera_drag";

    /// <summary>Open or close the build menu (B, Y).</summary>
    public static readonly StringName UiBuildMenu = "ui_build_menu";

    /// <summary>Move gamepad focus into or out of the HUD (Back/Select). Gamepad only; the HUD handles it (M1-18). The d-pad then moves between the speed buttons and A presses one.</summary>
    public static readonly StringName UiFocusHud = "ui_focus_hud";

    /// <summary>Open or close the research panel (T, left stick press).</summary>
    public static readonly StringName UiResearch = "ui_research";

    /// <summary>Open or close the civics panel (C, right stick press).</summary>
    public static readonly StringName UiCivics = "ui_civics";

    /// <summary>Rotate the building or tool being placed (R, X).</summary>
    public static readonly StringName ToolRotate = "tool_rotate";

    /// <summary>Start the road tool (keyboard F only; gamepad players reach it through the build menu).</summary>
    public static readonly StringName ToolRoad = "tool_road";

    /// <summary>Start the demolish tool (keyboard X and Delete; gamepad players reach it through the build menu).</summary>
    public static readonly StringName ToolDemolish = "tool_demolish";

    /// <summary>Cancel the current tool or close the topmost panel (Escape, right click, B).</summary>
    public static readonly StringName ToolCancel = "tool_cancel";

    /// <summary>Pause or resume the simulation (Space, Start).</summary>
    public static readonly StringName SpeedPause = "speed_pause";

    /// <summary>Run the simulation at normal speed (keyboard 1; gamepad players use the HUD speed buttons).</summary>
    public static readonly StringName Speed1 = "speed_1";

    /// <summary>Run the simulation at double speed (keyboard 2; gamepad players use the HUD speed buttons).</summary>
    public static readonly StringName Speed2 = "speed_2";

    /// <summary>Run the simulation at quadruple speed (keyboard 3; gamepad players use the HUD speed buttons).</summary>
    public static readonly StringName Speed3 = "speed_3";

    /// <summary>Move the gamepad tile cursor left (right stick).</summary>
    public static readonly StringName CursorLeft = "cursor_left";

    /// <summary>Move the gamepad tile cursor right (right stick).</summary>
    public static readonly StringName CursorRight = "cursor_right";

    /// <summary>Move the gamepad tile cursor up the screen (right stick).</summary>
    public static readonly StringName CursorUp = "cursor_up";

    /// <summary>Move the gamepad tile cursor down the screen (right stick).</summary>
    public static readonly StringName CursorDown = "cursor_down";

    /// <summary>Confirm at the pointer: place, select or click (left click, A).</summary>
    public static readonly StringName CursorConfirm = "cursor_confirm";

    /// <summary>Show or hide the terrain tile-grid overlay (keyboard G only; gamepad access will come from a view or settings menu).</summary>
    public static readonly StringName ViewGridToggle = "view_grid_toggle";

    /// <summary>Switch between fullscreen and the windowed mode (keyboard F11, or Ctrl+Cmd+F on macOS; gamepad players use a HUD or pause-menu button). No gamepad binding: the d-pad, sticks, A and B are reserved for focus navigation.</summary>
    public static readonly StringName ToggleFullscreen = "toggle_fullscreen";
}
