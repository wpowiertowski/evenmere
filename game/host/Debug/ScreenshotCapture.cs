using System.Globalization;
using Godot;

namespace Evenmere.Game.Host.Debug;

/// <summary>
/// A development aid, registered as an autoload. It does nothing unless the user arguments (after Godot's <c>--</c>) contain
/// <c>--screenshot=&lt;path&gt;</c>. Then, after N rendered frames (<c>--screenshot-after-frames=N</c>, default 30) it saves the
/// root viewport as a PNG and quits with exit code 0 (1 if the file could not be written). The window size comes from Godot's own
/// <c>--resolution WxH</c> argument. <c>--screenshot-focus=&lt;node path&gt;</c> optionally gives that node the keyboard/gamepad
/// focus first, so the focus ring can be captured.
/// </summary>
public sealed partial class ScreenshotCapture : Node
{
    private const string PathPrefix = "--screenshot=";
    private const string FramesPrefix = "--screenshot-after-frames=";
    private const string FocusPrefix = "--screenshot-focus=";
    private const int DefaultFrames = 30;

    private string _path = string.Empty;
    private string? _focusPath;
    private int _framesLeft;
    private bool _captured;

    /// <inheritdoc />
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string? path = ValueOf(args, PathPrefix);
        if (string.IsNullOrEmpty(path))
        {
            // Not asked for: stay out of the way, and stop being called.
            SetProcess(false);
            return;
        }

        _path = Path.GetFullPath(path);
        _focusPath = ValueOf(args, FocusPrefix);
        _framesLeft = DefaultFrames;

        string? frames = ValueOf(args, FramesPrefix);
        if (frames is not null)
        {
            if (!int.TryParse(frames, NumberStyles.None, CultureInfo.InvariantCulture, out _framesLeft))
            {
                GD.PushError($"[Evenmere] --screenshot-after-frames needs a non-negative whole number, not '{frames}'.");
                GetTree().Quit(1);
                return;
            }
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_captured)
        {
            return;
        }

        if (_framesLeft > 0)
        {
            _framesLeft--;
            return;
        }

        _captured = true;
        _ = CaptureAsync();
    }

    private static string? ValueOf(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private async Task CaptureAsync()
    {
        try
        {
            if (_focusPath is not null)
            {
                GetTree().Root.GetNodeOrNull<Control>(_focusPath)?.GrabFocus();

                // One more frame, so the focus style has been drawn before the capture.
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // The frame has to be fully drawn before the viewport texture holds it.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            Image image = GetViewport().GetTexture().GetImage();
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Error error = image.SavePng(_path);
            if (error != Error.Ok)
            {
                GD.PushError($"[Evenmere] Screenshot: could not write '{_path}': {error}");
                GetTree().Quit(1);
                return;
            }

            GD.Print($"[Evenmere] Screenshot saved: {_path} ({image.GetWidth()}x{image.GetHeight()})");
            GetTree().Quit(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[Evenmere] Screenshot: {ex.Message}");
            GetTree().Quit(1);
        }
    }
}
