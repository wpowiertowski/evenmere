using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Workforce.Commands;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The command-log coverage guard: every concrete <see cref="Command"/> in Evenmere.Sim needs a sample in the table
/// below and a formatter case in <see cref="CommandFormatter"/>. A new command type without either fails here, naming
/// the type and both places to update, instead of crashing the game the first time the command is logged.
/// </summary>
public sealed class CommandLogCoverageTests
{
    [Fact]
    public void AllCommands_RoundTripThroughLog()
    {
        ContentDb content = SiteFinder.Content;
        var failures = new List<string>();
        foreach (Type type in CommandTypes())
        {
            if (!CommandSamples.TrySample(content, type, out Command? sample) || sample is null)
            {
                failures.Add(
                    $"command '{type.Name}' has no sample; add one to {nameof(CommandSamples)} in {nameof(CommandLogCoverageTests)} "
                    + $"and a formatter case in {nameof(CommandFormatter)}");
                continue;
            }

            try
            {
                using var stream = new MemoryStream();
                var writer = new CommandLogWriter(stream);
                writer.Append(42, sample);
                stream.Position = 0;

                var reader = new CommandLogReader(stream, IdentityTables(content));
                IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();
                entries.ShouldHaveSingleItem();
                entries[0].Tick.ShouldBe(42);
                entries[0].Command.ShouldBe(sample);
            }
            catch (Exception exception) when (exception is not Shouldly.ShouldAssertException)
            {
                failures.Add(
                    $"command '{type.Name}' failed to round-trip ({exception.GetType().Name}: {exception.Message}); "
                    + $"check its sample in {nameof(CommandSamples)} in {nameof(CommandLogCoverageTests)} "
                    + $"and its formatter case in {nameof(CommandFormatter)}");
            }
        }

        failures.ShouldBeEmpty();
    }

    private static IReadOnlyList<Type> CommandTypes() =>
    [
        .. typeof(Command).Assembly.GetTypes()
            .Where(static t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Command)))
            .OrderBy(static t => t.FullName, StringComparer.Ordinal),
    ];

    private static SaveTables IdentityTables(ContentDb content)
    {
        var header = new SaveHeader
        {
            Goods = [.. content.Goods.Names],
            Buildings = [.. content.Buildings.Names],
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = [.. content.Techs.Names],
            Civics = [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = [.. content.Eras.Names],
            Terrains = [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }

    private static class CommandSamples
    {
        public static bool TrySample(ContentDb content, Type type, out Command? sample)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(type);

            sample = type switch
            {
                _ when type == typeof(PlaceBuilding) => new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0),
                _ when type == typeof(Demolish) => new Demolish(new EntityId(7)),
                _ when type == typeof(UpgradeBuilding) => new UpgradeBuilding(new EntityId(7), content.IdOf<BuildingTypeId>("storehouse")),
                _ when type == typeof(PlaceRoad) => new PlaceRoad([new TileCoord(1, 2), new TileCoord(1, 3)], content.Required.DirtRoad),
                _ when type == typeof(RemoveRoad) => new RemoveRoad([new TileCoord(1, 2)]),
                _ when type == typeof(SetBuildingPaused) => new SetBuildingPaused(new EntityId(7), true),
                _ when type == typeof(SetWorkforcePriority) => new SetWorkforcePriority(new EntityId(7), true),
                _ when type == typeof(SetHouseUpgradeLock) => new SetHouseUpgradeLock(new EntityId(7), true),
                _ when type == typeof(SetResearchTarget) => new SetResearchTarget(content.IdOf<TechId>("cultivation")),
                _ when type == typeof(SetResearchQueue) => new SetResearchQueue([content.IdOf<TechId>("cultivation"), content.IdOf<TechId>("milling")]),
                _ when type == typeof(SetCivicTarget) => new SetCivicTarget(content.IdOf<CivicId>("hearth_and_shrine")),
                _ when type == typeof(SetCivicQueue) => new SetCivicQueue([content.IdOf<CivicId>("hearth_and_shrine")]),
                _ when type == typeof(SetTradition) => new SetTradition(TraditionSlotType.Hearth, 0, null),
                _ when type == typeof(DismissAdvice) => new DismissAdvice(3),
                _ => null,
            };
            return sample is not null;
        }
    }
}
