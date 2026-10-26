using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Traditions;
using Godot;

namespace Evenmere.Game.Ui.Culture;

/// <summary>One tradition slot as the board shows it: its position and the card it holds, if any.</summary>
/// <param name="Slot">The slot type.</param>
/// <param name="Index">Which slot of that type.</param>
/// <param name="Tradition">The slotted card, or <see langword="null"/> when the slot is empty.</param>
public sealed record SlotEntry(TraditionSlotType Slot, int Index, TraditionId? Tradition);

/// <summary>One slot group as the board shows it: the slots of one type with a shared header.</summary>
/// <param name="Slot">The slot type.</param>
/// <param name="Title">The group header, such as <c>Hearth slot</c>.</param>
/// <param name="Entries">The slots of this type, in index order.</param>
public sealed record SlotGroup(TraditionSlotType Slot, string Title, IReadOnlyList<SlotEntry> Entries);

/// <summary>One tradition card as the board and drawer show it.</summary>
/// <param name="Id">The tradition.</param>
/// <param name="ContentId">The content string ID, used as the drag payload.</param>
/// <param name="Name">The display name.</param>
/// <param name="SlotText">The slot kind, such as <c>Hearth slot</c>.</param>
/// <param name="EffectText">The effect lines, joined into one calm sentence.</param>
/// <param name="StyleBadge">The style key for style cards, or <see langword="null"/>.</param>
/// <param name="SlottedElsewhere">Whether the card sits in another slot already.</param>
/// <param name="Status">Where the card is slotted, such as <c>In the Hearth slot</c>, or <see langword="null"/> when it is free.</param>
public sealed record TraditionCardData(
    TraditionId Id,
    string ContentId,
    string Name,
    string SlotText,
    string EffectText,
    string? StyleBadge,
    bool SlottedElsewhere,
    string? Status);

/// <summary>One sampled bonus behind the after-swap summary: what was measured and where it applies.</summary>
/// <param name="Stat">The stat.</param>
/// <param name="Context">The measured context.</param>
/// <param name="Target">Who gets the bonus, such as <c>farms</c> or <c>Island-wide</c>.</param>
/// <param name="Season">The season the bonus needs, or <see langword="null"/> when it always applies.</param>
public sealed record BonusProbe(Stat Stat, ModifierContext Context, string Target, string? Season);

/// <summary>One measured bonus: the probe and the island's answer, in percent.</summary>
/// <param name="Probe">What was measured.</param>
/// <param name="Percent">The total in percent.</param>
public sealed record ProbedBonus(BonusProbe Probe, int Percent);

/// <summary>
/// The pure rules behind the traditions board (M2-19): slot grouping, drawer building, bonus probes and the
/// calm change summary. It is plain C# with no Godot types, so the game test project exercises it on its own.
/// Every string the board shows lives in <c>content/locale/en/ui-culture.json</c> and is read through
/// <see cref="UiStrings"/>; there are no code fallbacks.
/// </summary>
public static class TraditionsBoardModel
{
    /// <summary>Groups granted slots by type for the board's slot columns. The input is already sorted.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slots">The granted slots with their cards, in ascending (slot type, index) order.</param>
    /// <returns>One group per slot type present, in first-appearance order.</returns>
    public static IReadOnlyList<SlotGroup> GroupSlots(UiStrings strings, IReadOnlyList<TraditionSlotView> slots)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(slots);
        var groups = new List<SlotGroup>();
        var backs = new List<List<SlotEntry>>();
        foreach (TraditionSlotView slot in slots)
        {
            if (backs.Count == 0 || groups[^1].Slot != slot.Slot)
            {
                groups.Add(new SlotGroup(slot.Slot, CivicGraphBuilder.SlotName(strings, slot.Slot), []));
                backs.Add(new List<SlotEntry>());
            }

            backs[^1].Add(new SlotEntry(slot.Slot, slot.Index, slot.Tradition));
        }

        for (int i = 0; i < groups.Count; i++)
        {
            groups[i] = groups[i] with { Entries = backs[i] };
        }

