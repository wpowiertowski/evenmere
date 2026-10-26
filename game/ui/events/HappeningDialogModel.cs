using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Game.Ui.Events;

/// <summary>One happening option effect as the dialog previews it, with names already resolved.</summary>
public abstract record HappeningEffectFact
{
    private protected HappeningEffectFact()
    {
    }
}

/// <summary>A goods grant, such as <c>+10 bread</c>.</summary>
/// <param name="GoodName">The display name of the good.</param>
/// <param name="Amount">How much it grants, in milli-units.</param>
public sealed record GrantGoodsFact(string GoodName, Amount Amount) : HappeningEffectFact;

/// <summary>A points grant, such as <c>+12 knowledge</c>.</summary>
/// <param name="KindName">The display name of the point kind.</param>
/// <param name="Amount">How much it grants, in milli-units.</param>
public sealed record GrantPointsFact(string KindName, Amount Amount) : HappeningEffectFact;

/// <summary>A lasting modifier, such as <c>+20% productivity (farms)</c>.</summary>
/// <param name="StatText">The display name of the stat.</param>
/// <param name="Percent">The change in percent.</param>
/// <param name="TargetText">Who it applies to, such as <c>farms</c> or <c>Island-wide</c>.</param>
public sealed record ModifierFact(string StatText, int Percent, string TargetText) : HappeningEffectFact;

/// <summary>A festival request, which becomes a discount on holding that festival.</summary>
/// <param name="FestivalName">The display name of the festival.</param>
/// <param name="DiscountPercent">The discount in percent.</param>
public sealed record FestivalRequestFact(string FestivalName, int DiscountPercent) : HappeningEffectFact;

/// <summary>One happening option as the dialog shows it.</summary>
/// <param name="Index">The option index, for the <c>ChooseHappeningOption</c> command.</param>
/// <param name="Effects">The option's effects, for the preview.</param>
/// <param name="IsDefault">Whether the timeout applies this option when the player waits.</param>
public sealed record HappeningOptionFacts(int Index, IReadOnlyList<HappeningEffectFact> Effects, bool IsDefault);

/// <summary>The pending happening as the dialog shows it.</summary>
/// <param name="Id">The happening.</param>
/// <param name="Title">The display name.</param>
/// <param name="Body">The calm description.</param>
/// <param name="Options">The options, in option order.</param>
/// <param name="DaysLeft">Whole days left before the default option applies.</param>
public sealed record HappeningCardFacts(
    HappeningId Id,
    string Title,
    string Body,
    IReadOnlyList<HappeningOptionFacts> Options,
    int DaysLeft);

/// <summary>Where the happening dialog reads its card. The game feeds it from the sim host; the tests feed it fixture facts.</summary>
public interface IHappeningData
{
    /// <summary>Gets the pending happening's card, or <see langword="null"/> when none waits.</summary>
    HappeningCardFacts? Card { get; }
}

/// <summary>One happening option as the dialog shows it: its preview lines and its wait marker.</summary>
/// <param name="Index">The option index, for the <c>ChooseHappeningOption</c> command.</param>
/// <param name="PreviewText">The effect preview, such as <c>+10 bread; +20% productivity (farms)</c>.</param>
/// <param name="IsDefault">Whether the timeout applies this option when the player waits.</param>
/// <param name="DefaultMark">The wait marker, such as <c>If you wait: Share the catch</c>, or <see langword="null"/>.</param>
public sealed record HappeningOptionRow(int Index, string PreviewText, bool IsDefault, string? DefaultMark);

/// <summary>
/// The numbers behind the happening dialog, as plain properties the dialog binds to. It has no Godot types: it reads
/// an <see cref="IHappeningData"/>, formats through <see cref="UiStrings"/> (the <c>ui.events.*</c> locale keys), and
/// refreshes at most four times a second (<see cref="ViewModel"/>). Choosing sends <c>ChooseHappeningOption</c>;
/// deciding later sends nothing, so the happening stays pending and the feed entry reopens this dialog.
/// </summary>
public sealed class HappeningDialogModel : ViewModel
{
    private readonly IHappeningData _data;
    private readonly UiStrings _strings;
    private readonly List<HappeningOptionRow> _options = [];

