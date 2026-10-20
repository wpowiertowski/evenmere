namespace Evenmere.Sim;

/// <summary>The choices that define a new world. Later tasks add the island, the starting site and similar settings.</summary>
public sealed record NewGameOptions
{
    /// <summary>Gets the world seed. Every system's random stream is derived from it, so one seed gives one world.</summary>
    public ulong Seed { get; init; }
}
