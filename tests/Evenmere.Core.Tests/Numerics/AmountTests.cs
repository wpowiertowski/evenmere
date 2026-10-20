using Evenmere.Core.Numerics;

namespace Evenmere.Core.Tests.Numerics;

public sealed class AmountTests
{
    [Fact]
    public void Default_IsZero()
    {
        default(Amount).ShouldBe(Amount.Zero);
        Amount.Zero.Milli.ShouldBe(0);
    }

    [Fact]
    public void FromUnits_WholeUnits_ScalesToMilli()
    {
        Amount.FromUnits(12).Milli.ShouldBe(12_000);
        Amount.FromUnits(-3).Milli.ShouldBe(-3_000);
    }

    [Fact]
    public void FromMilli_RawValue_KeepsMilli()
    {
        Amount.FromMilli(1).Milli.ShouldBe(1);
        Amount.FromMilli(-2_345).Milli.ShouldBe(-2_345);
    }

    [Theory]
    [InlineData("0.001", 1L)]
    [InlineData("12.5", 12_500L)]
    [InlineData("-3.25", -3_250L)]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("12", 12_000L)]
    [InlineData("12.50", 12_500L)]
    [InlineData("007.5", 7_500L)]
    [InlineData("-0.5", -500L)]
    [InlineData("0.050", 50L)]
    [InlineData("1000000", 1_000_000_000L)]
    [InlineData("9223372036854775.807", long.MaxValue)]
    [InlineData("-9223372036854775.808", long.MinValue)]
    public void Parse_ValidText_GivesMilli(string text, long expectedMilli)
    {
        Amount.Parse(text).Milli.ShouldBe(expectedMilli);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("12.5")]
    [InlineData("-3.25")]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("-0.5")]
    [InlineData("0.05")]
    [InlineData("100.125")]
    [InlineData("9223372036854775.807")]
    [InlineData("-9223372036854775.808")]
    public void ToString_ParsedText_RoundTripsExactly(string canonical)
    {
        Amount.Parse(canonical).ToString().ShouldBe(canonical);
    }

    [Theory]
    [InlineData(1L, "0.001")]
    [InlineData(-1L, "-0.001")]
    [InlineData(12_500L, "12.5")]
    [InlineData(12_000L, "12")]
    [InlineData(-3_250L, "-3.25")]
    [InlineData(50L, "0.05")]
    [InlineData(1_050L, "1.05")]
    [InlineData(0L, "0")]
    public void ToString_Milli_FormatsPlainDecimal(long milli, string expected)
    {
        Amount.FromMilli(milli).ToString().ShouldBe(expected);
    }

    [Fact]
    public void ToString_EveryMilliInOneUnit_RoundTripsThroughParse()
    {
        for (long milli = -2_000; milli <= 2_000; milli++)
        {
            var amount = Amount.FromMilli(milli);
            Amount.Parse(amount.ToString()).ShouldBe(amount);
        }
    }

    [Theory]
    [InlineData("1.2345")]
    [InlineData("0.0001")]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("-.5")]
    [InlineData("1.2.3")]
    [InlineData("--1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    [InlineData("1,000.5")]
    [InlineData("١")]
    public void Parse_MalformedText_ThrowsFormatException(string text)
    {
        Should.Throw<FormatException>(() => Amount.Parse(text));
    }

    [Fact]
    public void Parse_NullText_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Amount.Parse(null!));
    }

    [Theory]
    [InlineData("9223372036854775.808")]
    [InlineData("-9223372036854775.809")]
    [InlineData("99999999999999999999")]
    public void Parse_OutOfRangeValue_ThrowsOverflowException(string text)
    {
        Should.Throw<OverflowException>(() => Amount.Parse(text));
    }

    [Theory]
    [InlineData(1L, 500, 0L)]
    [InlineData(-1L, 500, -1L)]
    [InlineData(7L, 333, 2L)]
    [InlineData(-7L, 333, -3L)]
    [InlineData(-1_000L, 500, -500L)]
    [InlineData(1_000L, 1_400, 1_400L)]
    [InlineData(1_000L, 1_000, 1_000L)]
    [InlineData(1_000L, 0, 0L)]
    [InlineData(-999L, 1_000, -999L)]
    [InlineData(999L, -1, -1L)]
    [InlineData(-999L, -1, 0L)]
    public void MultiplyBy_NegativeOrFractionalResult_RoundsTowardNegativeInfinity(long milli, int perMille, long expectedMilli)
    {
        Amount.FromMilli(milli).MultiplyBy(Ratio.FromPerMille(perMille)).Milli.ShouldBe(expectedMilli);
    }

    [Fact]
    public void MultiplyBy_ProductBeyondLongButResultFits_DoesNotOverflow()
    {
        var large = Amount.FromMilli(9_000_000_000_000_000);

        large.MultiplyBy(Ratio.FromPerMille(1_500)).Milli.ShouldBe(13_500_000_000_000_000);
    }

    [Fact]
    public void MultiplyBy_ResultBeyondLong_ThrowsOverflowException()
    {
        var large = Amount.FromMilli(long.MaxValue);

        Should.Throw<OverflowException>(() => large.MultiplyBy(Ratio.FromPerMille(1_001)));
    }

    [Theory]
    [InlineData(10L, 1L, 3L, 3L)]
    [InlineData(-10L, 1L, 3L, -4L)]
    [InlineData(10L, -1L, 3L, -4L)]
    [InlineData(10L, 1L, -3L, -4L)]
    [InlineData(-10L, -1L, 3L, 3L)]
    [InlineData(-10L, 1L, -3L, 3L)]
    [InlineData(-9L, 1L, 3L, -3L)]
    [InlineData(9L, 1L, -3L, -3L)]
    [InlineData(7L, 3L, 4L, 5L)]
    [InlineData(-7L, 3L, 4L, -6L)]
    [InlineData(7L, 0L, 5L, 0L)]
    public void MulDiv_AnySigns_RoundsTowardNegativeInfinity(long milli, long numerator, long denominator, long expectedMilli)
    {
        Amount.FromMilli(milli).MulDiv(numerator, denominator).Milli.ShouldBe(expectedMilli);
    }

    [Fact]
    public void MulDiv_ZeroDenominator_ThrowsDivideByZeroException()
    {
        Should.Throw<DivideByZeroException>(() => Amount.FromMilli(5).MulDiv(1, 0));
    }

    [Fact]
    public void MulDiv_IntermediateProductBeyondLong_StaysExact()
    {
        Amount.FromMilli(long.MaxValue).MulDiv(10, 10).Milli.ShouldBe(long.MaxValue);
        Amount.FromMilli(long.MinValue).MulDiv(3, 3).Milli.ShouldBe(long.MinValue);
    }

    [Fact]
    public void MulDiv_ResultBeyondLong_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MaxValue).MulDiv(2, 1));
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MinValue).MulDiv(-1, 1));
    }

    [Fact]
    public void Add_SumBeyondLong_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MaxValue) + Amount.FromMilli(1));
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MinValue) + Amount.FromMilli(-1));
    }

    [Fact]
    public void Subtract_DifferenceBeyondLong_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MinValue) - Amount.FromMilli(1));
        Should.Throw<OverflowException>(() => Amount.FromMilli(long.MaxValue) - Amount.FromMilli(-1));
    }

    [Fact]
    public void Negate_SmallestAmount_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => -Amount.FromMilli(long.MinValue));
    }

    [Fact]
    public void FromUnits_UnitsBeyondRange_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Amount.FromUnits(long.MaxValue));
        Should.Throw<OverflowException>(() => Amount.FromUnits(long.MinValue));
        Should.Throw<OverflowException>(() => Amount.FromUnits(9_223_372_036_854_776));
    }

    [Fact]
    public void FromUnits_LargestWholeUnits_Succeeds()
    {
        Amount.FromUnits(9_223_372_036_854_775).Milli.ShouldBe(9_223_372_036_854_775_000);
    }

    [Fact]
    public void Operators_AddSubtractNegate_ComputeExactly()
    {
        var a = Amount.Parse("12.5");
        var b = Amount.Parse("0.25");

        (a + b).ShouldBe(Amount.Parse("12.75"));
        (a - b).ShouldBe(Amount.Parse("12.25"));
        (b - a).ShouldBe(Amount.Parse("-12.25"));
        (-a).ShouldBe(Amount.Parse("-12.5"));
    }

    [Fact]
    public void Operators_Comparison_OrdersByValue()
    {
        var small = Amount.FromMilli(-1);
        var large = Amount.FromMilli(1);

        (small < large).ShouldBeTrue();
        (large > small).ShouldBeTrue();
        (small <= Amount.FromMilli(-1)).ShouldBeTrue();
        (large >= Amount.FromMilli(1)).ShouldBeTrue();
        (small >= large).ShouldBeFalse();
        (large <= small).ShouldBeFalse();
        (large < small).ShouldBeFalse();
        (small > large).ShouldBeFalse();
        small.CompareTo(large).ShouldBeLessThan(0);
        large.CompareTo(small).ShouldBeGreaterThan(0);
        small.CompareTo(small).ShouldBe(0);
    }

    [Fact]
    public void Equality_SameMilli_IsEqual()
    {
        Amount.FromUnits(2).ShouldBe(Amount.FromMilli(2_000));
        (Amount.FromUnits(2) == Amount.FromMilli(2_000)).ShouldBeTrue();
        (Amount.FromUnits(2) != Amount.FromMilli(2_001)).ShouldBeTrue();
        Amount.FromUnits(2).GetHashCode().ShouldBe(Amount.FromMilli(2_000).GetHashCode());
    }

    [Fact]
    public void MinMax_TwoAmounts_PickSmallerAndLarger()
    {
        var a = Amount.FromMilli(-5);
        var b = Amount.FromMilli(7);

        Amount.Min(a, b).ShouldBe(a);
        Amount.Min(b, a).ShouldBe(a);
        Amount.Max(a, b).ShouldBe(b);
        Amount.Max(b, a).ShouldBe(b);
        Amount.Min(a, a).ShouldBe(a);
    }

    [Theory]
    [InlineData(12_500L, 12L)]
    [InlineData(12_000L, 12L)]
    [InlineData(999L, 0L)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, -1L)]
    [InlineData(-999L, -1L)]
    [InlineData(-1_000L, -1L)]
    [InlineData(-3_250L, -4L)]
    [InlineData(long.MaxValue, 9_223_372_036_854_775L)]
    [InlineData(long.MinValue, -9_223_372_036_854_776L)]
    public void WholeUnits_Milli_FloorsToUnits(long milli, long expectedUnits)
    {
        Amount.FromMilli(milli).WholeUnits.ShouldBe(expectedUnits);
    }
}
