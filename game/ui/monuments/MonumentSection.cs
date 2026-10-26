using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Monuments;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.Monuments;

/// <summary>
/// The inspector's monument section (M2-27): stage progress and the bill for each stage from the site's stage
/// members, the effects when finished, and the placeholder stage visuals that grow by stage. The inspector shows
/// it for a monument site or a finished monument and hides it for anything else.
/// </summary>
public sealed partial class MonumentSection : VBoxContainer
{
    private SimHost? _host;
    private SimMonumentData? _data;
    private MonumentSectionModel? _model;
    private UiStrings? _strings;
    private MonumentRenderer? _visuals;
    private bool _ready;
    private Label? _title;
    private Label? _stage;
    private Label? _progress;
    private Label? _stall;
    private Label? _effects;
    private VBoxContainer? _bills;
    private readonly List<Label> _billLabels = [];

    /// <summary>Sets the running host. Called once by the inspector before the first <see cref="ShowFor"/>.</summary>
    /// <param name="host">The simulation host.</param>
    public void Setup(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (ReferenceEquals(_host, host))
        {
            return;
        }

        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _host = host;
        _strings = new UiStrings(host.Locale);
        _data = new SimMonumentData(host);
        _model = new MonumentSectionModel(_data, _strings);
        _model.Refreshed += OnRefreshed;
        if (!_ready)
        {
            BuildChrome();
            _ready = true;
        }

        Retitle();
    }

    /// <summary>Shows the section for a building, refreshing at once.</summary>
    /// <param name="building">The inspected building.</param>
    public void ShowFor(EntityId building)
    {
        Visible = true;
        if (_model is not null)
        {
            _model.Target = building;
            _model.MarkDirty();
            _model.Advance(10.0);
        }
    }

    /// <summary>Hides the section and its stage visuals.</summary>
    public void HideSection()
    {
        Visible = false;
        if (_model is not null)
        {
            _model.Target = EntityId.None;
        }

        _visuals?.Clear();
    }

    /// <summary>Refreshes the section. Called by the inspector on its own refresh cadence.</summary>
    public void Refresh()
    {
        _model?.MarkDirty();
        _model?.Advance(10.0);
    }

    private void OnRefreshed() => UpdateAll();

    private void UpdateAll()
    {
        if (_model is null || _strings is null)
        {
            return;
        }

        MonumentSectionModel model = _model;
        if (_title is not null)
        {
            _title.Text = model.Title;
        }

        if (_stage is not null)
        {
            _stage.Visible = model.HasMonument;
            _stage.Text = model.StageLine;
        }

        if (_progress is not null)
        {
            _progress.Visible = !string.IsNullOrEmpty(model.ProgressLine);
            _progress.Text = model.ProgressLine;
        }

        if (_stall is not null)
        {
            _stall.Visible = !string.IsNullOrEmpty(model.StallLine);
            _stall.Text = model.StallLine;
        }

        if (_effects is not null)
        {
            _effects.Text = model.EffectsLine;
        }

        SyncBills(model);
        RefreshVisuals();
    }

    private void SyncBills(MonumentSectionModel model)
    {
        if (_bills is null)
        {
            return;
        }

        while (_billLabels.Count < model.Stages.Count)
        {
            var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _bills.AddChild(label);
            _billLabels.Add(label);
        }

        while (_billLabels.Count > model.Stages.Count)
        {
            Label last = _billLabels[^1];
            _bills.RemoveChild(last);
            last.QueueFree();
            _billLabels.RemoveAt(_billLabels.Count - 1);
        }

        for (int i = 0; i < model.Stages.Count; i++)
        {
            MonumentStageRow row = model.Stages[i];
            _billLabels[i].Text = $"{row.Headline}: {row.BillText}";
        }
    }

    private void RefreshVisuals()
    {
        if (_host is not { IsRunning: true } host || _model?.Target is not { } target || target == EntityId.None)
        {
            _visuals?.Clear();
            return;
        }

        if (_data?.ForBuilding(target) is not { } facts)
        {
            _visuals?.Clear();
            return;
        }

        if (!host.Query.Map.TryGetBuilding(target, out BuildingInfo info))
        {
            _visuals?.Clear();
            return;
        }

        _visuals ??= MakeVisuals();
        float groundY = GroundHeight(host, info);
        _visuals.ShowMonument(
            new Vector3(info.Origin.X + (info.Width / 2f), groundY, info.Origin.Y + (info.Height / 2f)),
            Math.Max(info.Width, info.Height),
            facts.StageCount,
            facts.IsComplete ? facts.StageCount : Math.Min(facts.StageIndex, facts.StageCount),
            WithinStage(facts));
    }

    private MonumentRenderer MakeVisuals()
    {
        // The visuals live in world space, so they hang off the scene root rather than this control.
        var renderer = new MonumentRenderer();
        GetTree().CurrentScene.AddChild(renderer);
        return renderer;
    }

