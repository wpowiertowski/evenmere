using Evenmere.Core.Numerics;

namespace Evenmere.Core.Tests.Numerics;

public sealed class RatioTests
{
    [Fact]
    public void Constants_OneAndZero_HaveExpectedPerMille()
    {
        Ratio.One.PerMille.ShouldBe(1_000);
        Ratio.Zero.PerMille.ShouldBe(0);
        default(Ratio).ShouldBe(Ratio.Zero);
    }

    [Fact]
    public void FromPerMille_RawValue_KeepsPerMille()
    {
        Ratio.FromPerMille(1_234).PerMille.ShouldBe(1_234);
        Ratio.FromPerMille(-5).PerMille.ShouldBe(-5);
    }

    [Theory]
    [InlineData(100, 1_000)]
    [InlineData(150, 1_500)]
    [InlineData(0, 0)]
    [InlineData(7, 70)]
    [InlineData(-5, -50)]
    public void FromPercent_Percent_ScalesByTen(int percent, int expectedPerMille)
    {
        Ratio.FromPercent(percent).PerMille.ShouldBe(expectedPerMille);
    }

    [Fact]
    public void FromPercent_HugePercent_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Ratio.FromPercent(int.MaxValue));
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(10, 1_100)]
    [InlineData(-25, 750)]
    [InlineData(-100, 0)]
    [InlineData(-150, 0)]
    [InlineData(-1_000_000, 0)]
    public void FromBonusPercent_SumOfPercents_AddsToOneAndClampsAtZero(int sum, int expectedPerMille)
    {
        Ratio.FromBonusPercent(sum).PerMille.ShouldBe(expectedPerMille);
    }

    [Fact]
    public void FromBonusPercent_TwoTwentyPercentBonuses_GivesFourteenHundredWithoutCompounding()
    {
        Ratio.FromBonusPercent(20 + 20).PerMille.ShouldBe(1_400);
    }

    [Fact]
    public void FromBonusPercent_HugeSum_ThrowsOverflowException()
    {
        Should.Throw<OverflowException>(() => Ratio.FromBonusPercent(int.MaxValue));
    }

    [Fact]
    public void Apply_SummedBonus_DiffersFromCompoundingTwice()
    {
        var hundred = Amount.FromUnits(100);

        Amount summed = Ratio.FromBonusPercent(20 + 20).Apply(hundred);
        Amount compounded = Ratio.FromBonusPercent(20).Apply(Ratio.FromBonusPercent(20).Apply(hundred));

        summed.ShouldBe(Amount.FromUnits(140));
        compounded.ShouldBe(Amount.FromUnits(144));
    }

    [Fact]
    public void Apply_One_LeavesAmountUnchanged()
    {
        Ratio.One.Apply(Amount.FromMilli(-12_345)).ShouldBe(Amount.FromMilli(-12_345));
    }

    [Fact]
    public void Apply_Amount_MatchesMultiplyByAndRoundsDown()
    {
        var amount = Amount.FromMilli(-7);
        var ratio = Ratio.FromPerMille(333);

        ratio.Apply(amount).ShouldBe(amount.MultiplyBy(ratio));
        ratio.Apply(amount).Milli.ShouldBe(-3);
    }

    [Fact]
    public void Apply_Zero_GivesZero()
    {
        Ratio.Zero.Apply(Amount.FromUnits(50)).ShouldBe(Amount.Zero);
    }
}
