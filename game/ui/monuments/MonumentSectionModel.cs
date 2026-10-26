using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Monuments;

/// <summary>One stage-bill line as the monument section shows it: the good's name and what the stage needs.</summary>
/// <param name="GoodName">The display name of the good.</param>
/// <param name="Need">What the stage's bill asks for, in milli-units.</param>
public sealed record MonumentBillLine(string GoodName, Amount Need);

/// <summary>Whether a monument stage is built, building or waiting.</summary>
public enum MonumentStageState
{
    /// <summary>The stage completed.</summary>
    Done = 0,

    /// <summary>The site is building this stage now.</summary>
    Active = 1,

    /// <summary>The stage is still ahead.</summary>
    Later = 2,
}

/// <summary>One monument stage as the section shows it: its state and its bill.</summary>
/// <param name="Index">The 0-based stage.</param>
/// <param name="State">Whether the stage is built, building or waiting.</param>
/// <param name="Bill">The stage's bill lines.</param>
public sealed record MonumentStageFacts(int Index, MonumentStageState State, IReadOnlyList<MonumentBillLine> Bill);

/// <summary>A monument site as the section shows it, with the stage the site reports.</summary>
/// <param name="Building">The site's entity ID.</param>
/// <param name="Name">The display name.</param>
/// <param name="StageIndex">The 0-based stage under construction, from <c>SiteInfo</c>.</param>
/// <param name="StageCount">How many stages the monument has in all, from <c>SiteInfo</c>; 0 when finished.</param>
/// <param name="ProgressPerMille">The share of the build time that has elapsed, in per-mille.</param>
/// <param name="StallGoodName">The display name of the good the next step waits for, or <see langword="null"/>.</param>
/// <param name="OutOfReach">Whether the site lies beyond every storehouse's reach.</param>
/// <param name="EffectText">The calm sentence naming the effects, on the last stage or when finished.</param>
/// <param name="IsComplete">Whether the monument is finished and its effects apply.</param>
/// <param name="Stages">The stages in build order.</param>
public sealed record MonumentFacts(
    EntityId Building,
    string Name,
    int StageIndex,
    int StageCount,
    int ProgressPerMille,
    string? StallGoodName,
    bool OutOfReach,
    string EffectText,
    bool IsComplete,
    IReadOnlyList<MonumentStageFacts> Stages);

/// <summary>Where the monument section reads its site. The game feeds it from the sim host; the tests feed it fixture facts.</summary>
public interface IMonumentData
{
    /// <summary>Gets the monument facts for a building, or <see langword="null"/> when it is not a monument site or building.</summary>
    /// <param name="building">The building's entity ID.</param>
    /// <returns>The facts, or <see langword="null"/>.</returns>
    MonumentFacts? ForBuilding(EntityId building);
}

/// <summary>One monument stage row as the section shows it: its headline and its bill.</summary>
/// <param name="Headline">The stage headline, such as <c>Stage 1 of 3 — building</c>.</param>
/// <param name="BillText">The stage's bill lines, joined.</param>
/// <param name="State">Whether the stage is built, building or waiting.</param>
public sealed record MonumentStageRow(string Headline, string BillText, MonumentStageState State);

/// <summary>
/// The numbers behind the inspector's monument section, as plain properties the section binds to. It has no Godot
/// types: it reads an <see cref="IMonumentData"/>, formats through <see cref="UiStrings"/> (the <c>ui.events.*</c>
/// locale keys), and refreshes at most four times a second (<see cref="ViewModel"/>).
/// </summary>
public sealed class MonumentSectionModel : ViewModel
{
    private readonly IMonumentData _data;
    private readonly UiStrings _strings;
    private readonly List<MonumentStageRow> _stages = [];

    /// <summary>Initializes a new instance of the <see cref="MonumentSectionModel"/> class.</summary>
    /// <param name="data">Where the monument site comes from.</param>
    /// <param name="strings">The display text.</param>
    public MonumentSectionModel(IMonumentData data, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(strings);
        _data = data;
        _strings = strings;
    }

    /// <summary>Gets or sets the inspected building. The inspector sets it when it opens.</summary>
    public EntityId Target { get; set; } = EntityId.None;

    /// <summary>Gets a value indicating whether the target is a monument site or finished monument.</summary>
    public bool HasMonument { get; private set; }

    /// <summary>Gets the monument's display name.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the stage line, such as <c>Stage 1 of 3</c>, or the finished line.</summary>
    public string StageLine { get; private set; } = string.Empty;

    /// <summary>Gets the progress line, such as <c>45% through this stage</c>, or empty when finished.</summary>
    public string ProgressLine { get; private set; } = string.Empty;

    /// <summary>Gets the stall line, such as <c>Waiting for stone</c>, or empty when the site advances.</summary>
    public string StallLine { get; private set; } = string.Empty;

    /// <summary>Gets the effects line, such as <c>When finished: +4 culture</c>.</summary>
    public string EffectsLine { get; private set; } = string.Empty;

    /// <summary>Gets the stage rows, in build order.</summary>
    public IReadOnlyList<MonumentStageRow> Stages => _stages;

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        _stages.Clear();
        if (Target == EntityId.None || _data.ForBuilding(Target) is not { } facts)
        {
            HasMonument = false;
            Title = _strings.Get("ui.events.monument.title");
            StageLine = string.Empty;
            ProgressLine = string.Empty;
            StallLine = string.Empty;
            EffectsLine = _strings.Get("ui.events.monument.empty");
            return;
        }

        HasMonument = true;
        Title = facts.Name;
        StageLine = facts.IsComplete
            ? _strings.Format(
                "ui.events.monument.stage.finished",
                ("done", NumberFormat.Count(facts.StageCount)),
                ("count", NumberFormat.Count(facts.StageCount)))
            : _strings.Format(
                "ui.events.monument.stage",
                ("done", NumberFormat.Count(Math.Min(facts.StageIndex + 1, facts.StageCount))),
                ("count", NumberFormat.Count(facts.StageCount)));
        ProgressLine = facts.IsComplete
            ? string.Empty
            : _strings.Format(
                "ui.events.monument.progress",
                ("percent", NumberFormat.Count(facts.ProgressPerMille / 10)));
        StallLine = !facts.IsComplete && facts.OutOfReach
            ? _strings.Get("ui.events.monument.reach")
            : !facts.IsComplete && facts.StallGoodName is { } waiting
                ? _strings.Format("ui.events.monument.stall", ("good", waiting))
                : string.Empty;
        EffectsLine = facts.IsComplete
            ? _strings.Format("ui.events.monument.complete", ("effects", facts.EffectText))
            : _strings.Format("ui.events.monument.effects", ("effects", facts.EffectText));
        foreach (MonumentStageFacts stage in facts.Stages)
        {
            _stages.Add(new MonumentStageRow(
                _strings.Format(
                    "ui.events.monument.bill.stage",
                    ("stage", NumberFormat.Count(stage.Index + 1))),
                BillText(stage.Bill),
                stage.State));
        }
    }

    private string BillText(IReadOnlyList<MonumentBillLine> bill)
    {
        var parts = new List<string>();
        foreach (MonumentBillLine line in bill)
        {
            parts.Add(_strings.Format(
                "ui.events.monument.bill.line",
                ("amount", NumberFormat.Units(line.Need)),
                ("good", line.GoodName)));
        }

        return string.Join(", ", parts);
    }
}
