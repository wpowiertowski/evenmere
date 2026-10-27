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

/// Tick bookkeeping: command outcomes, stalls, era detection and advice.
public static partial class EraOneBot
{
    private sealed partial class Runner
    {

        private void PostTick()
        {
            _claims.RemoveAll(claim => claim.Tick < _simulation.CurrentTick - 1);
            foreach (SimEvent raised in _simulation.DrainEvents())
            {
                if (raised is CommandRejected rejected)
                {
                    // The key carries the command and the handler's detail, so a validate-yes/apply-no pair can be
                    // told apart (same-tick overlaps, stale validations, or a real sim inconsistency).
                    string key = $"{rejected.Command.GetType().Name}({rejected.Code}): {DescribeCommand(rejected.Command)}: {rejected.Detail}";
                    _rejected[key] = _rejected.TryGetValue(key, out int count) ? count + 1 : 1;
                    foreach (BotGoalState state in _states)
                    {
                        if (state.Pending is BotPendingPlacement pending && pending.SentBuilding.Equals(rejected.Command))
                        {
                            state.Pending = null;
                            state.RetryTick = _simulation.CurrentTick + RetryDelayTicks;
                        }

                        if (state.SentTarget is not null && state.SentTarget.Equals(rejected.Command))
                        {
                            state.SentTarget = null;
                        }

                        if (state.SentQueue is not null && state.SentQueue.Equals(rejected.Command))
                        {
                            state.SentQueue = null;
                        }
                    }

                    foreach (BotPendingPlacement reactive in _reactive)
                    {
                        if (reactive.SentBuilding.Equals(rejected.Command))
                        {
                            reactive.Dead = true;
                        }
                    }
                }
            }

            int total = _simulation.Query.Population.TotalResidents;
            int residents = _simulation.Query.Population.Residents(_milestoneTierId);
            if (residents > 0 && _firstTick is null)
            {
                _firstTick = _simulation.CurrentTick;
                _residentsAtFirst = total;
            }

            if (residents >= 50 && _fiftyTick is null)
            {
                _fiftyTick = _simulation.CurrentTick;
            }

            if (residents >= 100 && _hundredTick is null)
            {
                _hundredTick = _simulation.CurrentTick;
                _residentsAtHundred = total;
            }

            if (total > _bestResidents)
            {
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }
            else if (_simulation.CurrentTick - _bestTick >= GameTime.TicksPerDay * GameTime.DaysPerSeason)
            {
                _stalls.Add(new BalanceStall(_bestTick, _simulation.CurrentTick, _bestResidents, total, TopAdvice()));
                _bestResidents = total;
                _bestTick = _simulation.CurrentTick;
            }

            if (_eraTwoTick is null && IsEraTwoBegun())
            {
                _eraTwoTick = _simulation.CurrentTick;
                _residentsAtEraTwo = total;
                _villagersAtEraTwo = _simulation.Query.Population.Residents(_milestoneTierId);
            }
        }

        private bool IsEraTwoBegun() =>
            _content.Get(_simulation.Query.Progression.CurrentEra).Order >= _content.Get(_township).Order;

        // One line per rejected command for the report: type, target and detail, all deterministic.
        private string DescribeCommand(Command command) => command switch
        {
            PlaceBuilding place => $"{_content.Buildings.Names[place.Type.Value]} at ({place.X},{place.Y})",
            PlaceRoad road => road.Path.Length == 0
                ? $"{_content.Buildings.Names[road.RoadType.Value]} empty path"
                : $"{_content.Buildings.Names[road.RoadType.Value]} {road.Path.Length} tiles from ({road.Path[0].X},{road.Path[0].Y})",
            SetResearchTarget target => $"research target {_content.Techs.Names[target.Tech.Value]}",
            SetResearchQueue queue => $"research queue {queue.Queue.Length} techs",
            SetCivicTarget target => $"civic target {_content.Civics.Names[target.Civic.Value]}",
            SetCivicQueue queue => $"civic queue {queue.Queue.Length} civics",
            SetTradition tradition => $"tradition {tradition.Slot} slot {tradition.Index}",
            StartExpedition expedition => $"expedition lodge #{expedition.Lodge.Value} to region {expedition.Region}",
            HoldFestival festival => $"festival {_content.Festivals.Names[festival.Festival.Value]} at ground #{festival.Ground.Value}",
            ChooseHappeningOption happening => $"happening {_content.Happenings.Names[happening.Happening.Value]} option {happening.OptionIndex}",
            SetHouseUpgradeLock house => $"house lock #{house.House.Value}={house.Locked}",
            Demolish demolish => $"demolish #{demolish.Building.Value}",
            _ => command.GetType().Name,
        };

        private string TopAdvice()
        {
            foreach (AdviceView advice in _simulation.Query.Advisors.Active().ToArray())
            {
                return $"{advice.Kind}:{DescribeSubject(advice.Subject)}";
            }

            return string.Empty;
        }

        private string DescribeSubject(SubjectLink subject) => subject.Kind switch
        {
            SubjectKind.Building => _simulation.Query.Map.TryGetBuilding(subject.AsBuilding(), out BuildingInfo building)
                ? $"{_content.Buildings.Names[building.Type.Value]} #{building.Id.Value}"
                : $"building #{subject.AsBuilding().Value}",
            SubjectKind.Good => _content.Goods.Names[subject.AsGood().Value],
            SubjectKind.Tier => _content.Tiers.Names[subject.AsTier().Value],
            SubjectKind.BuildingType => _content.Buildings.Names[subject.AsBuildingType().Value],
            _ => string.Empty,
        };

        // Walks the explanation tree for the good, pre-order, and returns the first other good it mentions:
        // the chained cause (for example bread -> flour) whose producer should grow instead.
    }
}
