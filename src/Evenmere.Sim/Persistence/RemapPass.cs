using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using System.Collections.Immutable;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Needs;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.Systems.Workforce;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// Moves a deserialised world state from the save's content IDs to the current content's, in place. Typed ID members
/// are mapped member by member (deserialisation keeps the save's raw values, so every use can still name its old item
/// for the report); arrays indexed by a content ID are permuted; raw-number members (the road layer, packed retry keys,
/// encoded conditions, map terrain) are rewritten. Anything that names an item the content no longer has is dropped
/// with a report, except buildings, which become ruins with their held buffers refunded to the pool.
/// </summary>
internal static class RemapPass
{
    /// <summary>Rewrites a world state to the current content's IDs.</summary>
    /// <param name="world">The state, deserialised with the save's IDs still in it.</param>
    /// <param name="tables">The old-to-new ID maps.</param>
    /// <param name="report">Collects every remap, ruin and drop.</param>
    public static void Apply(WorldState world, SaveTables tables, SaveReport report)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(report);

        var run = new Run(world, tables, report);
        run.CheckMembers();
        run.RemapStorage();
        run.RemapResearch();
        run.RemapCulture();
        run.RemapProgression();
        run.RemapModifiers();
        run.RemapBuildings();
        run.RemapLogistics();
        run.RemapPopulation();
        run.RemapNeeds();
        run.RemapProduction();
        run.RemapRoads();
        run.RemapMap();
        run.RemapAdvisors();
        run.RemapStatistics();
        run.RemapEra();
        run.Ledger.Emit(report);
        run.EmitRemapSummaries();
    }

    private sealed class Run(WorldState world, SaveTables tables, SaveReport report)
    {
        public DropLedger Ledger { get; } = new();

        public void CheckMembers()
        {
            // A damaged file can leave any reference member nil; fail here with data errors, not null references.
            object?[] members =
            [
                world.Map, world.Roads, world.Storage, world.Modifiers, world.Construction, world.Workforce,
                world.Production, world.Logistics, world.Needs, world.Population, world.Research, world.Culture,
                world.Progression, world.Advisors, world.Statistics,
            ];
            if (Array.IndexOf(members, null) >= 0)
            {
                throw new InvalidDataException("The world state is missing a required member.");
            }

            world.Needs.Shares ??= new FairShares();
            world.Production.Totals ??= new ProducedTotals();
        }

        public void RemapStorage()
        {
            StorageState storage = world.Storage;
            for (int old = 0; old < tables.GoodMap.Length; old++)
            {
                if (tables.GoodMap[old] < 0)
                {
                    Amount held = old < storage.Stock.Length ? storage.Stock[old] : Amount.Zero;
                    Ledger.Add("good", tables.OldGoodName(old), held > Amount.Zero ? $"stock {held} discarded" : "stock empty");
                }
            }

            storage.Stock = Permute(storage.Stock, tables.GoodMap, tables.Content.Goods.Count, static () => Amount.Zero);
        }

        public void RemapResearch()
        {
            ResearchState state = world.Research;
            for (int old = 0; old < tables.TechMap.Length; old++)
            {
                if (tables.TechMap[old] < 0)
                {
                    Amount held = old < state.Progress.Length ? state.Progress[old] : Amount.Zero;
                    Ledger.Add("tech", tables.OldTechName(old), held > Amount.Zero ? $"progress {held} discarded" : "no progress held");
                }
            }

            state.Progress = Permute(state.Progress, tables.TechMap, tables.Content.Techs.Count, static () => Amount.Zero);
            state.Done = PermuteBits(state.Done, tables.TechMap, tables.Content.Techs.Count);
            state.Inspired = PermuteBits(state.Inspired, tables.TechMap, tables.Content.Techs.Count);

            if (state.Target is { } target)
            {
                int mapped = tables.MapTech(target.Value);
                if (mapped < 0)
                {
                    Ledger.Add("tech", tables.OldTechName(target.Value), "research target cleared");
                    state.Target = null;
                }
                else
                {
                    state.Target = TechId.From(checked((ushort)mapped));
                }
            }

            if (state.Queue.Length > 0)
            {
                var kept = new List<TechId>(state.Queue.Length);
                foreach (TechId queued in state.Queue)
                {
                    int mapped = tables.MapTech(queued.Value);
                    if (mapped < 0)
                    {
                        Ledger.Add("tech", tables.OldTechName(queued.Value), "queue entry removed");
                    }
                    else
                    {
                        kept.Add(TechId.From(checked((ushort)mapped)));
                    }
                }

                state.Queue = [.. kept];
            }
        }

        public void RemapCulture()
        {
            CultureState state = world.Culture;
            for (int old = 0; old < tables.CivicMap.Length; old++)
            {
                if (tables.CivicMap[old] < 0)
                {
                    Amount held = old < state.Progress.Length ? state.Progress[old] : Amount.Zero;
                    Ledger.Add("civic", tables.OldCivicName(old), held > Amount.Zero ? $"progress {held} discarded" : "no progress held");
                }
            }

            state.Progress = Permute(state.Progress, tables.CivicMap, tables.Content.Civics.Count, static () => Amount.Zero);
            state.Done = PermuteBits(state.Done, tables.CivicMap, tables.Content.Civics.Count);
            state.Inspired = PermuteBits(state.Inspired, tables.CivicMap, tables.Content.Civics.Count);

            if (state.Target is { } target)
            {
                int mapped = tables.MapCivic(target.Value);
                if (mapped < 0)
                {
                    Ledger.Add("civic", tables.OldCivicName(target.Value), "civic target cleared");
                    state.Target = null;
                }
                else
                {
                    state.Target = CivicId.From(checked((ushort)mapped));
                }
            }

            if (state.Queue.Length > 0)
            {
                var kept = new List<CivicId>(state.Queue.Length);
                foreach (CivicId queued in state.Queue)
                {
                    int mapped = tables.MapCivic(queued.Value);
                    if (mapped < 0)
                    {
                        Ledger.Add("civic", tables.OldCivicName(queued.Value), "queue entry removed");
                    }
                    else
                    {
                        kept.Add(CivicId.From(checked((ushort)mapped)));
                    }
                }

                state.Queue = [.. kept];
            }
        }

        public void RemapProgression()
        {
            ProgressionState state = world.Progression;

            for (int old = 0; old < tables.GoodMap.Length; old++)
            {
                if (tables.GoodMap[old] < 0)
                {
                    long made = old < state.Produced.Length ? state.Produced[old] : 0;
                    long ate = old < state.Consumed.Length ? state.Consumed[old] : 0;
                    Ledger.Add("good", tables.OldGoodName(old), $"counters discarded (produced {made}, consumed {ate})");
                }
            }

            state.Produced = Permute(state.Produced, tables.GoodMap, tables.Content.Goods.Count, static () => 0L);
            state.Consumed = Permute(state.Consumed, tables.GoodMap, tables.Content.Goods.Count, static () => 0L);

            for (int old = 0; old < tables.BuildingMap.Length; old++)
            {
                if (tables.BuildingMap[old] < 0)
                {
                    long count = old < state.Built.Length ? state.Built[old] : 0;
                    Ledger.Add("building", tables.OldBuildingName(old), $"{count} completion(s) counted");
                }
            }

            state.Built = Permute(state.Built, tables.BuildingMap, tables.Content.Buildings.Count, static () => 0L);
            state.Buildings = PermuteBits(state.Buildings, tables.BuildingMap, tables.Content.Buildings.Count);
            state.Recipes = PermuteBits(state.Recipes, tables.RecipeMap, tables.Content.Recipes.Count);
            state.Tiers = PermuteBits(state.Tiers, tables.TierMap, tables.Content.Tiers.Count);
            state.Traditions = PermuteBits(state.Traditions, tables.TraditionMap, tables.Content.Traditions.Count);
            state.Festivals = PermuteBits(state.Festivals, tables.FestivalMap, tables.Content.Festivals.Count);
            state.GreatWorks = PermuteBits(state.GreatWorks, tables.GreatWorkMap, tables.Content.GreatWorks.Count);

            var kept = new List<EffectSource>(state.AppliedSources.Length);
            foreach (EffectSource source in state.AppliedSources)
            {
                if (RemapSource(source) is { } mapped)
                {
                    kept.Add(mapped);
                }
                else
                {
                    AddSourceDrop(source, "applied effects dropped");
                }
            }

            // Applied sources stay sorted and distinct however they arrive, so the bytes never depend on load order.
            kept.Sort(ProgressionState.Compare);
            state.AppliedSources = [.. kept];

            ApplyStartUnlocksForNew(state);
        }

        // Items the save never knew are unlocked exactly as a new game on the current content unlocks them: anything
        // flagged start is on. Without this, a fresh game and a loaded save would disagree about an added recipe.
        private void ApplyStartUnlocksForNew(ProgressionState state)
        {
            SetNewStartBits(state.Buildings, tables.BuildingMap, tables.Content.Buildings.Count, i => tables.Content.Buildings.Items[i].Start);
            SetNewStartBits(state.Recipes, tables.RecipeMap, tables.Content.Recipes.Count, i => tables.Content.Recipes.Items[i].Start);
            SetNewStartBits(state.Tiers, tables.TierMap, tables.Content.Tiers.Count, i => tables.Content.Tiers.Items[i].Start);
            SetNewStartBits(state.Traditions, tables.TraditionMap, tables.Content.Traditions.Count, i => tables.Content.Traditions.Items[i].Start);
            SetNewStartBits(state.Festivals, tables.FestivalMap, tables.Content.Festivals.Count, i => tables.Content.Festivals.Items[i].Start);
        }

        private static void SetNewStartBits(ulong[] bits, int[] map, int newCount, Func<int, bool> starts)
        {
            int[] inverse = Invert(map, newCount);
            for (int now = 0; now < newCount; now++)
            {
                if (inverse[now] < 0 && starts(now))
                {
                    Bits.Set(bits, now);
                }
            }
        }

        public void RemapModifiers()
        {
            ModifiersState state = world.Modifiers;
            var kept = new List<ActiveModifier>(state.Active.Length);
            foreach (ActiveModifier modifier in state.Active)
            {
                if (RemapModifier(modifier) is { } mapped)
                {
                    kept.Add(mapped);
                }
            }

            state.Active = [.. kept];
        }

        public void RemapBuildings()
        {
            MapState map = world.Map;
            ConstructionState construction = world.Construction;
            ProductionState production = world.Production;
            PopulationState population = world.Population;
            WorkforceState workforce = world.Workforce;
            StorageState storage = world.Storage;
            BuildingTypeId standIn = tables.Content.Required.FoundersHall;

            foreach (IdTable<BuildingRecord>.Entry entry in map.Buildings)
            {
                int mapped = tables.MapBuilding(entry.Value.Type.Value);
                if (mapped >= 0)
                {
                    entry.Value.Type = BuildingTypeId.From(checked((ushort)mapped));
                    continue;
                }

                string oldName = tables.OldBuildingName(entry.Value.Type.Value);
                var notes = new List<string>();

                if (construction.Sites.TryGet(entry.Id, out SiteRecord site))
                {
                    construction.Sites.Remove(entry.Id);
                    notes.Add($"site dropped after {site.StepsDrawn} step(s) drawn");
                }

                string refund = RefundRuinBuffers(production, storage, entry.Id);
                if (population.Houses.TryGet(entry.Id, out HouseRecord house))
                {
                    population.Houses.Remove(entry.Id);
                    notes.Add($"house with {house.Residents} resident(s) removed");
                }

                workforce.Workplaces.Remove(entry.Id);

                entry.Value = new BuildingRecord
                {
                    Type = standIn,
                    Origin = entry.Value.Origin,
                    Rotation = entry.Value.Rotation,
                    Width = entry.Value.Width,
                    Height = entry.Value.Height,
                    State = BuildingState.Ruin,
                    Stage = 0,
                };

                string ruin = $"ruin: building {entry.Id.Value} ('{oldName}') became a ruin; refunded {refund} to stock";
                report.Ruins.Add(notes.Count == 0 ? ruin : ruin + "; " + string.Join("; ", notes));
            }

            // Upgrade sites whose target is gone are cancelled; the finished building underneath keeps working.
            var cancelled = new List<EntityId>();
            foreach (IdTable<SiteRecord>.Entry entry in construction.Sites)
            {
                int mapped = tables.MapBuilding(entry.Value.Target.Value);
                if (mapped >= 0)
                {
                    entry.Value.Target = BuildingTypeId.From(checked((ushort)mapped));
                    int waiting = tables.MapGood(entry.Value.WaitingFor.Value);
                    if (waiting < 0)
                    {
                        Ledger.Add("good", tables.OldGoodName(entry.Value.WaitingFor.Value), "a site's missing material is gone");
                        entry.Value.WaitingFor = default;
                    }
                    else
                    {
                        entry.Value.WaitingFor = GoodId.From(checked((ushort)waiting));
                    }
                }
                else
                {
                    cancelled.Add(entry.Id);
                }
            }

            foreach (EntityId id in cancelled)
            {
                construction.Sites.Remove(id);
                report.Dropped.Add($"dropped site on building {id.Value}: its target is gone from the content");
            }
        }

        public void RemapLogistics()
        {
            LogisticsState state = world.Logistics;

            // Retry keys pack (building, good, direction); the day parallel to a dropped key goes with it.
            var retries = new List<(long Key, long Day)>(state.RetryKeys.Length);
            for (int i = 0; i < state.RetryKeys.Length; i++)
            {
                long key = state.RetryKeys[i];
                int good = (int)((key & 0xFFFFFFFFL) >> 1);
                int mapped = tables.MapGood(good);
                if (mapped < 0)
                {
                    Ledger.Add("good", tables.OldGoodName(good), "a postponed delivery is gone");
                    continue;
                }

                long rebuilt = (key & ~0xFFFFFFFFL) | (((long)(uint)mapped << 1) | (key & 1L));
                retries.Add((rebuilt, state.RetryDays[i]));
            }

            retries.Sort(static (a, b) => a.Key.CompareTo(b.Key));
            state.RetryKeys = new long[retries.Count];
            state.RetryDays = new long[retries.Count];
            for (int i = 0; i < retries.Count; i++)
            {
                state.RetryKeys[i] = retries[i].Key;
                state.RetryDays[i] = retries[i].Day;
            }

            var droppedJobs = new List<EntityId>();
            foreach (IdTable<TransportJobRecord>.Entry entry in state.Jobs)
            {
                int mapped = tables.MapGood(entry.Value.Good.Value);
                bool ruined = world.Map.Buildings.TryGet(entry.Value.Building, out BuildingRecord endpoint)
                    && endpoint.State == BuildingState.Ruin;
                if (mapped < 0 || ruined)
                {
                    if (mapped >= 0 && entry.Value.Carried > Amount.Zero)
                    {
                        Credit(world.Storage, GoodId.From(checked((ushort)mapped)), entry.Value.Carried);
                    }

                    droppedJobs.Add(entry.Id);
                }
                else
                {
                    entry.Value.Good = GoodId.From(checked((ushort)mapped));
                }
            }

            if (droppedJobs.Count > 0)
            {
                foreach (EntityId id in droppedJobs)
                {
                    state.Jobs.Remove(id);
                }

                report.Dropped.Add($"dropped {droppedJobs.Count} transport job(s) whose good or endpoint is gone; carried goods were refunded to stock");
            }
        }

        public void RemapPopulation()
        {
            PopulationState state = world.Population;
            state.SuppliedWorkers = Permute(state.SuppliedWorkers, tables.TierMap, tables.Content.Tiers.Count, static () => 0);

            var dropped = new List<EntityId>();
            foreach (IdTable<HouseRecord>.Entry entry in state.Houses)
            {
                int tier = tables.MapTier(entry.Value.Tier.Value);
                int type = tables.MapBuilding(entry.Value.Type.Value);
                if (tier < 0 || type < 0)
                {
                    dropped.Add(entry.Id);
                    continue;
                }

                entry.Value.Tier = TierId.From(checked((ushort)tier));
                entry.Value.Type = BuildingTypeId.From(checked((ushort)type));
            }

            foreach (EntityId id in dropped)
            {
                state.Houses.Remove(id);
                report.Dropped.Add($"dropped house {id.Value}: its tier or type is gone from the content");
            }

            world.Workforce.RawSupply = Permute(world.Workforce.RawSupply, tables.TierMap, tables.Content.Tiers.Count, static () => 0);
        }

        public void RemapNeeds()
        {
            FairShares shares = world.Needs.Shares;
            shares.Demand = Permute(shares.Demand, tables.GoodMap, tables.Content.Goods.Count, static () => Amount.Zero);
            shares.Fulfilment = Permute(shares.Fulfilment, tables.GoodMap, tables.Content.Goods.Count, static () => Ratio.Zero);
        }

        public void RemapProduction()
        {
            ProductionState state = world.Production;
            state.Totals.Today = Permute(state.Totals.Today, tables.GoodMap, tables.Content.Goods.Count, static () => Amount.Zero);
            state.Totals.Yesterday = Permute(state.Totals.Yesterday, tables.GoodMap, tables.Content.Goods.Count, static () => Amount.Zero);

            var dropped = new List<EntityId>();
            foreach (IdTable<ProducerRecord>.Entry entry in state.Producers)
            {
                int recipe = tables.MapRecipe(entry.Value.Recipe.Value);
                if (recipe < 0)
                {
                    dropped.Add(entry.Id);
                    continue;
                }

                var recipeId = RecipeId.From(checked((ushort)recipe));
                RealignBuffers(entry.Value, recipeId);
                entry.Value.Recipe = recipeId;
            }

            foreach (EntityId id in dropped)
            {
                if (state.Producers.TryGet(id, out ProducerRecord? record) && record is not null)
                {
                    Amount buffered = Amount.Zero;
                    foreach (Amount held in record.Inputs)
                    {
                        buffered += held;
                    }

                    foreach (Amount held in record.Outputs)
                    {
                        buffered += held;
                    }

                    state.Producers.Remove(id);
                    report.Dropped.Add($"dropped producer {id.Value}: its recipe is gone from the content; {buffered} buffered goods discarded");
                }
            }
        }

        public void RemapRoads()
        {
            RoadsState state = world.Roads;
            if (state.Layer.Length == 0)
            {
                return;
            }

            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < state.Layer.Length; i++)
            {
                if (state.Layer[i] == 0)
                {
                    continue;
                }

                int mapped = tables.MapBuilding(state.Layer[i] - 1);
                if (mapped < 0)
                {
                    string name = tables.OldBuildingName(state.Layer[i] - 1);
                    counts[name] = counts.GetValueOrDefault(name) + 1;
                    state.Layer[i] = 0;
                }
                else
                {
                    state.Layer[i] = checked((ushort)(mapped + 1));
                }
            }

            foreach ((string name, int count) in counts)
            {
                report.Dropped.Add($"dropped {count} road tile(s) of unknown road type '{name}'");
            }
        }

        public void RemapMap()
        {
            MapState map = world.Map;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < map.Terrain.Length; i++)
            {
                int mapped = tables.MapTerrain(map.Terrain[i]);
                if (mapped < 0)
                {
                    string name = tables.OldTerrainName(map.Terrain[i]);
                    counts[name] = counts.GetValueOrDefault(name) + 1;
                    map.Terrain[i] = 0;
                }
                else
                {
                    map.Terrain[i] = checked((byte)mapped);
                }
            }

            foreach ((string name, int count) in counts)
            {
                report.Dropped.Add($"remapped {count} tile(s) of unknown terrain '{name}' to '{tables.Content.NameOf(TerrainId.From(0))}'");
            }
        }

        public void RemapAdvisors()
        {
            AdvisorsState state = world.Advisors;
            state.GoodStreak = Permute(state.GoodStreak, tables.GoodMap, tables.Content.Goods.Count, static () => 0);
            state.TierStreak = Permute(state.TierStreak, tables.TierMap, tables.Content.Tiers.Count, static () => 0);

            int droppedRecords = 0;
            var records = new List<AdviceRecord>(state.Records.Length);
            foreach (AdviceRecord record in state.Records)
            {
                if (RemapSubject(record.Subject) is { } recordSubject)
                {
                    AdviceRecord updated = record;
                    updated.Subject = recordSubject;
                    records.Add(updated);
                }
                else
                {
                    droppedRecords++;
                }
            }

            records.Sort(CompareAdvice);
            state.Records = [.. records];

            int droppedSuppressions = 0;
            var suppressions = new List<Suppression>(state.Suppressions.Length);
            foreach (Suppression suppression in state.Suppressions)
            {
                if (RemapSubject(suppression.Subject) is { } suppressionSubject)
                {
                    Suppression updated = suppression;
                    updated.Subject = suppressionSubject;
                    suppressions.Add(updated);
                }
                else
                {
                    droppedSuppressions++;
                }
            }

            suppressions.Sort(CompareSuppression);
            state.Suppressions = [.. suppressions];

            if (droppedRecords > 0 || droppedSuppressions > 0)
            {
                report.Dropped.Add($"dropped {droppedRecords} advice record(s) and {droppedSuppressions} suppression(s) about gone items");
            }
        }

        public void RemapStatistics()
        {
            StatisticsState state = world.Statistics;
            int oldGoods = tables.OldGoods.Length;
            int oldTiers = tables.OldTiers.Length;
            int newGoods = tables.Content.Goods.Count;
            int newTiers = tables.Content.Tiers.Count;

            // Series order is four blocks per good (stock, produced, consumed, demand), one per tier, then the singles.
            int oldSeries = (oldGoods * 4) + oldTiers + 3;
            int newSeries = (newGoods * 4) + newTiers + 3;
            int[] inverseGoods = Invert(tables.GoodMap, newGoods);
            int[] inverseTiers = Invert(tables.TierMap, newTiers);

            long[] daily = new long[newSeries * StatisticsState.DailyCapacity];
            long[] seasonal = new long[newSeries * StatisticsState.SeasonalCapacity];
            CopySeries(state.Daily, oldSeries, daily, newSeries, StatisticsState.DailyCapacity, oldGoods, oldTiers, inverseGoods, inverseTiers);
            CopySeries(state.Seasonal, oldSeries, seasonal, newSeries, StatisticsState.SeasonalCapacity, oldGoods, oldTiers, inverseGoods, inverseTiers);
            state.Daily = daily;
            state.Seasonal = seasonal;
            state.GoodCount = newGoods;
            state.TierCount = newTiers;

            state.LastConsumed = Permute(state.LastConsumed, tables.GoodMap, newGoods, static () => 0L);
        }

        public void RemapEra()
        {
            ProgressionState state = world.Progression;
            int mapped = tables.MapEra(state.Era);
            if (mapped < 0)
            {
                int firstEra = tables.Content.ErasByOrder.Length > 0 ? tables.Content.ErasByOrder[0].Value : 0;
                Ledger.Add("era", tables.OldEraName(state.Era), $"current era gone; reset to '{tables.Content.Eras.Names[firstEra]}'");
                state.Era = firstEra;
            }
            else
            {
                state.Era = mapped;
            }
        }

        public void EmitRemapSummaries()
        {
            EmitSummary("goods", tables.GoodMap, tables.OldGoods);
            EmitSummary("building types", tables.BuildingMap, tables.OldBuildings);
            EmitSummary("recipes", tables.RecipeMap, tables.OldRecipes);
            EmitSummary("tiers", tables.TierMap, tables.OldTiers);
            EmitSummary("techs", tables.TechMap, tables.OldTechs);
            EmitSummary("civics", tables.CivicMap, tables.OldCivics);
            EmitSummary("traditions", tables.TraditionMap, tables.OldTraditions);
            EmitSummary("festivals", tables.FestivalMap, tables.OldFestivals);
            EmitSummary("great-work types", tables.GreatWorkMap, tables.OldGreatWorks);
            EmitSummary("monuments", tables.MonumentMap, tables.OldMonuments);
            EmitSummary("happenings", tables.HappeningMap, tables.OldHappenings);
            EmitSummary("eras", tables.EraMap, tables.OldEras);
            EmitSummary("terrains", tables.TerrainMap, tables.OldTerrains);
        }

        private void EmitSummary(string kind, int[] map, string[] oldNames)
        {
            var moved = new List<string>();
            for (int old = 0; old < map.Length; old++)
            {
                int now = map[old];
                if (now >= 0 && now != old)
                {
                    moved.Add($"'{oldNames[old]}' {old}->{now}");
                }
            }

            if (moved.Count > 0)
            {
                report.Remapped.Add($"remapped {kind}: {string.Join(", ", moved)}");
            }
        }

        private EffectSource? RemapSource(EffectSource source)
        {
            int mapped = source.Kind switch
            {
                EffectSourceKind.Tech => tables.MapTech(source.Value),
                EffectSourceKind.Civic => tables.MapCivic(source.Value),
                EffectSourceKind.Tradition => tables.MapTradition(source.Value),
                EffectSourceKind.Festival => tables.MapFestival(source.Value),
                EffectSourceKind.Monument => tables.MapMonument(source.Value),
                EffectSourceKind.Happening => tables.MapHappening(source.Value),
                EffectSourceKind.Building => source.Value,
                _ => -1,
            };
            return mapped < 0 ? null : new EffectSource(source.Kind, mapped);
        }

        private void AddSourceDrop(EffectSource source, string detail)
        {
            switch (source.Kind)
            {
                case EffectSourceKind.Tech:
                    Ledger.Add("tech", tables.OldTechName(source.Value), detail);
                    break;
                case EffectSourceKind.Civic:
                    Ledger.Add("civic", tables.OldCivicName(source.Value), detail);
                    break;
                case EffectSourceKind.Tradition:
                    Ledger.Add("tradition", tables.OldTraditionName(source.Value), detail);
                    break;
                case EffectSourceKind.Festival:
                    Ledger.Add("festival", tables.OldFestivalName(source.Value), detail);
                    break;
                case EffectSourceKind.Monument:
                    Ledger.Add("monument", tables.OldMonumentName(source.Value), detail);
                    break;
                case EffectSourceKind.Happening:
                    Ledger.Add("happening", tables.OldHappeningName(source.Value), detail);
                    break;
                default:
                    break;
            }
        }

        private ActiveModifier? RemapModifier(ActiveModifier modifier)
        {
            if (RemapSource(modifier.Source) is not { } sourceValue)
            {
                AddSourceDrop(modifier.Source, "a modifier was dropped");
                return null;
            }

            int target = modifier.TargetKind switch
            {
                ModifierTargetKind.Building => tables.MapBuilding(modifier.TargetValue),
                ModifierTargetKind.Tier => tables.MapTier(modifier.TargetValue),
                ModifierTargetKind.Good => tables.MapGood(modifier.TargetValue),
                _ => modifier.TargetValue,
            };
            if (target < 0)
            {
                AddSourceDrop(modifier.Source, "a modifier's target is gone");
                return null;
            }

            long[]? when = ConditionRemap.Remap(modifier.When, tables, out bool droppedWhen);
            if (droppedWhen)
            {
                AddSourceDrop(modifier.Source, "a modifier's condition names a gone item");
                return null;
            }

            return new ActiveModifier
            {
                Source = sourceValue,
                Stat = modifier.Stat,
                TargetKind = modifier.TargetKind,
                TargetValue = target,
                TargetTag = modifier.TargetTag,
                Percent = modifier.Percent,
                When = when,
            };
        }

        private SubjectLink? RemapSubject(SubjectLink subject) => subject.Kind switch
        {
            SubjectKind.Building => subject,
            SubjectKind.Good => MapLink(tables.MapGood, subject.Value, static v => SubjectLink.ForGood(GoodId.From(v))),
            SubjectKind.Tier => MapLink(tables.MapTier, subject.Value, static v => SubjectLink.ForTier(TierId.From(v))),
            SubjectKind.BuildingType => MapLink(tables.MapBuilding, subject.Value, static v => SubjectLink.ForBuildingType(BuildingTypeId.From(v))),
            _ => subject,
        };

        private static SubjectLink? MapLink(Func<int, int> map, int old, Func<ushort, SubjectLink> make)
        {
            int mapped = map(old);
            return mapped < 0 ? null : make(checked((ushort)mapped));
        }

        private string RefundRuinBuffers(ProductionState production, StorageState storage, EntityId building)
        {
            if (!production.Producers.TryGet(building, out ProducerRecord? record) || record is null)
            {
                return "nothing held";
            }

            // The recipe is mapped first: the ruin's producer still carries the save's recipe ID.
            int recipe = tables.MapRecipe(record.Recipe.Value);
            if (recipe < 0)
            {
                production.Producers.Remove(building);
                return "nothing attributable (recipe gone)";
            }

            var recipeId = RecipeId.From(checked((ushort)recipe));
            RecipeDef def = tables.Content.Get(recipeId);
            var parts = new List<string>();
            for (int i = 0; i < record.Inputs.Length && i < def.Inputs.Length; i++)
            {
                if (record.Inputs[i] > Amount.Zero)
                {
                    Credit(storage, def.Inputs[i].Good, record.Inputs[i]);
                    parts.Add($"{record.Inputs[i]} {tables.Content.NameOf(def.Inputs[i].Good)}");
                }
            }

            for (int i = 0; i < record.Outputs.Length && i < def.Outputs.Length; i++)
            {
                if (record.Outputs[i] > Amount.Zero)
                {
                    Credit(storage, def.Outputs[i].Good, record.Outputs[i]);
                    parts.Add($"{record.Outputs[i]} {tables.Content.NameOf(def.Outputs[i].Good)}");
                }
            }

            production.Producers.Remove(building);
            return parts.Count == 0 ? "nothing held" : string.Join(", ", parts);
        }

        private void RealignBuffers(ProducerRecord record, RecipeId recipe)
        {
            RecipeDef realignDef = tables.Content.Get(recipe);
            int[] inverse = Invert(tables.GoodMap, tables.Content.Goods.Count);

            (Amount[] Buffers, long[] Ticks) Realign(
                Amount[] oldBuffers, long[] oldTicks, ImmutableArray<GoodAmount> lines)
            {
                // The recipe's goods are sorted by good ID in both contents, and surviving goods keep their
                // relative order, so the k-th surviving new input held the k-th old buffer. New-only goods start
                // empty; the request ticks travel with their buffer.
                var surviving = new List<int>(lines.Length);
                foreach (GoodAmount line in lines)
                {
                    int oldId = (uint)line.Good.Value < (uint)inverse.Length ? inverse[line.Good.Value] : -1;
                    if (oldId >= 0)
                    {
                        surviving.Add(oldId);
                    }
                }

                surviving.Sort();
                var buffers = new Amount[lines.Length];
                long[] ticks = new long[lines.Length];
                Array.Fill(ticks, ProducerRecord.NoRequest);
                for (int n = 0; n < lines.Length; n++)
                {
                    int oldId = (uint)lines[n].Good.Value < (uint)inverse.Length ? inverse[lines[n].Good.Value] : -1;
                    if (oldId < 0)
                    {
                        continue;
                    }

                    int rank = surviving.IndexOf(oldId);
                    if (rank >= 0 && rank < oldBuffers.Length)
                    {
                        buffers[n] = oldBuffers[rank];
                    }

                    if (rank >= 0 && rank < oldTicks.Length)
                    {
                        ticks[n] = oldTicks[rank];
                    }
                }

                return (buffers, ticks);
            }

            (Amount[] Buffers, long[] Ticks) inputs = Realign(record.Inputs, record.InputRequestTicks, realignDef.Inputs);
            (Amount[] Buffers, long[] Ticks) outputs = Realign(record.Outputs, record.OutputRequestTicks, realignDef.Outputs);
            record.Inputs = inputs.Buffers;
            record.InputRequestTicks = inputs.Ticks;
            record.Outputs = outputs.Buffers;
            record.OutputRequestTicks = outputs.Ticks;
        }

        private static int CompareAdvice(AdviceRecord left, AdviceRecord right)
        {
            int byKind = left.Kind.CompareTo(right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int bySubjectKind = left.Subject.Kind.CompareTo(right.Subject.Kind);
            return bySubjectKind != 0 ? bySubjectKind : left.Subject.Value.CompareTo(right.Subject.Value);
        }

        private static int CompareSuppression(Suppression left, Suppression right)
        {
            int byKind = left.Kind.CompareTo(right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int bySubjectKind = left.Subject.Kind.CompareTo(right.Subject.Kind);
            return bySubjectKind != 0 ? bySubjectKind : left.Subject.Value.CompareTo(right.Subject.Value);
        }
    }

    private sealed class DropLedger
    {
        private readonly SortedDictionary<(string Kind, string Name), List<string>> _notes = new();

        public void Add(string kind, string name, string detail)
        {
            if (!_notes.TryGetValue((kind, name), out List<string>? details))
            {
                _notes[(kind, name)] = details = [];
            }

            details.Add(detail);
        }

        public void Emit(SaveReport report)
        {
            foreach (((string kind, string name), List<string> details) in _notes)
            {
                report.Dropped.Add($"dropped {kind} '{name}': {string.Join("; ", details)}");
            }
        }
    }

    private static void Credit(StorageState storage, GoodId good, Amount amount)
    {
        storage.Stock[good.Value] = storage.Stock[good.Value] + amount;
    }

    private static T[] Permute<T>(T[]? values, int[] map, int newCount, Func<T> zero)
    {
        var result = new T[newCount];
        for (int i = 0; i < newCount; i++)
        {
            result[i] = zero();
        }

        if (values is null)
        {
            return result;
        }

        int length = Math.Min(values.Length, map.Length);
        for (int old = 0; old < length; old++)
        {
            int now = map[old];
            if (now >= 0)
            {
                result[now] = values[old];
            }
        }

        return result;
    }

    private static ulong[] PermuteBits(ulong[]? words, int[] map, int newCount)
    {
        ulong[] result = new ulong[(newCount + 63) >> 6];
        if (words is null)
        {
            return result;
        }

        for (int old = 0; old < map.Length; old++)
        {
            int now = map[old];
            if (now < 0)
            {
                continue;
            }

            int word = old >> 6;
            if ((uint)word < (uint)words.Length && (words[word] & (1UL << (old & 63))) != 0)
            {
                result[now >> 6] |= 1UL << (now & 63);
            }
        }

        return result;
    }

    private static int[] Invert(int[] map, int newCount)
    {
        int[] inverse = new int[newCount];
        Array.Fill(inverse, -1);
        for (int old = 0; old < map.Length; old++)
        {
            if (map[old] >= 0)
            {
                inverse[map[old]] = old;
            }
        }

        return inverse;
    }

    private static void CopySeries(
        long[] oldSamples, int oldSeries, long[] newSamples, int newSeries, int capacity,
        int oldGoods, int oldTiers, int[] inverseGoods, int[] inverseTiers)
    {
        for (int s = 0; s < newSeries; s++)
        {
            int oldIndex = NewSeriesToOld(s, oldGoods, oldTiers, inverseGoods, inverseTiers);
            if (oldIndex < 0 || ((oldIndex + 1) * capacity) > oldSamples.Length)
            {
                continue;
            }

            Array.Copy(oldSamples, oldIndex * capacity, newSamples, s * capacity, capacity);
        }
    }

    private static int NewSeriesToOld(int series, int oldGoods, int oldTiers, int[] inverseGoods, int[] inverseTiers)
    {
        int newGoods = inverseGoods.Length;
        int newTiers = inverseTiers.Length;

        if (series < newGoods)
        {
            return inverseGoods[series];
        }

        if (series < newGoods * 2)
        {
            int old = inverseGoods[series - newGoods];
            return old >= 0 ? oldGoods + old : -1;
        }

        if (series < newGoods * 3)
        {
            int old = inverseGoods[series - (newGoods * 2)];
            return old >= 0 ? (oldGoods * 2) + old : -1;
        }

        if (series < newGoods * 4)
        {
            int old = inverseGoods[series - (newGoods * 3)];
            return old >= 0 ? (oldGoods * 3) + old : -1;
        }

        if (series < (newGoods * 4) + newTiers)
        {
            int old = inverseTiers[series - (newGoods * 4)];
            return old >= 0 ? (oldGoods * 4) + old : -1;
        }

        int singles = series - ((newGoods * 4) + newTiers);
        return singles is >= 0 and < 3 ? ((oldGoods * 4) + oldTiers) + singles : -1;
    }
}
