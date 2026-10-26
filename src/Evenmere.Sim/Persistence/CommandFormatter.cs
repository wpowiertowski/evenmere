using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Workforce.Commands;
using MessagePack;
using MessagePack.Formatters;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// The MessagePack shape of one logged command: a two-element array of the command's simple type name and its fields,
/// each field a primitive (an integer ID, a tile pair, a bool). New commands add a case on both sides; the order of
/// fields is part of the log format. Content IDs are the save's raw values; <see cref="CommandLogReader"/> maps them
/// on read.
/// </summary>
internal sealed class CommandFormatter : IMessagePackFormatter<Command?>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, Command? value, MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }
        writer.WriteArrayHeader(2);
        writer.Write(value.GetType().Name);
        switch (value)
        {
            case PlaceBuilding place:
                writer.WriteArrayHeader(4);
                writer.Write(place.Type.Value);
                writer.Write(place.X);
                writer.Write(place.Y);
                writer.Write((byte)place.Rotation);
                break;
            case Demolish demolish:
                writer.WriteArrayHeader(1);
                writer.Write(demolish.Building.Value);
                break;
            case UpgradeBuilding upgrade:
                writer.WriteArrayHeader(2);
                writer.Write(upgrade.Building.Value);
                writer.Write(upgrade.ToType.Value);
                break;
            case PlaceRoad road:
                writer.WriteArrayHeader(2);
                writer.Write(road.RoadType.Value);
                WriteTiles(ref writer, road.Path);
                break;
            case RemoveRoad remove:
                writer.WriteArrayHeader(1);
                WriteTiles(ref writer, remove.Tiles);
                break;
            case SetBuildingPaused paused:
                writer.WriteArrayHeader(2);
                writer.Write(paused.Building.Value);
                writer.Write(paused.Paused);
                break;
            case SetWorkforcePriority priority:
                writer.WriteArrayHeader(2);
                writer.Write(priority.Building.Value);
                writer.Write(priority.High);
                break;
            case SetHouseUpgradeLock houseLock:
                writer.WriteArrayHeader(2);
                writer.Write(houseLock.House.Value);
                writer.Write(houseLock.Locked);
                break;
            case SetResearchTarget target:
                writer.WriteArrayHeader(1);
                writer.Write(target.Tech.Value);
                break;
            case SetResearchQueue queue:
                writer.WriteArrayHeader(1);
                writer.WriteArrayHeader(queue.Queue.Length);
                foreach (TechId tech in queue.Queue)
                {
                    writer.Write(tech.Value);
                }

                break;
            case SetCivicTarget target:
                writer.WriteArrayHeader(1);
                writer.Write(target.Civic.Value);
                break;
            case SetCivicQueue queue:
                writer.WriteArrayHeader(1);
                writer.WriteArrayHeader(queue.Queue.Length);
                foreach (CivicId civic in queue.Queue)
                {
                    writer.Write(civic.Value);
                }

                break;
            case SetTradition tradition:
                writer.WriteArrayHeader(4);
                writer.Write((byte)tradition.Slot);
                writer.Write(tradition.Index);
                writer.Write(tradition.Tradition.HasValue);
                writer.Write(tradition.Tradition is { } card ? card.Value : (ushort)0);
                break;
            case HoldFestival held:
                writer.WriteArrayHeader(2);
                writer.Write(held.Ground.Value);
                writer.Write(held.Festival.Value);
                break;
            case DismissAdvice dismiss:
                writer.WriteArrayHeader(1);
                writer.Write(dismiss.AdviceId);
                break;
            default:
                throw new MessagePackSerializationException(
                    $"Command type {value.GetType().FullName} has no command-log formatter; add one to {nameof(CommandFormatter)}.");
        }
    }

    /// <inheritdoc />
    public Command? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        if (reader.ReadArrayHeader() != 2)
        {
            throw new MessagePackSerializationException("A logged command is a [type, fields] pair.");
        }

        string? name = reader.ReadString();
        if (name is null || !CommandLogOptions.TryGetCommandType(name, out Type? commandType) || commandType is null)
        {
            throw new MessagePackSerializationException($"The command log names an unknown command type '{name}'.");
        }

        try
        {
            return commandType.Name switch
            {
                nameof(PlaceBuilding) => ReadPlaceBuilding(ref reader),
                nameof(Demolish) => ReadDemolish(ref reader),
                nameof(UpgradeBuilding) => ReadUpgradeBuilding(ref reader),
                nameof(PlaceRoad) => ReadPlaceRoad(ref reader),
                nameof(RemoveRoad) => ReadRemoveRoad(ref reader),
                nameof(SetBuildingPaused) => ReadSetBuildingPaused(ref reader),
                nameof(SetWorkforcePriority) => ReadSetWorkforcePriority(ref reader),
                nameof(SetHouseUpgradeLock) => ReadSetHouseUpgradeLock(ref reader),
                nameof(SetResearchTarget) => ReadSetResearchTarget(ref reader),
                nameof(SetResearchQueue) => ReadSetResearchQueue(ref reader),
                nameof(SetCivicTarget) => ReadSetCivicTarget(ref reader),
                nameof(SetCivicQueue) => ReadSetCivicQueue(ref reader),
                nameof(SetTradition) => ReadSetTradition(ref reader),
                nameof(HoldFestival) => ReadHoldFestival(ref reader),
                nameof(DismissAdvice) => ReadDismissAdvice(ref reader),
                _ => throw new MessagePackSerializationException($"The command log names an unknown command type '{name}'."),
            };
        }
        catch (MessagePackSerializationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            throw new MessagePackSerializationException($"The command log entry for '{name}' is malformed.", exception);
        }
    }

    private static void WriteTiles(ref MessagePackWriter writer, TileCoord[] tiles)
    {
        writer.WriteArrayHeader(tiles.Length);
        foreach (TileCoord tile in tiles)
        {
            writer.WriteArrayHeader(2);
            writer.Write(tile.X);
            writer.Write(tile.Y);
        }
    }

    private static TileCoord[] ReadTiles(ref MessagePackReader reader)
    {
        int count = reader.ReadArrayHeader();
        var tiles = new TileCoord[count];
        for (int i = 0; i < count; i++)
        {
            if (reader.ReadArrayHeader() != 2)
            {
                throw new MessagePackSerializationException("A logged tile is an [x, y] pair.");
            }

            tiles[i] = new TileCoord(reader.ReadInt32(), reader.ReadInt32());
        }

        return tiles;
    }

    private static ushort[] ReadIds(ref MessagePackReader reader)
    {
        int count = reader.ReadArrayHeader();
        ushort[] ids = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = reader.ReadUInt16();
        }

        return ids;
    }

    private static void ExpectFields(ref MessagePackReader reader, int count)
    {
        if (reader.ReadArrayHeader() != count)
        {
            throw new MessagePackSerializationException($"A logged command has {count} fields.");
        }
    }

    private static PlaceBuilding ReadPlaceBuilding(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 4);
        return new PlaceBuilding(
            BuildingTypeId.From(reader.ReadUInt16()),
            reader.ReadInt32(),
            reader.ReadInt32(),
            (Rotation)reader.ReadByte());
    }

    private static Demolish ReadDemolish(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        return new Demolish(new EntityId(reader.ReadInt32()));
    }

    private static UpgradeBuilding ReadUpgradeBuilding(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        return new UpgradeBuilding(new EntityId(reader.ReadInt32()), BuildingTypeId.From(reader.ReadUInt16()));
    }

    private static PlaceRoad ReadPlaceRoad(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        var roadType = BuildingTypeId.From(reader.ReadUInt16());
        return new PlaceRoad(ReadTiles(ref reader), roadType);
    }

    private static RemoveRoad ReadRemoveRoad(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        return new RemoveRoad(ReadTiles(ref reader));
    }

    private static SetBuildingPaused ReadSetBuildingPaused(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        return new SetBuildingPaused(new EntityId(reader.ReadInt32()), reader.ReadBoolean());
    }

    private static SetWorkforcePriority ReadSetWorkforcePriority(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        return new SetWorkforcePriority(new EntityId(reader.ReadInt32()), reader.ReadBoolean());
    }

    private static SetHouseUpgradeLock ReadSetHouseUpgradeLock(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        return new SetHouseUpgradeLock(new EntityId(reader.ReadInt32()), reader.ReadBoolean());
    }

    private static SetResearchTarget ReadSetResearchTarget(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        return new SetResearchTarget(TechId.From(reader.ReadUInt16()));
    }

    private static SetResearchQueue ReadSetResearchQueue(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        ushort[] ids = ReadIds(ref reader);
        var queue = new TechId[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            queue[i] = TechId.From(ids[i]);
        }

        return new SetResearchQueue(queue);
    }

    private static SetCivicTarget ReadSetCivicTarget(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        return new SetCivicTarget(CivicId.From(reader.ReadUInt16()));
    }

    private static SetCivicQueue ReadSetCivicQueue(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        ushort[] ids = ReadIds(ref reader);
        var queue = new CivicId[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            queue[i] = CivicId.From(ids[i]);
        }

        return new SetCivicQueue(queue);
    }

    private static SetTradition ReadSetTradition(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 4);
        var slot = (TraditionSlotType)reader.ReadByte();
        int index = reader.ReadInt32();
        bool slotted = reader.ReadBoolean();
        ushort value = reader.ReadUInt16();
        if (!Enum.IsDefined(slot))
        {
            throw new InvalidOperationException($"A logged SetTradition names no tradition slot type ({(byte)slot}).");
        }

        return new SetTradition(slot, index, slotted ? TraditionId.From(value) : null);
    }

    private static HoldFestival ReadHoldFestival(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 2);
        int ground = reader.ReadInt32();
        ushort festival = reader.ReadUInt16();
        return new HoldFestival(new EntityId(ground), FestivalId.From(festival));
    }

    private static DismissAdvice ReadDismissAdvice(ref MessagePackReader reader)
    {
        ExpectFields(ref reader, 1);
        return new DismissAdvice(reader.ReadInt32());
    }
}