    private static int WithinStage(MonumentFacts facts)
    {
        if (facts.IsComplete)
        {
            return 1000;
        }

        int within = (facts.ProgressPerMille * facts.StageCount) - (facts.StageIndex * 1000);
        return Math.Clamp(within, 0, 1000);
    }

    private static float GroundHeight(SimHost host, BuildingInfo info)
    {
        float top = 0f;
        for (int y = info.Origin.Y; y < info.Origin.Y + info.Height; y++)
        {
            for (int x = info.Origin.X; x < info.Origin.X + info.Width; x++)
            {
                top = Math.Max(top, TerrainSurface.GroundHeight(host.Query.Map.GetTile(x, y).Elevation));
            }
        }

        return top;
    }

    private void Retitle()
    {
        if (_strings is null || _title is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.events.monument.title");
    }

    private void BuildChrome()
    {
        AddThemeConstantOverride("separation", 4);

        var title = new Label();
        title.AddThemeFontSizeOverride("font_size", 16);
        AddChild(title);
        _title = title;

        var stage = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(stage);
        _stage = stage;

        var progress = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(progress);
        _progress = progress;

        var stall = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(stall);
        _stall = stall;

        var effects = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(effects);
        _effects = effects;

        var bills = new VBoxContainer();
        bills.AddThemeConstantOverride("separation", 2);
        AddChild(bills);
        _bills = bills;
    }

    /// <summary>Reads the monument facts from the running simulation: content for the def, queries for the site.</summary>
    private sealed class SimMonumentData(SimHost host) : IMonumentData
    {
        public MonumentFacts? ForBuilding(EntityId building)
        {
            if (!host.Query.Map.TryGetBuilding(building, out BuildingInfo info))
            {
                return null;
            }

            ContentDb content = host.Content;
            if (!content.TryGetMonumentOf(info.Type, out MonumentId monument))
            {
                return null;
            }

            MonumentDef def = content.Get(monument);
            bool isSite = host.Query.Construction.TryGetSite(building, out SiteInfo site);
            bool complete = !isSite && info.State is BuildingState.Active or BuildingState.Paused;
            if (!isSite && !complete)
            {
                return null;
            }

            var strings = new UiStrings(host.Locale);
            int stageCount = def.Stages.Length;
            int stageIndex = complete ? stageCount - 1 : Math.Clamp(site.MonumentStage, 0, stageCount - 1);
            var stages = new List<MonumentStageFacts>(stageCount);
            for (int i = 0; i < stageCount; i++)
            {
                MonumentStageState state = complete || i < stageIndex
                    ? MonumentStageState.Done
                    : i == stageIndex ? MonumentStageState.Active : MonumentStageState.Later;
                stages.Add(new MonumentStageFacts(i, state, BillOf(host, def.Stages[i])));
            }

            string? stall = !complete
                && site.Stall == StallReason.MissingMaterial
                && site.WaitingFor is { } waiting
                && (uint)waiting.Value < (uint)content.Goods.Count
                ? strings.GoodName(content.Goods.NameOf(waiting))
                : null;
            bool outOfReach = !complete && site.Stall == StallReason.NoStorehouseReach;
            return new MonumentFacts(
                building,
                BuildingName(content, strings, info.Type),
                stageIndex,
                stageCount,
                complete ? 1000 : site.Progress.PerMille,
                stall,
                outOfReach,
                EffectText(host, strings, monument),
                complete,
                stages);
        }

        private static ImmutableArray<MonumentBillLine> BillOf(SimHost host, CostSpec bill)
        {
            // The same scaling the site draws: the island's materialCost modifiers, mirrored from the sim's
            // MonumentStages.Scale (internal to the sim).
            int bonus = host.Query.Progression.BonusPercent(Stat.MaterialCost, ModifierContext.Island);
            var strings = new UiStrings(host.Locale);
            var lines = new MonumentBillLine[bill.Goods.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                GoodAmount line = bill.Goods[i];
                lines[i] = new MonumentBillLine(
                    strings.GoodName(host.Content.Goods.NameOf(line.Good)),
                    line.Amount.MultiplyBy(Ratio.FromBonusPercent(bonus)));
            }

            return [.. lines];
        }

        private static string BuildingName(ContentDb content, UiStrings strings, BuildingTypeId type)
        {
            string key = $"building.{content.NameOf(type)}.name";
            string text = strings.Get(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(type) : text;
        }

        private static string EffectText(SimHost host, UiStrings strings, MonumentId monument)
        {
            ContentDb content = host.Content;
            MonumentDef def = content.Get(monument);
            IReadOnlyList<GraphUnlock> unlocks = CivicGraphBuilder.DescribeEffects(
                content, strings, def.Effects, static _ => null);
            if (unlocks.Count == 0)
            {
                return strings.Get($"monument.{content.NameOf(monument)}.desc");
            }

            var parts = new List<string>(unlocks.Count);
            foreach (GraphUnlock unlock in unlocks)
            {
                parts.Add(unlock.Glyph.Length > 0 ? $"{unlock.Glyph}  {unlock.Text}" : unlock.Text);
            }

            return string.Join("; ", parts);
        }
    }
}
