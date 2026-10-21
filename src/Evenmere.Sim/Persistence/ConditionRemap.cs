using Evenmere.Sim.Systems.Modifiers;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// Rewrites a saved modifier <c>when</c> condition (<see cref="ActiveModifier.When"/>) from the save's IDs to the
/// current content's. The flat prefix form is defined by <see cref="ConditionCodec"/>; this walker mirrors its
/// operation numbers and operand layouts exactly, mapping every embedded content ID through <see cref="SaveTables"/>.
/// A condition that names anything the content no longer has cannot be evaluated, so the whole condition is dropped
/// (the walker returns <see langword="null"/>) and the caller drops the modifier with a report.
/// </summary>
internal static class ConditionRemap
{
    // These mirror ConditionCodec's private operation numbers one for one. If that file adds an operation, this walker
    // must learn it: the codec round-trip test fails until it does.
    private const long OpCount = 1;
    private const long OpProduced = 2;
    private const long OpConsumed = 3;
    private const long OpStock = 4;
    private const long OpPopulation = 5;
    private const long OpRoadTiles = 6;
    private const long OpExpeditions = 7;
    private const long OpFestivalsHeld = 8;
    private const long OpGreatWorks = 9;
    private const long OpVisitorsPerDay = 10;
    private const long OpResearched = 11;
    private const long OpAdopted = 12;
    private const long OpDiscovered = 13;
    private const long OpSeason = 14;
    private const long OpEra = 15;
    private const long OpAll = 16;
    private const long OpAny = 17;
    private const long OpNot = 18;

    private const long SelectorNone = 0;
    private const long SelectorBuilding = 1;
    private const long SelectorTag = 2;
    private const long SelectorCategory = 3;

    /// <summary>Rewrites a flat condition to the current content's IDs.</summary>
    /// <param name="data">The saved flat condition, or <see langword="null"/> for an unconditional modifier.</param>
    /// <param name="tables">The old-to-new ID maps.</param>
    /// <param name="dropped">Whether the condition names a gone item and was dropped.</param>
    /// <returns>The rewritten condition, or <see langword="null"/> when <paramref name="data"/> is <see langword="null"/> or the condition was dropped.</returns>
    public static long[]? Remap(long[]? data, SaveTables tables, out bool dropped)
    {
        ArgumentNullException.ThrowIfNull(tables);
        dropped = false;
        if (data is null)
        {
            return null;
        }

        var output = new List<long>(data.Length);
        var reader = new Walker(data, tables, output);
        if (!reader.ReadCondition() || reader.Position != data.Length)
        {
            dropped = true;
            return null;
        }

        return [.. output];
    }

    private sealed class Walker(long[] data, SaveTables tables, List<long> output)
    {
        public int Position { get; private set; }

        public bool ReadCondition()
        {
            if (!Next(out long op))
            {
                return false;
            }

            switch (op)
            {
                case OpCount:
                    output.Add(op);
                    return ReadSelector() && ReadOptionalTerrain() && Copy(1);
                case OpProduced:
                case OpConsumed:
                case OpStock:
                    output.Add(op);
                    return MapId(tables.MapGood) && Copy(1);
                case OpPopulation:
                    output.Add(op);
                    return MapOptionalTier() && Copy(1);
                case OpRoadTiles:
                    output.Add(op);
                    return MapOptionalBuilding() && Copy(1);
                case OpExpeditions:
                case OpFestivalsHeld:
                case OpVisitorsPerDay:
                    output.Add(op);
                    return Copy(1);
                case OpGreatWorks:
                    output.Add(op);
                    return MapOptionalGreatWork() && Copy(2);
                case OpResearched:
                    output.Add(op);
                    return MapId(tables.MapTech);
                case OpAdopted:
                    output.Add(op);
                    return MapId(tables.MapCivic);
                case OpDiscovered:
                    output.Add(op);
                    return Copy(1) && MapOptionalTerrain() && Copy(1);
                case OpSeason:
                    output.Add(op);
                    return Copy(1);
                case OpEra:
                    output.Add(op);
                    return MapId(tables.MapEra);
                case OpAll:
                case OpAny:
                    output.Add(op);
                    if (!Next(out long count) || count < 1)
                    {
                        return false;
                    }

                    output.Add(count);
                    for (long i = 0; i < count; i++)
                    {
                        if (!ReadCondition())
                        {
                            return false;
                        }
                    }

                    return true;
                case OpNot:
                    output.Add(op);
                    return ReadCondition();
                default:
                    return false;
            }
        }

        private bool ReadSelector()
        {
            if (!Next(out long kind))
            {
                return false;
            }

            switch (kind)
            {
                case SelectorNone:
                    output.Add(kind);
                    return true;
                case SelectorBuilding:
                    output.Add(kind);
                    return MapId(tables.MapBuilding);
                case SelectorTag:
                    output.Add(kind);
                    if (!Next(out long length) || length < 0 || Position + length > data.Length)
                    {
                        return false;
                    }

                    output.Add(length);
                    for (long i = 0; i < length; i++)
                    {
                        output.Add(data[Position++]);
                    }

                    return true;
                case SelectorCategory:
                    output.Add(kind);
                    return Copy(1);
                default:
                    return false;
            }
        }

        private bool ReadOptionalTerrain()
        {
            if (!Next(out long raw))
            {
                return false;
            }

            if (raw == 0)
            {
                output.Add(0);
                return true;
            }

            int mapped = tables.MapTerrain(checked((int)raw - 1));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped + 1L);
            return true;
        }

        private bool MapOptionalTerrain()
        {
            if (!Next(out long raw))
            {
                return false;
            }

            if (raw == 0)
            {
                output.Add(0);
                return true;
            }

            int mapped = tables.MapTerrain(checked((int)raw - 1));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped + 1L);
            return true;
        }

        private bool MapOptionalTier()
        {
            if (!Next(out long raw))
            {
                return false;
            }

            if (raw == 0)
            {
                output.Add(0);
                return true;
            }

            int mapped = tables.MapTier(checked((int)raw - 1));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped + 1L);
            return true;
        }

        private bool MapOptionalBuilding()
        {
            if (!Next(out long raw))
            {
                return false;
            }

            if (raw == 0)
            {
                output.Add(0);
                return true;
            }

            int mapped = tables.MapBuilding(checked((int)raw - 1));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped + 1L);
            return true;
        }

        private bool MapOptionalGreatWork()
        {
            if (!Next(out long raw))
            {
                return false;
            }

            if (raw == 0)
            {
                output.Add(0);
                return true;
            }

            int mapped = tables.MapGreatWork(checked((int)raw - 1));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped + 1L);
            return true;
        }

        private bool MapId(Func<int, int> map)
        {
            if (!Next(out long raw))
            {
                return false;
            }

            int mapped = map(checked((int)raw));
            if (mapped < 0)
            {
                return false;
            }

            output.Add(mapped);
            return true;
        }

        private bool Copy(int count)
        {
            if (Position + count > data.Length)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                output.Add(data[Position++]);
            }

            return true;
        }

        private bool Next(out long value)
        {
            if (Position >= data.Length)
            {
                value = 0;
                return false;
            }

            value = data[Position++];
            return true;
        }
    }
}
