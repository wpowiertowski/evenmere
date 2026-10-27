using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Tools.Balance.EraOne;

/// Culture behaviours: expeditions, tradition slotting, festivals and happenings.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

        private void RunExpeditions()
        {
            _expeditionSent.Clear();
            _expeditionRegions.Clear();
            while (true)
            {
                (EntityId Lodge, ushort Region)? next = FindUnsentExpedition();
                if (next is null)
                {
                    return;
                }

                Send(new StartExpedition(next.Value.Lodge, next.Value.Region));
                _expeditionSent.Add(next.Value.Lodge);
                _expeditionRegions.Add(next.Value.Region);
                _notes.Add($"expedition: lodge #{next.Value.Lodge.Value} sails to region {next.Value.Region}.");
            }
        }

        private (EntityId Lodge, ushort Region)? FindUnsentExpedition()
        {
            (EntityId Lodge, ushort Region)? found = ExpeditionPlanner.FindExpedition(_content, _simulation);
            if (found is null)
            {
                return null;
            }

            // The planner does not know what this tick already sent (commands apply on the next tick), so sent
            // lodges and targeted regions are skipped here; anything else means the planner has nothing new.
            if (_expeditionSent.Contains(found.Value.Lodge) || _expeditionRegions.Contains(found.Value.Region))
            {
                return null;
            }

            return found;
        }

        // Era I: re-slot traditions toward the heuristic whenever the unlocked set or the granted slots change.
        // Slotting is free, so the only throttle is a cooldown after a rejection (a lock race with the queue).
        private void RunTraditions()
        {
            List<SetTradition> planned;
            try
            {
                planned = TraditionHeuristics.PlanSlottings(_content, _simulation.Query.Culture);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or IndexOutOfRangeException)
            {
                return;
            }

            foreach (SetTradition command in planned)
            {
                string key = $"tradition:{command.Slot}:{command.Index}";
                if (OnCooldown(key))
                {
                    continue;
                }

                Send(command);
                _cooldowns.Add((key, _simulation.CurrentTick + TraditionCooldownTicks));
                _notes.Add(command.Tradition is null
                    ? $"tradition: cleared {command.Slot} slot {command.Index}."
                    : $"tradition: slotted {_content.Traditions.Names[command.Tradition.Value.Value]} in {command.Slot} slot {command.Index}.");
            }
        }

        // Era I: hold the Harvest Fair (or any unlocked festival) when the island can pay. One festival runs at a
        // time and each type once a year; the first affordable festival in content order wins, so the choice is
        // deterministic.
        private void RunFestivals()
        {
            if (_simulation.Query.Culture.IsFestivalRunning())
            {
                return;
            }

            EntityId ground = FirstFestivalGround();
            if (ground == EntityId.None)
            {
                return;
            }

            long year = new GameClock(_simulation.CurrentTick).Year;
            Season season = new GameClock(_simulation.CurrentTick).Season;
            foreach (FestivalDef def in _content.Festivals.Items)
            {
                if (!_simulation.Query.Progression.IsFestivalUnlocked(def.Id))
                {
                    continue;
                }

                if (_simulation.Query.Culture.FestivalsHeldInYear(def.Id, (int)year) > 0)
                {
                    continue;
                }

                if (def.Seasons.Length > 0 && !def.Seasons.Contains(season))
                {
                    continue;
                }

                if (!CanAfford(def.Cost))
                {
                    continue;
                }

                Send(new HoldFestival(ground, def.Id));
                _notes.Add($"festival: holding {_content.Festivals.Names[def.Id.Value]} at ground #{ground.Value}.");
                return;
            }
        }

        private EntityId FirstFestivalGround()
        {
            EntityId best = EntityId.None;
            foreach (BuildingInfo building in _simulation.Query.Map.Buildings)
            {
                if (!building.IsComplete)
                {
                    continue;
                }

                bool isGround = false;
                foreach (string tag in _content.Get(building.Type).Tags)
                {
                    if (tag == "festival_ground")
                    {
                        isGround = true;
                        break;
                    }
                }

                if (!isGround)
                {
                    continue;
                }

                if (best == EntityId.None || building.Id < best)
                {
                    best = building.Id;
                }
            }

            return best;
        }

        // Era I: answer a pending happening on day 5, with its first option. Every option is gentle by lint
        // (EVL012), so the first one is always safe; the choice is deterministic.
        private void RunHappenings()
        {
            PendingHappening? pending = _simulation.Query.Happenings.PendingHappening();
            if (pending is null)
            {
                return;
            }

            if (_simulation.CurrentTick - pending.Value.RaisedTick < 5 * GameTime.TicksPerDay)
            {
                return;
            }

            string key = $"happening:{_content.Happenings.Names[pending.Value.Happening.Value]}";
            if (OnCooldown(key))
            {
                return;
            }

            Send(new ChooseHappeningOption(pending.Value.Happening, 0));
            _cooldowns.Add((key, _simulation.CurrentTick + ShortageCooldownTicks));
            _notes.Add($"happening: answered {_content.Happenings.Names[pending.Value.Happening.Value]} with option 0.");
        }
    }
}
