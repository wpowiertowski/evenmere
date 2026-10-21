using MessagePack;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// The save header (ADR-0007): the meta the view and the tools show without touching the body, the content hash the
/// save was written with, the state hash the body must still have, and one string-ID table per content kind, each
/// listing the string IDs in the order the save's integer IDs refer to them. Explicit integer keys, never reused.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
internal sealed class SaveHeader
{
    /// <summary>Gets or sets the format version the save was written with.</summary>
    [Key(0)]
    public int FormatVersion { get; set; } = SaveFormat.CurrentVersion;

    /// <summary>Gets or sets the game's version text.</summary>
    [Key(1)]
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the content hash the save was written with.</summary>
    [Key(2)]
    public ulong ContentHash { get; set; }

    /// <summary>Gets or sets the world seed.</summary>
    [Key(3)]
    public ulong Seed { get; set; }

    /// <summary>Gets or sets the island ID, or <see langword="null"/> for the empty world.</summary>
    [Key(4)]
    public string? MapId { get; set; }

    /// <summary>Gets or sets the tick the save was written at.</summary>
    [Key(5)]
    public long Tick { get; set; }

    /// <summary>Gets or sets the current era's raw ID.</summary>
    [Key(6)]
    public int Era { get; set; }

    /// <summary>Gets or sets the total residents.</summary>
    [Key(7)]
    public long Population { get; set; }

    /// <summary>Gets or sets the state hash of the body: XxHash64 over the uncompressed canonical bytes.</summary>
    [Key(8)]
    public ulong StateHash { get; set; }

    /// <summary>Gets or sets the good string IDs in save-ID order.</summary>
    [Key(9)]
    public string[] Goods { get; set; } = [];

    /// <summary>Gets or sets the building-type string IDs in save-ID order.</summary>
    [Key(10)]
    public string[] Buildings { get; set; } = [];

    /// <summary>Gets or sets the recipe string IDs in save-ID order.</summary>
    [Key(11)]
    public string[] Recipes { get; set; } = [];

    /// <summary>Gets or sets the tier string IDs in save-ID order.</summary>
    [Key(12)]
    public string[] Tiers { get; set; } = [];

    /// <summary>Gets or sets the tech string IDs in save-ID order.</summary>
    [Key(13)]
    public string[] Techs { get; set; } = [];

    /// <summary>Gets or sets the civic string IDs in save-ID order.</summary>
    [Key(14)]
    public string[] Civics { get; set; } = [];

    /// <summary>Gets or sets the tradition string IDs in save-ID order.</summary>
    [Key(15)]
    public string[] Traditions { get; set; } = [];

    /// <summary>Gets or sets the festival string IDs in save-ID order.</summary>
    [Key(16)]
    public string[] Festivals { get; set; } = [];

    /// <summary>Gets or sets the great-work-type string IDs in save-ID order.</summary>
    [Key(17)]
    public string[] GreatWorks { get; set; } = [];

    /// <summary>Gets or sets the monument string IDs in save-ID order.</summary>
    [Key(18)]
    public string[] Monuments { get; set; } = [];

    /// <summary>Gets or sets the happening string IDs in save-ID order.</summary>
    [Key(19)]
    public string[] Happenings { get; set; } = [];

    /// <summary>Gets or sets the era string IDs in save-ID order.</summary>
    [Key(20)]
    public string[] Eras { get; set; } = [];

    /// <summary>Gets or sets the terrain string IDs in save-ID order.</summary>
    [Key(21)]
    public string[] Terrains { get; set; } = [];
}
