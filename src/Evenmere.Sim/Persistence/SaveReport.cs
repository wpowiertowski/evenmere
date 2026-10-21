namespace Evenmere.Sim.Persistence;

/// <summary>
/// What loading a save changed or dropped to fit the current content. The common case (the save's tables equal the
/// current content) skips the remap entirely and reports nothing; otherwise every entry says what moved, what became a
/// ruin and what was dropped, once per item. Unknown goods, techs and civics are reported once each, with what was lost.
/// </summary>
public sealed class SaveReport
{
    /// <summary>Gets the ID remaps that moved state, one line per content kind that shifted.</summary>
    public List<string> Remapped { get; } = [];

    /// <summary>Gets the buildings that became ruins because their type is gone, one line per building.</summary>
    public List<string> Ruins { get; } = [];

    /// <summary>Gets the state that was dropped because its content is gone, one line per unknown item.</summary>
    public List<string> Dropped { get; } = [];

    /// <summary>Gets the notes that are informative but changed nothing, such as a content-hash mismatch.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Gets a value indicating whether loading changed nothing: no remap, no ruin and no drop.</summary>
    public bool IsClean => Remapped.Count == 0 && Ruins.Count == 0 && Dropped.Count == 0;
}
