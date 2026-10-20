using System.IO.Hashing;
using System.Text;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;

namespace Evenmere.Core.Tests.Rng;

public sealed class Pcg32Tests
{
    [Fact]
    public void NextUInt_ReferenceSeed42Stream54_MatchesPcgCDemoOutput()
    {
        Pcg32 rng = new(42, 54);

        uint[] actual = [rng.NextUInt(), rng.NextUInt(), rng.NextUInt(), rng.NextUInt(), rng.NextUInt(), rng.NextUInt()];

        actual.ShouldBe([0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu]);
    }

    [Fact]
    public void Constructor_ReferenceSeeding_SetsOddIncrementFromStream()
    {
        Pcg32 rng = new(42, 54);

        rng.Inc.ShouldBe((54UL << 1) | 1UL);
    }

    [Fact]
    public void Constructor_StreamTopBit_IsDiscardedLikeTheReference()
    {
        Pcg32 withTopBit = new(7, 0x8000_0000_0000_0005UL);
        Pcg32 without = new(7, 5);

        withTopBit.State.ShouldBe(without.State);
        withTopBit.Inc.ShouldBe(without.Inc);
    }

    [Fact]
    public void NextUInt_SameSeedAndStream_GivesSameSequence()
    {
        Pcg32 a = new(123, 456);
        Pcg32 b = new(123, 456);

        for (int i = 0; i < 1_000; i++)
        {
            a.NextUInt().ShouldBe(b.NextUInt());
        }
    }

    [Fact]
    public void NextUInt_DifferentSeedsOrStreams_GiveDifferentSequences()
    {
        Pcg32 baseline = new(1, 1);
        Pcg32 otherSeed = new(2, 1);
        Pcg32 otherStream = new(1, 2);

        uint first = baseline.NextUInt();

        otherSeed.NextUInt().ShouldNotBe(first);
        otherStream.NextUInt().ShouldNotBe(first);
    }

    [Fact]
    public void FromState_SavedStateAndInc_ContinuesTheSameSequence()
    {
        Pcg32 original = new(99, 7);
        for (int i = 0; i < 37; i++)
        {
            original.NextUInt();
        }

        var restored = Pcg32.FromState(original.State, original.Inc);

        restored.State.ShouldBe(original.State);
        restored.Inc.ShouldBe(original.Inc);
        for (int i = 0; i < 100; i++)
        {
            restored.NextUInt().ShouldBe(original.NextUInt());
        }
    }