        return groups;
    }

    /// <summary>Builds the drawer's cards: every unlocked card with its words, marking the ones already slotted.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="unlocked">The unlocked traditions, in ascending ID order.</param>
    /// <param name="slottedSlot">The slot a card sits in, or <see langword="null"/> when it is free.</param>
    /// <param name="iconFor">The placeholder glyph for a content building ID, or <see langword="null"/> when it has none.</param>
    /// <returns>The drawer cards, in unlocked order.</returns>
    public static IReadOnlyList<TraditionCardData> BuildDrawer(
        ContentDb content,
        UiStrings strings,
        IReadOnlyList<TraditionId> unlocked,
        Func<TraditionId, TraditionSlotType?> slottedSlot,
        Func<string, string?> iconFor)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(unlocked);
        ArgumentNullException.ThrowIfNull(slottedSlot);
        ArgumentNullException.ThrowIfNull(iconFor);

        var cards = new List<TraditionCardData>(unlocked.Count);
        foreach (TraditionId id in unlocked)
        {
            if ((uint)id.Value >= (uint)content.Traditions.Count)
            {
                continue;
            }

            TraditionDef def = content.Get(id);
            string name = content.Traditions.NameOf(id);
            string display = strings.Get($"tradition.{name}.name");
            var lines = new List<string>();
            foreach (GraphUnlock unlock in CivicGraphBuilder.DescribeEffects(content, strings, def.Effects, iconFor))
            {
                lines.Add(unlock.Glyph.Length > 0 ? $"{unlock.Glyph}  {unlock.Text}" : unlock.Text);
            }

            bool style = HasTag(def.Tags, "style");
            TraditionSlotType? slotted = slottedSlot(id);
            string? status = slotted is { } filled
                ? strings.Format("ui.traditions.slotted", ("slot", CivicGraphBuilder.SlotName(strings, filled)))
                : null;
            cards.Add(new TraditionCardData(
                id,
                name,
                display,
                CivicGraphBuilder.SlotName(strings, def.Slot),
                lines.Count > 0 ? string.Join("; ", lines) : strings.Get($"tradition.{name}.desc"),
                style ? def.Style : null,
                slotted is not null,
                status));
        }

        return cards;
    }

    /// <summary>
    /// Builds the bonus probes for a swap's summary from the swapped cards' own modifier effects: one probe per
    /// (stat, context), carrying the target and season words the summary line names. The panel snapshots these
    /// probes before the swap and again after <c>TraditionChanged</c>, and <see cref="SummarizeChanges"/> reports
    /// the deltas, so a line reads <c>farms productivity +20% (winter)</c>: what gets the bonus, by how much,
    /// and in which season.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <param name="names">The condition display names.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="effects">The swapped cards' modifier effects.</param>
    /// <returns>The probes, deduplicated.</returns>
    public static IReadOnlyList<BonusProbe> ProbesFor(
        ContentDb content,
        IConditionNames names,
        UiStrings strings,
        System.Collections.Immutable.ImmutableArray<Effect> effects)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(strings);

        var probes = new List<BonusProbe>();
        foreach (Effect effect in effects)
        {
            if (effect is not ModifierEffect modifier)
            {
                continue;
            }

            string? season = modifier.When is SeasonCondition winter ? names.Season(winter.Season) : null;
            foreach ((ModifierContext context, string target) in TargetsFor(content, names, strings, modifier.Target))
            {
                var probe = new BonusProbe(modifier.Stat, context, target, season);
                if (!probes.Contains(probe))
                {
                    probes.Add(probe);
                }
            }
        }

        return probes;
    }

    /// <summary>Measures every probe through the progression queries.</summary>
    /// <param name="progression">The progression queries.</param>
    /// <param name="probes">What to measure.</param>
    /// <returns>The measured percents, parallel to <paramref name="probes"/>.</returns>
    public static IReadOnlyList<ProbedBonus> SnapshotBonuses(
        IProgressionQueries progression,
        IReadOnlyList<BonusProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(probes);

        var measured = new List<ProbedBonus>(probes.Count);
        foreach (BonusProbe probe in probes)
        {
            measured.Add(new ProbedBonus(probe, progression.BonusPercent(probe.Stat, probe.Context)));
        }

        return measured;
    }

    /// <summary>
    /// Turns two probe snapshots into the calm summary the board shows after a swap. One line per probe whose
    /// total moved, naming the target, the stat, the signed delta and the season when the bonus has one.
    /// </summary>
    /// <param name="strings">The display text.</param>
    /// <param name="before">The snapshot taken before the swap.</param>
    /// <param name="after">The snapshot taken after the swap.</param>
    /// <returns>The summary lines, in probe order; empty when nothing moved.</returns>
    public static IReadOnlyList<string> SummarizeChanges(
        UiStrings strings,
        IReadOnlyList<ProbedBonus> before,
        IReadOnlyList<ProbedBonus> after)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var afterByProbe = new Dictionary<BonusProbe, int>(after.Count);
        foreach (ProbedBonus measured in after)
        {
            afterByProbe[measured.Probe] = measured.Percent;
        }

        var lines = new List<string>();
        foreach (ProbedBonus measured in before)
        {
            if (!afterByProbe.TryGetValue(measured.Probe, out int updated))
            {
                continue;
            }

            int delta = updated - measured.Percent;
            if (delta == 0)
            {
                continue;
            }

            string amount = Signed(delta) + "%";
            string stat = ContentNames.NameOf(measured.Probe.Stat);
            // The summary leads the line with the target, capitalised the way the card example reads
            // ("Farms +20% (Winter)"); condition names stay lowercase where sentences use them.
            string target = Capitalize(measured.Probe.Target);
            lines.Add(measured.Probe.Season is { } season
                ? strings.Format(
                    "ui.traditions.change.seasoned",
                    ("target", target),
                    ("stat", stat),
                    ("delta", amount),
                    ("season", season))
                : strings.Format(
                    "ui.traditions.change.plain",
                    ("target", target),
                    ("stat", stat),
                    ("delta", amount)));
        }

        return lines;
    }

    private static IReadOnlyList<(ModifierContext Context, string Target)> TargetsFor(
        ContentDb content,
        IConditionNames names,
        UiStrings strings,
        ModifierTarget target)
    {
        string island = strings.Get("ui.traditions.target.island");
        switch (target)
        {
            case BuildingsTarget buildings:
                return BuildingTargets(content, names, buildings.Selector, island);
            case TierTarget tier:
                return [(ModifierContext.ForTier(tier.Tier), names.Tier(tier.Tier))];
            case GoodTarget good:
                return [(ModifierContext.ForGood(good.Good), names.Good(good.Good))];
            case DiscoveryTarget discovery:
                return [(ModifierContext.Island, names.Discovery(discovery.Kind))];
            default:
                return [(ModifierContext.Island, island)];
        }
    }

    private static IReadOnlyList<(ModifierContext Context, string Target)> BuildingTargets(
        ContentDb content,
        IConditionNames names,
        BuildingSelector selector,
        string island)
    {
        switch (selector)
        {
            case BuildingTypeSelector single:
                return (uint)single.Building.Value < (uint)content.Buildings.Count
                    ? [(ModifierContext.ForBuildingType(single.Building), names.Building(single.Building))]
                    : [(ModifierContext.Island, island)];
            case BuildingTagSelector tagged:
                return RepresentativeOf(content, tagged.Tag) is { } representative
                    ? [(ModifierContext.ForBuildingType(representative), names.Tag(tagged.Tag))]
                    : [(ModifierContext.Island, names.Tag(tagged.Tag))];
            case BuildingCategorySelector grouped:
                return RepresentativeOf(content, grouped.Category) is { } member
                    ? [(ModifierContext.ForBuildingType(member), names.Category(grouped.Category))]
                    : [(ModifierContext.Island, island)];
            default:
                return [(ModifierContext.Island, island)];
        }
    }

    private static BuildingTypeId? RepresentativeOf(ContentDb content, string tag)
    {
        for (int i = 0; i < content.Buildings.Count; i++)
        {
            var id = BuildingTypeId.From(checked((ushort)i));
            foreach (string candidate in content.Get(id).Tags)
            {
                if (string.Equals(candidate, tag, StringComparison.Ordinal))
                {
                    return id;
                }
            }
        }

        return null;
    }

    private static BuildingTypeId? RepresentativeOf(ContentDb content, BuildingCategory category)
    {
        for (int i = 0; i < content.Buildings.Count; i++)
        {
            var id = BuildingTypeId.From(checked((ushort)i));
            if (content.Get(id).Category == category)
            {
                return id;
            }
        }

        return null;
    }

    private static string Signed(int percent) =>
        (percent >= 0 ? "+" : string.Empty) + percent.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static bool HasTag(System.Collections.Immutable.ImmutableArray<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The traditions board: granted slots grouped by type, owned cards in a drawer, and the calm bonus summary.
/// Cards are picked with a press (or dragged with the mouse) and placed with a press on a slot; the slot's own
/// clear button unslots. Every control is a focusable button through the built-in <c>ui_*</c> focus actions, so
/// the picker works with gamepad focus alone and no new input actions are added.
/// </summary>
public sealed partial class TraditionsBoard : VBoxContainer
{
    /// <summary>Raised when a slot should change: place <paramref cref="tradition"/>, or clear it when <see langword="null"/>.</summary>
    public event Action<TraditionSlotType, int, TraditionId?>? SlotRequested;

    private Label? _title;
    private Label? _slotsTitle;
    private VBoxContainer? _slotGroups;
    private ScrollContainer? _slotScroll;
    private Label? _drawerTitle;
    private VBoxContainer? _drawer;
    private ScrollContainer? _drawerScroll;
    private Label? _pending;
    private Label? _style;
    private Label? _summary;
    private UiStrings? _strings;
    private IReadOnlyDictionary<string, TraditionId>? _dragIds;

    private TraditionId? _pendingPick;
    private readonly Dictionary<(TraditionSlotType Slot, int Index), SlotRow> _slotRows = new();
    private readonly Dictionary<TraditionId, TraditionCardView> _cardViews = new();

    /// <summary>
    /// Shows the board's data, reusing slot rows and cards by identity so keyboard and gamepad focus survive refreshes.
    /// </summary>
    /// <param name="strings">The display text.</param>
    /// <param name="groups">The slot groups.</param>
    /// <param name="drawer">The drawer cards, in unlocked order.</param>
    /// <param name="cardsById">Every shown card by tradition, for slot button text.</param>
    /// <param name="dragIds">The content IDs the drop targets resolve, to traditions.</param>
    /// <param name="styleLine">The style preview line, or <see langword="null"/> when no style is slotted.</param>
    /// <param name="summary">The bonus summary lines; hidden when empty.</param>
    public void SetData(
        UiStrings strings,
        IReadOnlyList<SlotGroup> groups,
        IReadOnlyList<TraditionCardData> drawer,
        IReadOnlyDictionary<TraditionId, TraditionCardData> cardsById,
        IReadOnlyDictionary<string, TraditionId> dragIds,
        string? styleLine,
        IReadOnlyList<string> summary)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(cardsById);
        ArgumentNullException.ThrowIfNull(dragIds);
        ArgumentNullException.ThrowIfNull(summary);
        EnsureChrome();
        _strings = strings;
        _dragIds = dragIds;

        _title!.Text = strings.Get("ui.traditions.title");
        _slotsTitle!.Text = strings.Get("ui.traditions.slots");
        _drawerTitle!.Text = strings.Get("ui.traditions.drawer");

        SyncSlots(strings, groups, cardsById);
        SyncDrawer(drawer);
        SyncPending(strings);
        SyncStyleAndSummary(strings, styleLine, summary);
    }

    /// <summary>Clears the picker's pending pick, for example after the command was sent.</summary>
    public void ClearPending()
    {
        _pendingPick = null;
        foreach (TraditionCardView view in _cardViews.Values)
        {
            view.SetPicked(false);
        }

        SyncPending(_strings);
    }

    private void EnsureChrome()
    {
        if (_title is not null)
        {
            return;
        }

        AddThemeConstantOverride("separation", 6);

        var title = new Label();
        AddChild(title);
        _title = title;

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        AddChild(columns);

        var slotScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(300, 0),
        };
        columns.AddChild(slotScroll);
        _slotScroll = slotScroll;

        var slotColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        slotColumn.AddThemeConstantOverride("separation", 6);
        slotScroll.AddChild(slotColumn);

        var slotsTitle = new Label();
        slotsTitle.AddThemeFontSizeOverride("font_size", 16);
        slotColumn.AddChild(slotsTitle);
        _slotsTitle = slotsTitle;

        var slotGroups = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        slotGroups.AddThemeConstantOverride("separation", 6);
        slotColumn.AddChild(slotGroups);
        _slotGroups = slotGroups;

        var drawerScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        columns.AddChild(drawerScroll);
        _drawerScroll = drawerScroll;

        var drawerColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        drawerColumn.AddThemeConstantOverride("separation", 6);
        drawerScroll.AddChild(drawerColumn);

        var drawerTitle = new Label();
        drawerTitle.AddThemeFontSizeOverride("font_size", 16);
        drawerColumn.AddChild(drawerTitle);
        _drawerTitle = drawerTitle;

        var drawer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        drawer.AddThemeConstantOverride("separation", 6);
        drawerColumn.AddChild(drawer);
        _drawer = drawer;

        var pending = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        pending.AddThemeFontSizeOverride("font_size", 15);
        AddChild(pending);
        _pending = pending;

        var style = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        style.AddThemeFontSizeOverride("font_size", 15);
        AddChild(style);
        _style = style;

        var summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        summary.AddThemeFontSizeOverride("font_size", 15);
        AddChild(summary);
        _summary = summary;
    }

    private void SyncSlots(UiStrings strings, IReadOnlyList<SlotGroup> groups, IReadOnlyDictionary<TraditionId, TraditionCardData> cardsById)
    {
        var wanted = new HashSet<(TraditionSlotType Slot, int Index)>();
        foreach (SlotGroup group in groups)
        {
            foreach (SlotEntry entry in group.Entries)
            {
                wanted.Add((entry.Slot, entry.Index));
            }
        }

        var gone = new List<(TraditionSlotType Slot, int Index)>();
        foreach ((TraditionSlotType slot, int index) in _slotRows.Keys)
        {
            if (!wanted.Contains((slot, index)))
            {
                gone.Add((slot, index));
            }
        }

        foreach ((TraditionSlotType slot, int index) in gone)
        {
            _slotRows[(slot, index)].Root.QueueFree();
            _slotRows.Remove((slot, index));
        }

        string empty = strings.Get("ui.traditions.slot.empty");
        string clearTip = strings.Get("ui.traditions.clear.tip");

        foreach (SlotGroup group in groups)
        {
            foreach (SlotEntry entry in group.Entries)
            {
                if (!_slotRows.TryGetValue((entry.Slot, entry.Index), out SlotRow? row))
                {
                    row = CreateSlotRow(entry.Slot, entry.Index);
                    _slotRows[(entry.Slot, entry.Index)] = row;
                    _slotGroups!.AddChild(row.Root);
                }

                string name = entry.Tradition is { } tradition && cardsById.TryGetValue(tradition, out TraditionCardData? card)
                    ? card.Name
                    : empty;
                row.Place.Text = $"{name} · {group.Title}";
                row.Place.TooltipText = entry.Tradition is { } filled && cardsById.TryGetValue(filled, out TraditionCardData? held)
                    ? held.EffectText
                    : group.Title;
                row.Clear.TooltipText = clearTip;
                row.Clear.Visible = entry.Tradition is not null;
                row.Group = group.Title;
            }
        }

        // Empty state: the groups box holds only rows, so a friends-and-family note needs its own label.
        _slotGroups!.GetNodeOrNull<Label>("EmptyNote")?.QueueFree();
        if (groups.Count == 0)
        {
            var note = new Label
            {
                Name = "EmptyNote",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Text = strings.Get("ui.traditions.slots.empty"),
            };
            note.AddThemeFontSizeOverride("font_size", 15);
            _slotGroups.AddChild(note);
        }
    }

    private SlotRow CreateSlotRow(TraditionSlotType slot, int index)
    {
        var root = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);

        var place = new SlotDropButton(this, slot, index)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All,
            Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        root.AddChild(place);

        var clear = new Button
        {
            Text = "×",
            FocusMode = Control.FocusModeEnum.All,
        };
        root.AddChild(clear);

        var row = new SlotRow(root, place, clear, string.Empty);
        place.Pressed += () => OnSlotPressed(slot, index);
        clear.Pressed += () => SlotRequested?.Invoke(slot, index, null);
        return row;
    }

    private void OnSlotPressed(TraditionSlotType slot, int index)
    {
        if (_pendingPick is not { } pick)
        {
            return;
        }

        SlotRequested?.Invoke(slot, index, pick);
        ClearPending();
    }

    private void SyncDrawer(IReadOnlyList<TraditionCardData> drawer)
    {
        var wanted = new HashSet<TraditionId>();
        foreach (TraditionCardData card in drawer)
        {
            wanted.Add(card.Id);
        }

        var gone = new List<TraditionId>();
        foreach (TraditionId id in _cardViews.Keys)
        {
            if (!wanted.Contains(id))
            {
                gone.Add(id);
            }
        }

        foreach (TraditionId id in gone)
        {
            _cardViews[id].QueueFree();
            _cardViews.Remove(id);
        }

        foreach (TraditionCardData card in drawer)
        {
            if (!_cardViews.TryGetValue(card.Id, out TraditionCardView? view))
            {
                view = new TraditionCardView { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                view.CardChosen += OnCardChosen;
                _cardViews[card.Id] = view;
                _drawer!.AddChild(view);
            }

            view.SetCard(card);
            view.SetPicked(_pendingPick == card.Id);
        }

        _drawer!.GetNodeOrNull<Label>("EmptyNote")?.QueueFree();
        if (drawer.Count == 0)
        {
            UiStrings strings = _strings ?? throw new InvalidOperationException("SetData has not run yet.");
            var note = new Label
            {
                Name = "EmptyNote",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Text = strings.Get("ui.traditions.drawer.empty"),
            };
            note.AddThemeFontSizeOverride("font_size", 15);
            _drawer.AddChild(note);
        }
    }

    private void OnCardChosen(TraditionCardView view)
    {
        // Slotted cards are shown dimmed with their slot, but they are not offered again: picking them up
        // would silently unslot them. To move a card, clear its slot first, then pick it from the drawer.
        if (view.Card is null || view.Card.SlottedElsewhere)
        {
            return;
        }

        _pendingPick = view.Card.Id;
        foreach (TraditionCardView other in _cardViews.Values)
        {
            other.SetPicked(other == view);
        }

        SyncPending(_strings);
    }

    private void SyncPending(UiStrings? strings)
    {
        if (_pending is null || strings is null)
        {
            return;
        }

        if (_pendingPick is { } pick
            && _cardViews.TryGetValue(pick, out TraditionCardView? view)
            && view.Card is not null)
        {
            _pending.Visible = true;
            _pending.Text = strings.Format("ui.traditions.pending", ("card", view.Card.Name));
        }
        else
        {
            _pending.Visible = false;
            _pendingPick = null;
        }
    }

    private void SyncStyleAndSummary(UiStrings strings, string? styleLine, IReadOnlyList<string> summary)
    {
        _style!.Visible = styleLine is not null;
        _style.Text = styleLine ?? string.Empty;

        _summary!.Visible = summary.Count > 0;
        _summary.Text = summary.Count > 0 ? string.Join("\n", summary) : string.Empty;
    }

    private sealed class SlotRow
    {
        public SlotRow(HBoxContainer root, SlotDropButton place, Button clear, string group)
        {
            Root = root;
            Place = place;
            Clear = clear;
            Group = group;
        }

        public HBoxContainer Root { get; }
        public SlotDropButton Place { get; }
        public Button Clear { get; }
        public string Group { get; set; }
    }

    private sealed partial class SlotDropButton : Button
    {
        public SlotDropButton(TraditionsBoard board, TraditionSlotType slot, int index)
        {
            _board = board;
            Slot = slot;
            Index = index;
        }

        private readonly TraditionsBoard _board;
        public TraditionSlotType Slot { get; }
        public int Index { get; }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.String)
            {
                return false;
            }

            return _board._dragIds?.ContainsKey(data.AsString()) == true;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            if (_board._dragIds?.TryGetValue(data.AsString(), out TraditionId tradition) == true)
            {
                _board.SlotRequested?.Invoke(Slot, Index, tradition);
                _board.ClearPending();
            }
        }
    }
}
