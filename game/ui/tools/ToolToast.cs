using Evenmere.Game.Ui.Progress;
using Godot;

namespace Evenmere.Game.Ui.Tools;

/// <summary>
/// The small calm stack that tells the player when one of their own tool commands was refused (M1-21). It shows at
/// most three notes at once, oldest first, each fading after a few seconds. M1-26's notification feed may absorb it
/// later, so it stays a small separate component with a single <c>Show</c> entry point.
/// </summary>
public sealed partial class ToolToast : VBoxContainer
{
    private const double ShowSeconds = 6.0;
    private const int MaxNotes = 3;

    private readonly List<(PanelContainer Note, double Left)> _notes = new();

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        MouseFilter = Control.MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 8);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            (PanelContainer note, double left) = _notes[i];
            left -= delta;
            if (left <= 0.0)
            {
                _notes.RemoveAt(i);
                RemoveChild(note);
                note.QueueFree();
            }
            else
            {
                _notes[i] = (note, left);
            }
        }
    }

    /// <summary>Shows a note, dropping the oldest one when three are already showing.</summary>
    /// <param name="text">The note text, already resolved from locale.</param>
    public void Show(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        while (_notes.Count >= MaxNotes)
        {
            (PanelContainer oldest, _) = _notes[0];
            _notes.RemoveAt(0);
            RemoveChild(oldest);
            oldest.QueueFree();
        }

        var note = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        note.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        AddChild(note);
        _notes.Add((note, ShowSeconds));
        GD.Print($"[Evenmere] Tools: toast '{text}'");
    }
}
