using System.Reflection;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// The remap coverage guard: the shift-every-kind overlay adds one item sorting first in every content kind, so every
/// ID of every kind moves. The slice save is then walked value by value, before the remap under the old content and
/// after it under the new content:
/// <list type="bullet">
/// <item>every typed-ID value (and every effect source, subject link, modifier target and the era) must name the same
/// string ID on both sides, unless the report lists the item as dropped or ruined;</item>
/// <item>every <c>[ContentIndexed]</c> member must hold, at each new index, the old value of the same string ID
/// (the default for a new item, the start bit for a newly unlocked one).</item>
/// </list>
/// The walker is keyed by type and member shape, never by member name (beyond the two documented parallel-array
/// companions), so a new scalar, record or IdTable member is covered automatically. On failure the messages list the
/// member paths and the mismatched names.
/// </summary>
public sealed class RemapCoverageTests
{
    private readonly ITestOutputHelper _output;

    public RemapCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Remap_ShiftEveryKind_AllValuesAndIndexedMembersFollowNames()
    {
        ContentDb oldContent = SiteFinder.Content;
        ContentOverlays.ShiftEveryKind(out ContentDb content, out _);

        WorldState pre = LoadFixtureWorld(oldContent);
        (WorldState post, SaveReport report) = LoadFixtureRemapped(content);

        report.Dropped.ShouldBeEmpty();
        report.Ruins.ShouldBeEmpty();
        report.Remapped.ShouldContain(line => line.Contains("bread", StringComparison.Ordinal));

        post.Clock.Tick.ShouldBe(pre.Clock.Tick);
        post.Seed.ShouldBe(pre.Seed);
        post.EntityIds.NextValue.ShouldBe(pre.EntityIds.NextValue);
        post.SystemRngs.Select(static r => (r.State, r.Inc)).ShouldBe(pre.SystemRngs.Select(static r => (r.State, r.Inc)));

        var failures = new List<string>();
        var walk = new CoverageWalk(oldContent, content, report, failures);
        walk.Check("WorldState", pre, post, typeof(WorldState));
        _output.WriteLine($"checked {walk.IdValuesChecked} typed-ID values, {walk.IndexedElementsChecked} indexed elements and bits, {walk.SeriesChecked} statistics series");
        int expectedSeries = ((content.Goods.Count * 4) + content.Tiers.Count + 3) * 2;
        walk.SeriesChecked.ShouldBe(expectedSeries);
        walk.IndexedElementsChecked.ShouldBeGreaterThan(100);
        walk.IdValuesChecked.ShouldBeGreaterThan(10000);
        _output.WriteLine($"checked {walk.IdValuesChecked} typed-ID values, {walk.IndexedElementsChecked} indexed elements and bits, {walk.SeriesChecked} statistics series");
        failures.ShouldBeEmpty();
    }

    /// <summary>
    /// The M2-30 content-growth proof: the frozen v1 fixture loads against content with one extra building that sorts
    /// first, so every building ID shifts. The load must remap (not clean) yet lose nothing (lossless), and continuing
    /// from the remapped load must be deterministic.
    /// </summary>
    [Fact]
    public void Remap_ExtraBuildingFirst_FrozenFixtureLoadsLosslesslyAndContinuesDeterministically()
    {
        ContentOverlays.ExtraBuildingFirst(out ContentDb content);

        (ISimulation first, SaveReport firstReport) = ReadFrozen(content);
        (ISimulation second, SaveReport secondReport) = ReadFrozen(content);

        firstReport.IsLossless.ShouldBeTrue();
        secondReport.IsLossless.ShouldBeTrue();
        firstReport.IsClean.ShouldBeFalse("the extra building shifts every building ID");
        firstReport.Remapped.ShouldContain(
            static line => line.Contains("building types", StringComparison.Ordinal));

        const long Ticks = 2_000;
        long end = first.CurrentTick + Ticks;
        for (long tick = first.CurrentTick + 1; tick <= end; tick++)
        {
            first.Tick();
            second.Tick();
        }

        first.ComputeStateHash().ShouldBe(second.ComputeStateHash());
    }

    private static (ISimulation Simulation, SaveReport Report) ReadFrozen(ContentDb content)
    {
        using FileStream stream = File.OpenRead(SaveFixtureTests.FixturePath);
        return SaveGame.Read(stream, content);
    }

