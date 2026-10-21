using Godot;

namespace Evenmere.Game.Controls;

/// <summary>
/// The names of the input actions declared in <c>project.godot</c>. Game code uses these constants instead of string
/// literals, so a typo is a compile error and the action list has one home. Never test raw key codes (AGENTS.md).
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

    /// <summary>Move gamepad focus into or out of the HUD (Back/Select). Gamepad only; the HUD handles it (M1-18).</summary>
    public static readonly StringName UiFocusHud = "ui_focus_hud";

    /// <summary>Open or close the research panel (T, left stick press).</summary>
    public static readonly StringName UiResearch = "ui_research";

    /// <summary>Open or close the civics panel (C, right stick press).</summary>
    public static readonly StringName UiCivics = "ui_civics";

    /// <summary>Rotate the building or tool being placed (R, X).</summary>
    public static readonly StringName ToolRotate = "tool_rotate";

    /// <summary>Cancel the current tool or close the topmost panel (Escape, right click, B).</summary>
    public static readonly StringName ToolCancel = "tool_cancel";

    /// <summary>Pause or resume the simulation (Space, Start).</summary>
    public static readonly StringName SpeedPause = "speed_pause";

    /// <summary>Run the simulation at normal speed (1, d-pad left).</summary>
    public static readonly StringName Speed1 = "speed_1";

    /// <summary>Run the simulation at double speed (2, d-pad down).</summary>
    public static readonly StringName Speed2 = "speed_2";

    /// <summary>Run the simulation at quadruple speed (3, d-pad right).</summary>
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
}