    [Fact]
    public void FromState_EvenIncrement_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => Pcg32.FromState(1, 2));
        Should.Throw<ArgumentException>(() => Pcg32.FromState(0, 0));
    }

    [Fact]
    public void State_AfterOneDraw_AdvancesByTheLcgStep()
    {
        Pcg32 rng = new(5, 11);
        ulong before = rng.State;
        ulong inc = rng.Inc;

        rng.NextUInt();

        rng.State.ShouldBe(unchecked((before * 6364136223846793005UL) + inc));
        rng.Inc.ShouldBe(inc);
    }

    [Fact]
    public void NextInt_SevenOver100kDraws_AlwaysInRangeAndRoughlyUniform()
    {
        Pcg32 rng = new(42, 54);
        int[] counts = new int[7];

        for (int i = 0; i < 100_000; i++)
        {
            int value = rng.NextInt(7);
            value.ShouldBeInRange(0, 6);
            counts[value]++;
        }

        // The expected count is 14,285; the draws are deterministic, so the bounds are a fixed check, not a flaky one.
        foreach (int count in counts)
        {
            count.ShouldBeInRange(13_500, 15_100);
        }
    }

    [Fact]
    public void NextInt_BoundOfOne_AlwaysZero()
    {
        Pcg32 rng = new(1, 2);

        for (int i = 0; i < 1_000; i++)
        {
            rng.NextInt(1).ShouldBe(0);
        }
    }

    [Fact]
    public void NextInt_LargeBound_StaysBelowBound()
    {
        Pcg32 rng = new(3, 4);

        for (int i = 0; i < 10_000; i++)
        {
            rng.NextInt(int.MaxValue).ShouldBeInRange(0, int.MaxValue - 1);
            rng.NextInt(2_000_000_011).ShouldBeInRange(0, 2_000_000_010);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NextInt_BoundNotPositive_ThrowsArgumentOutOfRange(int bound)
    {
        Pcg32 rng = new(1, 1);

        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(bound));
    }

    [Fact]
    public void NextInt_MinAndMax_StaysWithinHalfOpenRange()
    {
        Pcg32 rng = new(8, 9);
        bool sawMin = false;
        bool sawMax = false;

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(-3, 4);
            value.ShouldBeInRange(-3, 3);
            sawMin |= value == -3;
            sawMax |= value == 3;
        }

        sawMin.ShouldBeTrue();
        sawMax.ShouldBeTrue();
    }

    [Fact]
    public void NextInt_FullIntRange_DoesNotOverflow()
    {
        Pcg32 rng = new(10, 11);
        bool sawNegative = false;
        bool sawPositive = false;

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(int.MinValue, int.MaxValue);
            value.ShouldBeLessThan(int.MaxValue);
            sawNegative |= value < 0;
            sawPositive |= value > 0;
        }

        sawNegative.ShouldBeTrue();
        sawPositive.ShouldBeTrue();
    }

    [Fact]
    public void NextInt_EmptyRangeOfOne_ReturnsMin()
    {
        Pcg32 rng = new(1, 1);

        rng.NextInt(5, 6).ShouldBe(5);
        rng.NextInt(-5, -4).ShouldBe(-5);
    }

    [Fact]
    public void NextInt_MinNotBelowMax_ThrowsArgumentOutOfRange()
    {
        Pcg32 rng = new(1, 1);

        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
    }

    [Fact]
    public void NextInt_ZeroBasedAndRangedForms_AgreeOnTheSameStream()
    {
        Pcg32 a = new(21, 22);
        Pcg32 b = new(21, 22);

        for (int i = 0; i < 1_000; i++)
        {
            a.NextInt(0, 50).ShouldBe(b.NextInt(50));
        }
    }

    [Fact]
    public void Chance_One_AlwaysSucceeds()
    {
        Pcg32 rng = new(1, 1);

        for (int i = 0; i < 10_000; i++)
        {
            rng.Chance(Ratio.One).ShouldBeTrue();
        }
    }

    [Fact]
    public void Chance_ZeroOrNegative_NeverSucceeds()
    {
        Pcg32 rng = new(1, 1);

        for (int i = 0; i < 10_000; i++)
        {
            rng.Chance(Ratio.Zero).ShouldBeFalse();
            rng.Chance(Ratio.FromPerMille(-200)).ShouldBeFalse();
        }
    }

    [Fact]
    public void Chance_AboveOne_AlwaysSucceeds()
    {
        Pcg32 rng = new(1, 1);

        for (int i = 0; i < 10_000; i++)
        {
            rng.Chance(Ratio.FromPerMille(1_400)).ShouldBeTrue();
        }
    }

    [Fact]
    public void Chance_HalfOver100kDraws_SucceedsAboutHalfTheTime()
    {
        Pcg32 rng = new(77, 78);
        int successes = 0;

        for (int i = 0; i < 100_000; i++)
        {
            if (rng.Chance(Ratio.FromPercent(50)))
            {
                successes++;
            }
        }

        successes.ShouldBeInRange(49_000, 51_000);
    }

    [Fact]
    public void Chance_AnyRatio_ConsumesTheSameNumberOfDrawsBarRejections()
    {
        Pcg32 certain = new(5, 6);
        Pcg32 impossible = new(5, 6);

        for (int i = 0; i < 1_000; i++)
        {
            certain.Chance(Ratio.One);
            impossible.Chance(Ratio.Zero);
        }

        certain.State.ShouldBe(impossible.State);
    }

    [Fact]
    public void ForSystem_NameAndSeed_UsesXxHash64OfUtf8NameAsStream()
    {
        ulong stream = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes("production"));

        Pcg32 expected = new(42, stream);
        var actual = Pcg32.ForSystem(42, "production");

        actual.State.ShouldBe(expected.State);
        actual.Inc.ShouldBe(expected.Inc);
    }

    [Fact]
    public void ForSystem_ProductionSeed42_MatchesGoldenOutput()
    {
        // Locks the derivation: it is part of the save format, so a change here changes every world's randomness.
        var rng = Pcg32.ForSystem(42, "production");

        uint[] actual = [rng.NextUInt(), rng.NextUInt(), rng.NextUInt()];

        actual.ShouldBe([136427322u, 1703663052u, 3852198227u]);
    }

    [Fact]
    public void ForSystem_SameInputs_GiveSameSequence()
    {
        var a = Pcg32.ForSystem(1234, "needs");
        var b = Pcg32.ForSystem(1234, "needs");

        for (int i = 0; i < 100; i++)
        {
            a.NextUInt().ShouldBe(b.NextUInt());
        }
    }

    [Fact]
    public void ForSystem_DifferentNames_GiveDifferentStreams()
    {
        string[] names = ["map", "roads", "production", "logistics", "needs", "population", "economy", "research", "culture"];
        HashSet<ulong> increments = [];

        foreach (string name in names)
        {
            increments.Add(Pcg32.ForSystem(7, name).Inc).ShouldBeTrue();
        }
    }

    [Fact]
    public void ForSystem_DifferentWorldSeeds_GiveDifferentSequences()
    {
        var a = Pcg32.ForSystem(1, "needs");
        var b = Pcg32.ForSystem(2, "needs");

        a.Inc.ShouldBe(b.Inc);
        a.NextUInt().ShouldNotBe(b.NextUInt());
    }

    [Fact]
    public void ForSystem_NonAsciiAndLongNames_AreHashedFromUtf8()
    {
        string accented = "café";
        string longName = new('x', 1_000);

        Pcg32.ForSystem(1, accented).Inc.ShouldBe(new Pcg32(1, XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(accented))).Inc);
        Pcg32.ForSystem(1, longName).Inc.ShouldBe(new Pcg32(1, XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(longName))).Inc);
    }

    [Fact]
    public void ForSystem_NullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Pcg32.ForSystem(1, null!));
    }
}
