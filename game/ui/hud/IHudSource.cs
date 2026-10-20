using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// Everything the HUD view-model reads from the game, in one small interface, so the view-model has no Godot types and
/// tests can feed it numbers. <see cref="SimHostHudSource"/> is the real one. A <see langword="null"/> from a method that
/// returns a nullable means "no query answers this yet", and the HUD shows a dash.
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

    /// <summary>Gets the number of residents of a tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The residents, or <see langword="null"/> while no query provides them.</returns>
    long? Residents(TierId tier);

    /// <summary>Gets the knowledge the island gains per game day.</summary>
    /// <returns>The rate, or <see langword="null"/> while no query provides it.</returns>
    Amount? KnowledgePerDay();

    /// <summary>Gets the culture the island gains per game day.</summary>
    /// <returns>The rate, or <see langword="null"/> while no query provides it.</returns>
    Amount? CulturePerDay();
}
