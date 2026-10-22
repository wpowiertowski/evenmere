using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// Everything the HUD view-model reads from the game, in one small interface, so the view-model has no Godot types and
/// tests can feed it numbers. <see cref="SimHostHudSource"/> is the real one. A <see langword="null"/> from a method that
/// returns a nullable means no simulation is running, and the HUD shows a dash.
/// </summary>
public interface IHudSource
{
    /// <summary>Gets a value indicating whether a simulation is running. When it is not, nothing else here may be read.</summary>
    bool IsRunning { get; }

    /// <summary>Gets the calendar at the current tick.</summary>
    GameClock Clock { get; }

    /// <summary>Gets the speed the host is running at.</summary>
    SimSpeed Speed { get; }

    /// <summary>Gets how much of a good the island holds.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The stock in milli-units.</returns>
    Amount Stock(GoodId good);

    /// <summary>Gets how many residents live in the houses of a tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The residents, or <see langword="null"/> while no simulation is running.</returns>
    long? Residents(TierId tier);

    /// <summary>Tests whether a population tier is available to houses.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    bool IsTierUnlocked(TierId tier);

    /// <summary>Gets how many workers a tier's houses supply.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The workers that tier's houses supply; 0 when it has no residents.</returns>
    int WorkforceSupply(TierId tier);

    /// <summary>Gets how many workers a tier's active, unpaused workplaces ask for.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The workers wanted.</returns>
    int WorkforceDemand(TierId tier);

    /// <summary>Gets the knowledge the island gains per game day.</summary>
    /// <returns>The rate, or <see langword="null"/> while no simulation is running.</returns>
    Amount? KnowledgePerDay();

    /// <summary>Gets the culture the island gains per game day.</summary>
    /// <returns>The culture, or <see langword="null"/> while no simulation is running.</returns>
    Amount? CulturePerDay();
}
