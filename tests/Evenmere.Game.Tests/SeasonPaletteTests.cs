using Evenmere.Core.Time;
using Evenmere.Game.Rendering.Seasons;

namespace Evenmere.Game.Tests;

/// <summary>M2-24: each season has its own gentle look, and season changes blend over two game days.</summary>
public sealed class SeasonPaletteTests
{
    [Fact]
    public void Targets_AreGentleMultipliersAroundOne()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            SeasonTint tint = SeasonPalette.Target(season);
            tint.TintR.ShouldBeInRange(0.85f, 1.1f);
            tint.TintG.ShouldBeInRange(0.85f, 1.1f);
            tint.TintB.ShouldBeInRange(0.85f, 1.1f);
        }
    }

    [Fact]
    public void Targets_SpringIsFresherThanTheSummerBase()
    {
        SeasonTint spring = SeasonPalette.Target(Season.Spring);
        SeasonTint summer = SeasonPalette.Target(Season.Summer);
        summer.ShouldBe(new SeasonTint(1f, 1f, 1f, 0f, 0f, 1f, 0.90f, 0.68f));
        spring.TintG.ShouldBeGreaterThan(summer.TintG);
        spring.TintR.ShouldBeGreaterThan(summer.TintR);
        spring.TintB.ShouldBeLessThan(summer.TintB);
    }

    [Fact]
    public void Targets_WinterLightStaysWarm()
    {
        // Snow is a warm paper white, never a cool purple: red leads blue in every season's key light.
        foreach (Season season in Enum.GetValues<Season>())
        {
            SeasonTint tint = SeasonPalette.Target(season);
            tint.LightR.ShouldBeGreaterThanOrEqualTo(tint.LightB);
        }
    }

    [Fact]
    public void Targets_SnowOnlyInWinterAndBlossomOnlyInSpring()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            SeasonTint tint = SeasonPalette.Target(season);
            tint.Snow.ShouldBe(season == Season.Winter ? 1f : 0f);
            tint.Blossom.ShouldBe(season == Season.Spring ? 1f : 0f);
        }
    }

    [Fact]
    public void Sample_BeforeBlendHoldsThePreviousSeason()
    {
        // Day 0 of summer is still fully spring: the blend starts from the outgoing target.
        SeasonPalette.Sample(Season.Summer, 0).ShouldBe(SeasonPalette.Target(Season.Spring));
    }

    [Fact]
    public void Sample_AfterTwoDaysHoldsTheFullTarget()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            for (int day = SeasonPalette.BlendDays; day < GameTime.DaysPerSeason; day++)
            {
                SeasonPalette.Sample(season, day).ShouldBe(SeasonPalette.Target(season));
            }
        }
    }

    [Fact]
    public void Sample_MidBlendIsHalfway()
    {
        SeasonTint mid = SeasonPalette.Sample(Season.Winter, 1);
        SeasonTint autumn = SeasonPalette.Target(Season.Autumn);
        SeasonTint winter = SeasonPalette.Target(Season.Winter);
        mid.TintR.ShouldBe((autumn.TintR + winter.TintR) / 2f);
        mid.Snow.ShouldBe(0.5f);
        mid.Blossom.ShouldBe(0f);
    }

    [Fact]
    public void Previous_WrapsWinterToAutumn()
    {
        SeasonPalette.Previous(Season.Spring).ShouldBe(Season.Winter);
        SeasonPalette.Previous(Season.Summer).ShouldBe(Season.Spring);
        SeasonPalette.Previous(Season.Autumn).ShouldBe(Season.Summer);
        SeasonPalette.Previous(Season.Winter).ShouldBe(Season.Autumn);
    }
}
