using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Notifications;

/// <summary>
/// Which history filter chip a notification belongs to. Building covers construction, People covers tier moves,
/// Progress covers research, civics and inspirations, Advice covers advisor notes, and Events covers festivals,
/// monuments and happenings (M2-27).
/// </summary>
public enum NotificationCategory : byte
{
    /// <summary>A building finished construction.</summary>
    Building = 0,

    /// <summary>A house changed tier.</summary>
    People = 1,

    /// <summary>A tech, civic or inspiration completed.</summary>
    Progress = 2,

    /// <summary>An advisor note was raised.</summary>
    Advice = 3,

    /// <summary>A festival, monument or happening moment (M2-27).</summary>
    Events = 4,
}

/// <summary>
/// One row of the notification history: what happened, when, and what it is about. Entries are session-only and never
/// saved; the newest entry is shown first. A batch (several completions of one kind on one day) is a single entry whose
/// count grows in place.
/// </summary>
/// <param name="Id">The entry's number, unique within the session.</param>
/// <param name="Tick">The tick the entry (or its latest batch addition) happened on.</param>
/// <param name="Category">Which filter chip shows the entry.</param>
/// <param name="Title">The display text, already resolved from locale.</param>
/// <param name="Note">An optional quiet second line, such as the gentle decline reassurance; <see langword="null"/> for none.</param>
/// <param name="Subject">What the entry is about; clicking focuses it.</param>
/// <param name="Tech">The tech, for research entries; otherwise <see langword="null"/>.</param>
/// <param name="Civic">The civic, for civic entries; otherwise <see langword="null"/>.</param>
/// <param name="Count">How many happenings the entry batches; 1 for a single.</param>
/// <param name="Happening">The happening, for happening entries; otherwise <see langword="null"/>.</param>
public sealed record NotificationEntry(
    int Id,
    long Tick,
    NotificationCategory Category,
    string Title,
    string? Note,
    SubjectLink Subject,
    TechId? Tech,
    CivicId? Civic,
    int Count,
    HappeningId? Happening = null)
{
    /// <summary>Creates a single (unbatched) entry.</summary>
    /// <param name="id">The entry's number.</param>
    /// <param name="tick">The tick it happened on.</param>
    /// <param name="category">Which filter chip shows it.</param>
    /// <param name="title">The display text.</param>
    /// <param name="note">An optional second line.</param>
    /// <param name="subject">What it is about.</param>
    /// <param name="tech">The tech, for research entries.</param>
    /// <param name="civic">The civic, for civic entries.</param>
    /// <param name="happening">The happening, for happening entries.</param>
    /// <returns>The entry.</returns>
    public static NotificationEntry Create(
        int id,
        long tick,
        NotificationCategory category,
        string title,
        string? note,
        SubjectLink subject,
        TechId? tech = null,
        CivicId? civic = null,
        HappeningId? happening = null) => new(id, tick, category, title, note, subject, tech, civic, 1, happening);
}
