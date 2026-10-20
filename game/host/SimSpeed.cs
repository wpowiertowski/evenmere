namespace Evenmere.Game.Host;

/// <summary>How fast the host feeds game time to the simulation. <see cref="X16"/> exists only in debug builds.</summary>
public enum SimSpeed
{
    /// <summary>No ticks run.</summary>
    Paused = 0,

    /// <summary>One tick every 0.25 s of real time.</summary>
    X1 = 1,

    /// <summary>Twice real time.</summary>
    X2 = 2,

    /// <summary>Four times real time.</summary>
    X4 = 3,

    /// <summary>Sixteen times real time; debug builds only.</summary>
    X16 = 4,
}

/// <summary>Numbers and names for <see cref="SimSpeed"/>, kept apart from the enum so the enum values stay plain.</summary>
public static class SimSpeeds
{
    /// <summary>Gets how many seconds of game time pass per second of real time. It is a whole number so scaling never adds rounding error.</summary>
    /// <param name="speed">The speed.</param>
    /// <returns>The multiplier; zero when paused.</returns>
    public static int Multiplier(this SimSpeed speed) => speed switch
    {
        SimSpeed.Paused => 0,
        SimSpeed.X1 => 1,
        SimSpeed.X2 => 2,
        SimSpeed.X4 => 4,
        SimSpeed.X16 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown simulation speed."),
    };

    /// <summary>Gets the short name shown in the HUD.</summary>
    /// <param name="speed">The speed.</param>
    /// <returns>The label, such as <c>2×</c>.</returns>
    public static string Label(this SimSpeed speed) => speed switch
    {
        SimSpeed.Paused => "Paused",
        SimSpeed.X1 => "1×",
        SimSpeed.X2 => "2×",
        SimSpeed.X4 => "4×",
        SimSpeed.X16 => "16×",
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown simulation speed."),
    };
}
