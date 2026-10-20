using Godot;

namespace Evenmere.Game.Controls;

/// <summary>
/// Follows the device the player touched last and raises <see cref="ModeChanged"/> when it switches between mouse and
/// keyboard and gamepad. The HUD can use it to swap button prompts, and the camera uses it to switch between the system
/// pointer and the virtual tile cursor.
/// </summary>
public sealed partial class InputModeTracker : Node
{
    /// <summary>
    /// How far a stick or trigger must be pushed before it counts as the player using the gamepad. It is above the
    /// usual drift of a worn stick, so a resting controller never steals the mode from the mouse.
    /// </summary>
    public const float GamepadAxisThreshold = 0.4f;

    /// <summary>The least the mouse must move in one event, in pixels, to count as the player using it. It ignores zero-distance motion events the OS sends when a window gains focus.</summary>
    public const float MouseMoveThreshold = 1f;

    /// <summary>Gets the tracker in the running scene, or <see langword="null"/> when none is in the tree. Until an autoload exists, it is hosted by the camera rig scene.</summary>
    public static InputModeTracker? Instance { get; private set; }

    /// <summary>Raised with the new mode, once, whenever the mode changes.</summary>
    public event Action<InputMode>? ModeChanged;

    /// <summary>Gets the mode of the device used last. It starts as <see cref="InputMode.MouseKeyboard"/>.</summary>
    public InputMode Mode { get; private set; } = InputMode.MouseKeyboard;

    /// <summary>
    /// Says which mode an input event belongs to.
    /// </summary>
    /// <param name="inputEvent">The event.</param>
    /// <returns>The mode it indicates, or <see langword="null"/> when it says nothing about the device (key releases, stick drift, touch, window events).</returns>
    public static InputMode? Classify(InputEvent inputEvent) => inputEvent switch
    {
        InputEventKey { Pressed: true } => InputMode.MouseKeyboard,
        InputEventMouseButton => InputMode.MouseKeyboard,
        InputEventMouseMotion motion when motion.Relative.Length() >= MouseMoveThreshold => InputMode.MouseKeyboard,
        InputEventPanGesture or InputEventMagnifyGesture => InputMode.MouseKeyboard,
        InputEventJoypadButton { Pressed: true } => InputMode.Gamepad,
        InputEventJoypadMotion motion when MathF.Abs(motion.AxisValue) >= GamepadAxisThreshold => InputMode.Gamepad,
        _ => null,
    };

    /// <inheritdoc />
    public override void _EnterTree() => Instance = this;

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        InputMode? mode = Classify(@event);
        if (mode is { } next && next != Mode)
        {
            Mode = next;
            ModeChanged?.Invoke(next);
        }
    }
}