    private static WorldState LoadFixtureWorld(ContentDb content)
    {
        // The before-world is the current-version golden, which the golden updater regenerates whenever content
        // changes, so the baseline load is clean; the frozen v1 save cannot promise that under grown content.
        using FileStream stream = File.OpenRead(SaveFixtureTests.CurrentFixturePath);
        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, content);
        report.IsClean.ShouldBeTrue();
        return ((Simulation)simulation).World;
    }

    private static (WorldState World, SaveReport Report) LoadFixtureRemapped(ContentDb content)
    {
        using FileStream stream = File.OpenRead(SaveFixtureTests.CurrentFixturePath);
        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, content);
        return (((Simulation)simulation).World, report);
    }

    private sealed class CoverageWalk(ContentDb oldContent, ContentDb content, SaveReport report, List<string> failures)
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private readonly Dictionary<ContentKind, Dictionary<string, int>> _oldByName = new();
        private readonly Dictionary<ContentKind, Dictionary<string, int>> _newByName = new();

        public int IdValuesChecked { get; private set; }

        public int IndexedElementsChecked { get; private set; }

        public int SeriesChecked { get; private set; }

        private MemberInfo? _member;

        private Type? _declaringType;

        private object? _preOwner;

        private object? _postOwner;

        public void Check(string path, object? pre, object? post, Type declaredType)
        {
            if (pre is null && post is null)
            {
                return;
            }

            if (pre is null || post is null)
            {
                failures.Add($"{path}: {(pre is null ? "missing before" : "missing after")} the remap");
                return;
            }

            Type underlying = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (underlying != declaredType)
            {
                Check(path, pre, post, underlying);
                return;
            }

            if (IsContentId(underlying))
            {
                CheckNamed(path, KindOf(underlying), RawOf(pre), RawOf(post));
                return;
            }

            if (underlying == typeof(EffectSource))
            {
                CheckSource(path, (EffectSource)pre, (EffectSource)post);
                return;
            }

            if (underlying == typeof(SubjectLink))
            {
                CheckSubject(path, (SubjectLink)pre, (SubjectLink)post);
                return;
            }

            if (underlying == typeof(ActiveModifier))
            {
                CheckModifier(path, (ActiveModifier)pre, (ActiveModifier)post);
                return;
            }

            if (underlying.IsArray)
            {
                CheckArray(path, (Array)pre, (Array)post, underlying.GetElementType()!);
                return;
            }

            if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(IdTable<>))
            {
                CheckTable(path, pre, post, underlying.GetGenericArguments()[0]);
                return;
            }

            if (underlying == typeof(int) && _member?.GetCustomAttribute<ContentValuedAttribute>(inherit: false) is { } valued)
            {
                if (valued.Kinds.Count != 1)
                {
                    failures.Add($"{path}: the coverage walk supports one content kind on a scalar, not {valued.Kinds.Count}");
                    return;
                }

                CheckNamed(path, valued.Kinds[0], (int)pre, (int)post);
                return;
            }

            if (IsLeaf(underlying))
            {
                return;
            }

            CheckObject(path, pre, post, underlying);
        }

        private void CheckSource(string path, EffectSource pre, EffectSource post)
        {
            if (pre.Kind != post.Kind)
            {
                failures.Add($"{path}: source kind changed from {pre.Kind} to {post.Kind}");
                return;
            }

            if (pre.Kind == EffectSourceKind.Building)
            {
                if (pre.Value != post.Value)
                {
                    failures.Add($"{path}: building source changed entity from {pre.Value} to {post.Value}");
                }

                return;
            }

            ContentKind kind = pre.Kind switch
            {
                EffectSourceKind.Tech => ContentKind.Tech,
                EffectSourceKind.Civic => ContentKind.Civic,
                EffectSourceKind.Tradition => ContentKind.Tradition,
                EffectSourceKind.Festival => ContentKind.Festival,
                EffectSourceKind.Monument => ContentKind.Monument,
                EffectSourceKind.Happening => ContentKind.Happening,
                _ => throw new InvalidOperationException($"Unknown effect source kind {pre.Kind}."),
            };
            CheckNamed(path, kind, pre.Value, post.Value);
        }

        private void CheckSubject(string path, SubjectLink pre, SubjectLink post)
        {
            if (pre.Kind != post.Kind)
            {
                failures.Add($"{path}: subject kind changed from {pre.Kind} to {post.Kind}");
                return;
            }

            ContentKind? kind = pre.Kind switch
            {
                SubjectKind.Good => ContentKind.Good,
                SubjectKind.Tier => ContentKind.Tier,
                SubjectKind.BuildingType => ContentKind.Building,
                _ => null,
            };
            if (kind is null)
            {
                if (pre.Value != post.Value)
                {
                    failures.Add($"{path}: subject value changed from {pre.Value} to {post.Value}");
                }

                return;
            }

            CheckNamed(path, kind.Value, pre.Value, post.Value);
        }

        private void CheckModifier(string path, ActiveModifier pre, ActiveModifier post)
        {
            CheckSource(path + ".Source", pre.Source, post.Source);
            if (pre.TargetKind != post.TargetKind)
            {
                failures.Add($"{path}.TargetKind: changed from {pre.TargetKind} to {post.TargetKind}");
                return;
            }

            ContentKind? kind = pre.TargetKind switch
            {
                ModifierTargetKind.Building => ContentKind.Building,
                ModifierTargetKind.Tier => ContentKind.Tier,
                ModifierTargetKind.Good => ContentKind.Good,
                _ => null,
            };
            if (kind is null)
            {
                if (pre.TargetValue != post.TargetValue)
                {
                    failures.Add($"{path}.TargetValue: changed from {pre.TargetValue} to {post.TargetValue}");
                }

                return;
            }

            CheckNamed(path + ".TargetValue", kind.Value, pre.TargetValue, post.TargetValue);
        }

        private void CheckNamed(string path, ContentKind kind, int preRaw, int postRaw)
        {
            IdValuesChecked++;
            string oldName = OldName(kind, preRaw);
            string newName = NewName(kind, postRaw);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal) && !Excused(oldName))
            {
                failures.Add($"{path}: '{oldName}' became '{newName}' with no report line");
            }
        }

        private void CheckArray(string path, Array pre, Array post, Type elementType)
        {
            ContentIndexedAttribute? indexed = _member?.GetCustomAttribute<ContentIndexedAttribute>(inherit: false);
            ContentValuedAttribute? valued = _member?.GetCustomAttribute<ContentValuedAttribute>(inherit: false);

            if (indexed is not null)
            {
                CheckIndexed(path, pre, post, elementType, indexed.Kinds);
                return;
            }

            if (valued is not null && elementType == typeof(long) && _declaringType == typeof(LogisticsState))
            {
                // RetryDays is covered alongside RetryKeys below; any other long payload is opaque to the walk.
                return;
            }

            if (valued is not null && elementType.IsPrimitive)
            {
                CheckValuedPrimitives(path, pre, post, elementType);
                return;
            }

            if (valued is not null || IsComposite(elementType))
            {
                // Effect sources, advice records, modifiers and queues: pairwise, with the values checked inside.
                if (pre.Length != post.Length)
                {
                    failures.Add($"{path}: has {pre.Length} entries before and {post.Length} after the remap");
                    return;
                }

                for (int i = 0; i < pre.Length; i++)
                {
                    Check($"{path}[{i}]", pre.GetValue(i), post.GetValue(i), elementType);
                }

                return;
            }

            CheckUnmarkedPrimitives(path, pre, post);
        }

        private void CheckIndexed(string path, Array pre, Array post, Type elementType, IReadOnlyList<ContentKind> kinds)
        {
            if (kinds.Count == 2 && _declaringType == typeof(StatisticsState))
            {
                CheckStatisticsSeries(path, (long[])pre, (long[])post);
                return;
            }

            if (kinds.Count != 1)
            {
                failures.Add($"{path}: the coverage walk supports one content kind, not {kinds.Count}");
                return;
            }

            if (_declaringType == typeof(ProducerRecord))
            {
                CheckProducerBuffers(path, pre, post);
                return;
            }

            ContentKind kind = kinds[0];
            int count = NewCount(kind);
            int expectedLength = elementType == typeof(ulong) ? (count + 63) >> 6 : count;
            if (post.Length != expectedLength)
            {
                failures.Add($"{path}: has {post.Length} entries after the remap but the content defines {count}");
            }

            Dictionary<string, int> oldByName = OldByName(kind);
            Dictionary<string, int> newByName = NewByName(kind);
            if (elementType == typeof(ulong))
            {
                ulong[] preWords = (ulong[])pre;
                ulong[] postWords = (ulong[])post;
                foreach ((string name, int now) in newByName)
                {
                    IndexedElementsChecked++;
                    bool expected = oldByName.TryGetValue(name, out int old) ? Bit(preWords, old) : StartBit(kind, name);
                    if (Bit(postWords, now) != expected)
                    {
                        failures.Add($"{path}: bit for '{name}' is {(Bit(postWords, now) ? "set" : "clear")} but should be {(expected ? "set" : "clear")}");
                    }
                }

                return;
            }

            if (IsContentId(elementType))
            {
                CheckIdSequence(path, pre, post, kind);
                return;
            }

            for (int now = 0; now < post.Length; now++)
            {
                IndexedElementsChecked++;
                string name = NewName(kind, now);
                object? postValue = post.GetValue(now);
                if (oldByName.TryGetValue(name, out int old))
                {
                    object? preValue = old < pre.Length ? pre.GetValue(old) : null;
                    if (!Equals(preValue, postValue))
                    {
                        failures.Add($"{path}[{now}] ('{name}'): value changed by the remap");
                    }
                }
                else if (!IsDefault(postValue))
                {
                    failures.Add($"{path}[{now}] ('{name}'): a new item holds a non-default value");
                }
            }
        }

        private void CheckIdSequence(string path, Array pre, Array post, ContentKind kind)
        {
            // A filtered ID list (a research queue): the new names must be the old names minus the reported drops.
            var oldNames = new List<string>(pre.Length);
            for (int i = 0; i < pre.Length; i++)
            {
                oldNames.Add(OldName(kind, RawOf(pre.GetValue(i)!)));
            }

            var newNames = new List<string>(post.Length);
            for (int i = 0; i < post.Length; i++)
            {
                newNames.Add(NewName(kind, RawOf(post.GetValue(i)!)));
            }

            int j = 0;
            foreach (string oldName in oldNames)
            {
                if (j < newNames.Count && string.Equals(newNames[j], oldName, StringComparison.Ordinal))
                {
                    j++;
                }
                else if (!Excused(oldName))
                {
                    failures.Add($"{path}: '{oldName}' left the list with no report line");
                }
            }

            if (j != newNames.Count)
            {
                failures.Add($"{path}: has unexpected entries after the remap: [{string.Join(",", newNames)}]");
            }
        }

        private void CheckProducerBuffers(string path, Array pre, Array post)
        {
            // ProducerRecord buffers run parallel to their recipe's goods, not to good IDs: the k-th surviving
            // new input held the k-th old buffer. The recipe is read off the sibling record on each side.
            var preRecord = (ProducerRecord)_preOwner!;
            var postRecord = (ProducerRecord)_postOwner!;
            RecipeDef def = content.Get(postRecord.Recipe);
            int[] inverse = Invert(GoodMap(), content.Goods.Count);

            System.Collections.Immutable.ImmutableArray<Evenmere.Content.Definitions.GoodAmount> lines =
                _member?.Name switch
                {
                    nameof(ProducerRecord.Inputs) => def.Inputs,
                    nameof(ProducerRecord.Outputs) => def.Outputs,
                    _ => def.Inputs,
                };
            bool isOutput = _member?.Name == nameof(ProducerRecord.Outputs) || _member?.Name == nameof(ProducerRecord.OutputRequestTicks);
            if (_member?.Name == nameof(ProducerRecord.Inputs) || _member?.Name == nameof(ProducerRecord.InputRequestTicks))
            {
                lines = def.Inputs;
            }
            else if (isOutput)
            {
                lines = def.Outputs;
            }

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
            for (int n = 0; n < lines.Length && n < post.Length; n++)
            {
                int oldId = (uint)lines[n].Good.Value < (uint)inverse.Length ? inverse[lines[n].Good.Value] : -1;
                int rank = oldId < 0 ? -1 : surviving.IndexOf(oldId);
                object? expected = rank >= 0 && rank < pre.Length ? pre.GetValue(rank) : DefaultOf(post.GetType().GetElementType()!);
                object? actual = post.GetValue(n);
                if (!Equals(expected, actual))
                {
                    failures.Add($"{path}[{n}] ('{content.NameOf(lines[n].Good)}'): buffer changed by the remap");
                }
            }

            _ = preRecord;
        }

        private void CheckStatisticsSeries(string path, long[] pre, long[] post)
        {
            int oldGoods = oldContent.Goods.Count;
            int oldTiers = oldContent.Tiers.Count;
            int newGoods = content.Goods.Count;
            int newTiers = content.Tiers.Count;
            int capacity = path.EndsWith(".Seasonal", StringComparison.Ordinal)
                ? StatisticsState.SeasonalCapacity
                : StatisticsState.DailyCapacity;

            int newSeries = (newGoods * 4) + newTiers + 3;
            if (post.Length != newSeries * capacity)
            {
                failures.Add($"{path}: has {post.Length} samples after the remap, expected {newSeries * capacity}");
                return;
            }

            for (int s = 0; s < newSeries; s++)
            {
                SeriesChecked++;
                (ContentKind? kind, string? name, int oldIndex) = SeriesMapping(s, newGoods, newTiers, oldGoods, oldTiers);
                if (kind is null)
                {
                    int singles = s - ((newGoods * 4) + newTiers);
                    int oldSingles = oldIndex - ((oldGoods * 4) + oldTiers);
                    if (singles != oldSingles)
                    {
                        failures.Add($"{path}: single series {s} moved to {oldIndex}");
                        continue;
                    }

                    for (int slot = 0; slot < capacity; slot++)
                    {
                        if (pre[(oldIndex * capacity) + slot] != post[(s * capacity) + slot])
                        {
                            failures.Add($"{path}: single series {s} slot {slot} changed by the remap");
                            break;
                        }
                    }

                    continue;
                }

                for (int slot = 0; slot < capacity; slot++)
                {
                    long postValue = post[(s * capacity) + slot];
                    if (oldIndex < 0)
                    {
                        if (postValue != 0)
                        {
                            failures.Add($"{path}: new series '{name}' holds {postValue} at slot {slot}");
                            break;
                        }

                        continue;
                    }

                    long preValue = pre[(oldIndex * capacity) + slot];
                    if (preValue != postValue)
                    {
                        failures.Add($"{path}: series '{name}' slot {slot} changed from {preValue} to {postValue}");
                        break;
                    }
                }
            }
        }

        private (ContentKind? Kind, string? Name, int OldIndex) SeriesMapping(int series, int newGoods, int newTiers, int oldGoods, int oldTiers)
        {
            Dictionary<string, int> oldGoodByName = OldByName(ContentKind.Good);
            Dictionary<string, int> oldTierByName = OldByName(ContentKind.Tier);
            if (series < newGoods * 4)
            {
                int block = series / newGoods;
                string name = content.Goods.Names[series % newGoods];
                int oldIndex = oldGoodByName.TryGetValue(name, out int old) ? (block * oldGoods) + old : -1;
                return (ContentKind.Good, name, oldIndex);
            }

            if (series < (newGoods * 4) + newTiers)
            {
                string name = content.Tiers.Names[series - (newGoods * 4)];
                int oldIndex = oldTierByName.TryGetValue(name, out int old) ? (oldGoods * 4) + old : -1;
                return (ContentKind.Tier, name, oldIndex);
            }

            int singles = series - ((newGoods * 4) + newTiers);
            return (null, null, ((oldGoods * 4) + oldTiers) + singles);
        }

        private void CheckValuedPrimitives(string path, Array pre, Array post, Type elementType)
        {
            if (_declaringType == typeof(RoadsState) && elementType == typeof(ushort))
            {
                ushort[] preLayer = (ushort[])pre;
                ushort[] postLayer = (ushort[])post;
                if (preLayer.Length != postLayer.Length)
                {
                    failures.Add($"{path}: the road layer changed length");
                    return;
                }

                for (int i = 0; i < preLayer.Length; i++)
                {
                    if (preLayer[i] == 0)
                    {
                        if (postLayer[i] != 0)
                        {
                            failures.Add($"{path}[{i}]: an empty tile gained a road");
                        }

                        continue;
                    }

                    CheckNamed($"{path}[{i}]", ContentKind.Building, preLayer[i] - 1, postLayer[i] - 1);
                }

                return;
            }

            if (_declaringType == typeof(MapState) && elementType == typeof(byte))
            {
                byte[] preTerrain = (byte[])pre;
                byte[] postTerrain = (byte[])post;
                if (preTerrain.Length != postTerrain.Length)
                {
                    failures.Add($"{path}: the terrain layer changed length");
                    return;
                }

                for (int i = 0; i < preTerrain.Length; i++)
                {
                    CheckNamed($"{path}[{i}]", ContentKind.Terrain, preTerrain[i], postTerrain[i]);
                }

                return;
            }

            if (_declaringType == typeof(LogisticsState) && elementType == typeof(long))
            {
                CheckRetryKeys(path, (long[])pre, (long[])post);
                return;
            }

            failures.Add($"{path}: the coverage walk does not know this content-valued primitive array");
        }

        private void CheckRetryKeys(string path, long[] pre, long[] post)
        {
            long[] preDays = (long[])((LogisticsState)_preOwner!).RetryDays;
            long[] postDays = (long[])((LogisticsState)_postOwner!).RetryDays;
            SortedDictionary<string, long> PreKeyed() => Keyed(pre, preDays, OldGoodName);
            SortedDictionary<string, long> PostKeyed() => Keyed(post, postDays, NewGoodName);

            SortedDictionary<string, long> preKeyed = PreKeyed();
            SortedDictionary<string, long> postKeyed = PostKeyed();
            foreach ((string key, long day) in postKeyed)
            {
                if (!preKeyed.TryGetValue(key, out long oldDay))
                {
                    failures.Add($"{path}: retry entry '{key}' appeared in the remap");
                }
                else if (oldDay != day)
                {
                    failures.Add($"{path}: retry entry '{key}' moved from day {oldDay} to day {day}");
                }
            }

            foreach (string key in preKeyed.Keys)
            {
                if (!postKeyed.ContainsKey(key))
                {
                    failures.Add($"{path}: retry entry '{key}' lost in the remap");
                }
            }

            SortedDictionary<string, long> Keyed(long[] keys, long[] days, Func<int, string> nameOf)
            {
                var keyed = new SortedDictionary<string, long>(StringComparer.Ordinal);
                for (int i = 0; i < keys.Length; i++)
                {
                    int building = (int)(keys[i] >> 32);
                    int good = (int)((keys[i] & 0xFFFFFFFFL) >> 1);
                    long direction = keys[i] & 1L;
                    keyed[$"{building}:{nameOf(good)}:{direction}"] = days[i];
                }

                return keyed;
            }
        }

        private void CheckTable(string path, object pre, object post, Type valueType)
        {
            try
            {
                MethodInfo helper = typeof(CoverageWalk).GetMethod(nameof(CheckTableTyped), Members)!;
                helper.MakeGenericMethod(valueType).Invoke(this, [path, pre, post]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        private void CheckTableTyped<TValue>(string path, IdTable<TValue> pre, IdTable<TValue> post)
        {
            var preIds = new List<int>();
            foreach (IdTable<TValue>.Entry entry in pre)
            {
                preIds.Add(entry.Id.Value);
            }

            var postIds = new List<int>();
            foreach (IdTable<TValue>.Entry entry in post)
            {
                postIds.Add(entry.Id.Value);
            }

            if (!preIds.SequenceEqual(postIds))
            {
                failures.Add($"{path}: entry IDs changed from [{string.Join(",", preIds)}] to [{string.Join(",", postIds)}]");
                return;
            }

            foreach (IdTable<TValue>.Entry entry in pre)
            {
                post.TryGet(entry.Id, out TValue? postValue);
                Check($"{path}[id={entry.Id.Value}]", entry.Value, postValue, typeof(TValue));
            }
        }

        private void CheckObject(string path, object pre, object post, Type type)
        {
            if (type == typeof(StatisticsState))
            {
                var preState = (StatisticsState)pre;
                var postState = (StatisticsState)post;
                if (preState.DaysSampled != postState.DaysSampled)
                {
                    failures.Add($"{path}.DaysSampled: changed from {preState.DaysSampled} to {postState.DaysSampled}");
                }

                if (preState.SeasonsSampled != postState.SeasonsSampled)
                {
                    failures.Add($"{path}.SeasonsSampled: changed from {preState.SeasonsSampled} to {postState.SeasonsSampled}");
                }

                if (postState.GoodCount != content.Goods.Count || postState.TierCount != content.Tiers.Count)
                {
                    failures.Add($"{path}: sized for {postState.GoodCount} goods and {postState.TierCount} tiers, not {content.Goods.Count} and {content.Tiers.Count}");
                }
            }

            object? savedPre = _preOwner;
            object? savedPost = _postOwner;
            _preOwner = pre;
            _postOwner = post;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() is null)
                {
                    continue;
                }

                if (property.IsDefined(typeof(MessagePack.IgnoreMemberAttribute), inherit: true))
                {
                    continue;
                }

                _member = property;
                _declaringType = type;
                Check(
                    $"{path}.{property.Name}",
                    property.GetValue(pre),
                    property.GetValue(post),
                    property.PropertyType);
            }

            _member = null;
            _declaringType = null;
            _preOwner = savedPre;
            _postOwner = savedPost;
        }

        private void CheckUnmarkedPrimitives(string path, Array pre, Array post)
        {
            // RetryDays travels with the packed RetryKeys it parallels (checked there), and the producer
            // request ticks travel with their buffers (checked with them).
            if ((_declaringType == typeof(LogisticsState) && _member?.Name == "RetryDays")
                || (_declaringType == typeof(ProducerRecord)
                    && (_member?.Name == nameof(ProducerRecord.InputRequestTicks) || _member?.Name == nameof(ProducerRecord.OutputRequestTicks))))
            {
                return;
            }

            if (pre.Length != post.Length)
            {
                failures.Add($"{path}: changed length from {pre.Length} to {post.Length}");
                return;
            }

            for (int i = 0; i < pre.Length; i++)
            {
                if (!Equals(pre.GetValue(i), post.GetValue(i)))
                {
                    failures.Add($"{path}[{i}]: changed by the remap");
                    return;
                }
            }
        }

        private bool Excused(string oldName) =>
            report.Dropped.Any(line => line.Contains($"'{oldName}'", StringComparison.Ordinal))
            || report.Ruins.Any(line => line.Contains($"'{oldName}'", StringComparison.Ordinal));

        private int[] GoodMap() => _goodMap ??= BuildGoodMap();

        private int[]? _goodMap;

        private int[] BuildGoodMap()
        {
            int[] map = new int[OldByName(ContentKind.Good).Count];
            Dictionary<string, int> olds = OldByName(ContentKind.Good);
            Dictionary<string, int> news = NewByName(ContentKind.Good);
            foreach ((string name, int old) in olds)
            {
                map[old] = news.TryGetValue(name, out int now) ? now : -1;
            }

            return map;
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

        private string OldGoodName(int raw) => OldName(ContentKind.Good, raw);

        private string NewGoodName(int raw) => NewName(ContentKind.Good, raw);

        private string OldName(ContentKind kind, int raw) => kind switch
        {
            ContentKind.Good => NameAt(oldContent.Goods.Names, raw),
            ContentKind.Building => NameAt(oldContent.Buildings.Names, raw),
            ContentKind.Recipe => NameAt(oldContent.Recipes.Names, raw),
            ContentKind.Tier => NameAt(oldContent.Tiers.Names, raw),
            ContentKind.Tech => NameAt(oldContent.Techs.Names, raw),
            ContentKind.Civic => NameAt(oldContent.Civics.Names, raw),
            ContentKind.Tradition => NameAt(oldContent.Traditions.Names, raw),
            ContentKind.Festival => NameAt(oldContent.Festivals.Names, raw),
            ContentKind.GreatWork => NameAt(oldContent.GreatWorks.Names, raw),
            ContentKind.Monument => NameAt(oldContent.Monuments.Names, raw),
            ContentKind.Happening => NameAt(oldContent.Happenings.Names, raw),
            ContentKind.Era => NameAt(oldContent.Eras.Names, raw),
            ContentKind.Terrain => NameAt(oldContent.Terrains.Names, raw),
            _ => throw new InvalidOperationException($"Unknown content kind {kind}."),
        };

        private string NewName(ContentKind kind, int raw) => kind switch
        {
            ContentKind.Good => NameAt(content.Goods.Names, raw),
            ContentKind.Building => NameAt(content.Buildings.Names, raw),
            ContentKind.Recipe => NameAt(content.Recipes.Names, raw),
            ContentKind.Tier => NameAt(content.Tiers.Names, raw),
            ContentKind.Tech => NameAt(content.Techs.Names, raw),
            ContentKind.Civic => NameAt(content.Civics.Names, raw),
            ContentKind.Tradition => NameAt(content.Traditions.Names, raw),
            ContentKind.Festival => NameAt(content.Festivals.Names, raw),
            ContentKind.GreatWork => NameAt(content.GreatWorks.Names, raw),
            ContentKind.Monument => NameAt(content.Monuments.Names, raw),
            ContentKind.Happening => NameAt(content.Happenings.Names, raw),
            ContentKind.Era => NameAt(content.Eras.Names, raw),
            ContentKind.Terrain => NameAt(content.Terrains.Names, raw),
            _ => throw new InvalidOperationException($"Unknown content kind {kind}."),
        };

        private int NewCount(ContentKind kind) => kind switch
        {
            ContentKind.Good => content.Goods.Count,
            ContentKind.Building => content.Buildings.Count,
            ContentKind.Recipe => content.Recipes.Count,
            ContentKind.Tier => content.Tiers.Count,
            ContentKind.Tech => content.Techs.Count,
            ContentKind.Civic => content.Civics.Count,
            ContentKind.Tradition => content.Traditions.Count,
            ContentKind.Festival => content.Festivals.Count,
            ContentKind.GreatWork => content.GreatWorks.Count,
            ContentKind.Monument => content.Monuments.Count,
            ContentKind.Happening => content.Happenings.Count,
            ContentKind.Era => content.Eras.Count,
            ContentKind.Terrain => content.Terrains.Count,
            _ => throw new InvalidOperationException($"Unknown content kind {kind}."),
        };

        private Dictionary<string, int> OldByName(ContentKind kind) =>
            _oldByName.TryGetValue(kind, out Dictionary<string, int>? map)
                ? map
                : _oldByName[kind] = NamesToIndex(kind switch
                {
                    ContentKind.Good => oldContent.Goods.Names,
                    ContentKind.Building => oldContent.Buildings.Names,
                    ContentKind.Recipe => oldContent.Recipes.Names,
                    ContentKind.Tier => oldContent.Tiers.Names,
                    ContentKind.Tech => oldContent.Techs.Names,
                    ContentKind.Civic => oldContent.Civics.Names,
                    ContentKind.Tradition => oldContent.Traditions.Names,
                    ContentKind.Festival => oldContent.Festivals.Names,
                    ContentKind.GreatWork => oldContent.GreatWorks.Names,
                    ContentKind.Monument => oldContent.Monuments.Names,
                    ContentKind.Happening => oldContent.Happenings.Names,
                    ContentKind.Era => oldContent.Eras.Names,
                    ContentKind.Terrain => oldContent.Terrains.Names,
                    _ => throw new InvalidOperationException($"Unknown content kind {kind}."),
                });

        private Dictionary<string, int> NewByName(ContentKind kind) =>
            _newByName.TryGetValue(kind, out Dictionary<string, int>? map)
                ? map
                : _newByName[kind] = NamesToIndex(kind switch
                {
                    ContentKind.Good => content.Goods.Names,
                    ContentKind.Building => content.Buildings.Names,
                    ContentKind.Recipe => content.Recipes.Names,
                    ContentKind.Tier => content.Tiers.Names,
                    ContentKind.Tech => content.Techs.Names,
                    ContentKind.Civic => content.Civics.Names,
                    ContentKind.Tradition => content.Traditions.Names,
                    ContentKind.Festival => content.Festivals.Names,
                    ContentKind.GreatWork => content.GreatWorks.Names,
                    ContentKind.Monument => content.Monuments.Names,
                    ContentKind.Happening => content.Happenings.Names,
                    ContentKind.Era => content.Eras.Names,
                    ContentKind.Terrain => content.Terrains.Names,
                    _ => throw new InvalidOperationException($"Unknown content kind {kind}."),
                });

        private static Dictionary<string, int> NamesToIndex(IReadOnlyList<string> names)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                map[names[i]] = i;
            }

            return map;
        }

        private static string NameAt(IReadOnlyList<string> names, int raw) =>
            (uint)raw < (uint)names.Count ? names[raw] : $"<out of range {raw}>";

        private static bool Bit(ulong[] words, int index) =>
            (uint)(index >> 6) < (uint)words.Length && (words[index >> 6] & (1UL << (index & 63))) != 0;

        private bool StartBit(ContentKind kind, string name)
        {
            int now = NewByName(kind)[name];
            return kind switch
            {
                ContentKind.Building => content.Buildings.Items[now].Start,
                ContentKind.Recipe => content.Recipes.Items[now].Start,
                ContentKind.Tier => content.Tiers.Items[now].Start,
                ContentKind.Tradition => content.Traditions.Items[now].Start,
                ContentKind.Festival => content.Festivals.Items[now].Start,
                _ => false,
            };
        }

        private static bool IsContentId(Type type) =>
            type.IsValueType && type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>) && i.GetGenericArguments()[0] == type);

        private static ContentKind KindOf(Type type) => type.Name switch
        {
            nameof(GoodId) => ContentKind.Good,
            nameof(BuildingTypeId) => ContentKind.Building,
            nameof(RecipeId) => ContentKind.Recipe,
            nameof(TierId) => ContentKind.Tier,
            nameof(TechId) => ContentKind.Tech,
            nameof(CivicId) => ContentKind.Civic,
            nameof(TraditionId) => ContentKind.Tradition,
            nameof(FestivalId) => ContentKind.Festival,
            nameof(GreatWorkTypeId) => ContentKind.GreatWork,
            nameof(MonumentId) => ContentKind.Monument,
            nameof(HappeningId) => ContentKind.Happening,
            nameof(EraId) => ContentKind.Era,
            nameof(TerrainId) => ContentKind.Terrain,
            _ => throw new InvalidOperationException($"The coverage walk does not know the content ID type {type.Name}; map it to a ContentKind."),
        };

        private static int RawOf(object value) => Convert.ToInt32(value.GetType().GetProperty("Value", Members)!.GetValue(value));

        private static bool IsLeaf(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(EntityId) || type == typeof(Amount) || type == typeof(Ratio)
            || type == typeof(Pcg32) || type == typeof(GameClock) || type == typeof(EntityIdAllocator);

        private static bool IsComposite(Type type) =>
            !IsLeaf(type) && !IsContentId(type) && type != typeof(EffectSource) && type != typeof(SubjectLink)
            && type != typeof(ActiveModifier) && !type.IsArray
            && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IdTable<>));

        private static bool IsDefault(object? value) =>
            value is null || (value.GetType().IsValueType && Equals(value, Activator.CreateInstance(value.GetType())));

        private static object? DefaultOf(Type type) =>
            type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
