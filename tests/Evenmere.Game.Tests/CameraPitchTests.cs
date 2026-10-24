using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M1-34: the zoom-dependent pitch blends from the street-level default to the island-view default.</summary>
public sealed class CameraPitchTests
{
    private const float StreetDegrees = 30f;
    private const float IslandDegrees = 50f;
    private const float MinZoom = 8f;
    private const float MaxZoom = 220f;
    private const float InitialZoom = 70f;
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Pitch_AtMinZoom_IsStreetPitch()
    {
        CameraMath.PitchRadians(MinZoom, MinZoom, MaxZoom, StreetDegrees, IslandDegrees)
            .ShouldBe(StreetDegrees * (MathF.PI / 180f), Tolerance);
    }

    [Fact]
    public void Pitch_AtMaxZoom_IsIslandPitch()
    {
        CameraMath.PitchRadians(MaxZoom, MinZoom, MaxZoom, StreetDegrees, IslandDegrees)
            .ShouldBe(IslandDegrees * (MathF.PI / 180f), Tolerance);
    }

    [Fact]
    public void Pitch_AtInitialZoom_SitsNearFortyFourDegrees()
    {
        float pitch = CameraMath.PitchRadians(InitialZoom, MinZoom, MaxZoom, StreetDegrees, IslandDegrees);
        float degrees = pitch * (180f / MathF.PI);

        // Log-space fraction ln(70/8)/ln(220/8) ≈ 0.654, eased by the smooth step to ≈ 0.724.
        degrees.ShouldBe(44.5f, 0.1f);
    }

    [Fact]
    public void Pitch_BelowMinZoom_ClampsToStreetPitch()
    {
        CameraMath.PitchRadians(MinZoom / 2f, MinZoom, MaxZoom, StreetDegrees, IslandDegrees)
            .ShouldBe(StreetDegrees * (MathF.PI / 180f), Tolerance);
    }

    [Fact]
    public void Pitch_AboveMaxZoom_ClampsToIslandPitch()
    {
        CameraMath.PitchRadians(MaxZoom * 2f, MinZoom, MaxZoom, StreetDegrees, IslandDegrees)
            .ShouldBe(IslandDegrees * (MathF.PI / 180f), Tolerance);
    }

    [Fact]
    public void Pitch_BetweenEnds_RisesMonotonically()
    {
        float previous = float.NegativeInfinity;
        for (int i = 0; i <= 20; i++)
        {
            float zoom = MinZoom * MathF.Pow(MaxZoom / MinZoom, i / 20f);
            float pitch = CameraMath.PitchRadians(zoom, MinZoom, MaxZoom, StreetDegrees, IslandDegrees);
            pitch.ShouldBeGreaterThan(previous);
            previous = pitch;
        }
    }
}
