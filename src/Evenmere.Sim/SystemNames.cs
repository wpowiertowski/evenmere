namespace Evenmere.Sim;

/// <summary>
/// The stable names of the systems. A name seeds the system's random stream through <c>Pcg32.ForSystem</c>, so it is
/// part of the save format: changing one changes the randomness of every existing world.
/// </summary>
public static class SystemNames
{
    /// <summary>The map service.</summary>
    public const string Map = "map";

    /// <summary>The roads service.</summary>
    public const string Roads = "roads";

    /// <summary>The storage service.</summary>
    public const string Storage = "storage";

    /// <summary>The modifiers service.</summary>
    public const string Modifiers = "modifiers";

    /// <summary>The construction system.</summary>
    public const string Construction = "construction";

    /// <summary>The workforce system.</summary>
    public const string Workforce = "workforce";

    /// <summary>The production system.</summary>
    public const string Production = "production";

    /// <summary>The logistics system.</summary>
    public const string Logistics = "logistics";

    /// <summary>The exploration system.</summary>
    public const string Exploration = "exploration";

    /// <summary>The needs system.</summary>
    public const string Needs = "needs";

    /// <summary>The population system.</summary>
    public const string Population = "population";

    /// <summary>The coin economy system.</summary>
    public const string Economy = "economy";

    /// <summary>The research system.</summary>
    public const string Research = "research";

    /// <summary>The culture system.</summary>
    public const string Culture = "culture";

    /// <summary>The progression system.</summary>
    public const string Progression = "progression";

    /// <summary>The happenings system.</summary>
    public const string Happenings = "happenings";

    /// <summary>The advisors system.</summary>
    public const string Advisors = "advisors";

    /// <summary>The statistics system.</summary>
    public const string Statistics = "statistics";

    /// <summary>The number of systems and services, which is the number of <see cref="SystemId"/> members.</summary>
    public const int Count = 18;

    // Indexed by SystemId; a test asserts it matches the enum member for member.
    private static readonly string[] _byId =
    [
        Map, Roads, Storage, Modifiers, Construction, Workforce, Production, Logistics, Exploration,
        Needs, Population, Economy, Research, Culture, Progression, Happenings, Advisors, Statistics,
    ];

    /// <summary>Gets the stable name of a system.</summary>
    /// <param name="id">The system.</param>
    /// <returns>The name, such as <c>"production"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a defined system.</exception>
    public static string Of(SystemId id)
    {
        int index = (int)id;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(id));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _byId.Length, nameof(id));
        return _byId[index];
    }
}