    /// <summary>Initializes a new instance of the <see cref="HappeningDialogModel"/> class.</summary>
    /// <param name="data">Where the pending happening comes from.</param>
    /// <param name="strings">The display text.</param>
    public HappeningDialogModel(IHappeningData data, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(strings);
        _data = data;
        _strings = strings;
    }

    /// <summary>Gets a value indicating whether a happening is waiting for a choice.</summary>
    public bool HasPending { get; private set; }

    /// <summary>Gets the happening's display name.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the happening's calm description.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Gets how long the choice can wait, such as <c>3 days to decide</c>.</summary>
    public string DaysLeftText { get; private set; } = string.Empty;

    /// <summary>Gets the options, in option order.</summary>
    public IReadOnlyList<HappeningOptionRow> Options => _options;

    /// <summary>Builds the <c>ChooseHappeningOption</c> command for an option, or <see langword="null"/> when none waits.</summary>
    /// <param name="optionIndex">The option index.</param>
    /// <returns>The command, or <see langword="null"/> when nothing is pending.</returns>
    public Command? ChooseCommand(int optionIndex) =>
        HasPending && _data.Card is { } card ? new ChooseHappeningOption(card.Id, optionIndex) : null;

    /// <summary>Formats one option's effects into its calm preview line.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="effects">The option's effects.</param>
    /// <returns>The preview, with one part per effect.</returns>
    public static string PreviewFor(UiStrings strings, IReadOnlyList<HappeningEffectFact> effects)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(effects);

        var parts = new List<string>(effects.Count);
        foreach (HappeningEffectFact effect in effects)
        {
            parts.Add(effect switch
            {
                GrantGoodsFact granted => strings.Format(
                    "ui.events.preview.grant",
                    ("amount", NumberFormat.Units(granted.Amount)),
                    ("good", granted.GoodName)),
                GrantPointsFact granted => strings.Format(
                    "ui.events.preview.points",
                    ("amount", NumberFormat.Units(granted.Amount)),
                    ("kind", granted.KindName)),
                ModifierFact modifier => strings.Format(
                    "ui.events.preview.modifier",
                    ("percent", NumberFormat.Count(modifier.Percent)),
                    ("stat", modifier.StatText),
                    ("target", modifier.TargetText)),
                FestivalRequestFact request => strings.Format(
                    "ui.events.preview.request",
                    ("discount", NumberFormat.Count(request.DiscountPercent)),
                    ("festival", request.FestivalName)),
                _ => strings.Get("ui.events.effect.unknown"),
            });
        }

        return parts.Count > 0 ? string.Join("; ", parts) : strings.Get("ui.events.effect.unknown");
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        _options.Clear();
        if (_data.Card is not { } card)
        {
            HasPending = false;
            Title = _strings.Get("ui.events.happening.title");
            Body = _strings.Get("ui.events.happening.empty");
            DaysLeftText = string.Empty;
            return;
        }

        HasPending = true;
        Title = card.Title;
        Body = card.Body;
        DaysLeftText = card.DaysLeft <= 1
            ? _strings.Get("ui.events.happening.days_left.one")
            : _strings.Format("ui.events.happening.days_left.other", ("count", NumberFormat.Count(card.DaysLeft)));
        foreach (HappeningOptionFacts option in card.Options)
        {
            string preview = PreviewFor(_strings, option.Effects);
            string? mark = option.IsDefault && card.Options.Count > 1
                ? _strings.Format("ui.events.happening.default_mark", ("option", preview))
                : null;
            _options.Add(new HappeningOptionRow(option.Index, preview, option.IsDefault, mark));
        }
    }
}
