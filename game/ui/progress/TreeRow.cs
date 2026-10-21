using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// The root of one research or civics row. Queued rows are drag sources and drop targets: dragging one onto
/// another queued row reorders the queue. It carries no sim knowledge; the panel wires
/// <see cref="DropReceived"/> to the queue command.
/// </summary>
public sealed partial class TreeRow : HBoxContainer
{
    /// <summary>Gets or sets the row's key, used only for debugging.</summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the position in the queue, or -1 when the row is not queued and cannot be dragged.</summary>
    public int QueuePosition { get; set; } = -1;

    /// <summary>Raised when a dragged queued row is dropped onto this row.</summary>
    public Action<int, TreeRow>? DropReceived { get; set; }

    /// <inheritdoc />
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (QueuePosition < 0)
        {
            return new Variant();
        }

        var preview = new Label { Text = RowKey };
        SetDragPreview(preview);
        return Variant.From(QueuePosition);
    }

    /// <inheritdoc />
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return QueuePosition >= 0
            && data.VariantType == Variant.Type.Int
            && data.AsInt32() != QueuePosition;
    }

    /// <inheritdoc />
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType == Variant.Type.Int)
        {
            DropReceived?.Invoke(data.AsInt32(), this);
        }
    }
}
