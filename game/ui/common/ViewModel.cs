namespace Evenmere.Game.Ui.Common;

/// <summary>
/// The base of every UI view-model (architecture.md, "UI"). A view-model reads the simulation through its queries, turns the
/// numbers into plain C# properties (already formatted text, enums, small records), and tells its nodes when to re-read them
/// through <see cref="Refreshed"/>. It holds no simulation state of its own beyond what it shows.
/// <para>
/// Reading queries every frame would waste the frame and make numbers flicker, so a view-model refreshes at most
/// <c>1 / <see cref="MinRefreshIntervalSeconds"/></c> = 4 times a second. Two things ask for a refresh: <see cref="MarkDirty"/>,
/// which the owning node calls when <c>SimHost.EventsDrained</c> fires, and a slow timer (<see cref="PollIntervalSeconds"/>) that
/// catches changes no event announces, such as a speed change while paused. The base class has no Godot types, so
/// the throttle is testable on its own: the owning node calls <see cref="Advance"/> from <c>_Process</c>.
/// </para>
/// </summary>
public abstract class ViewModel
{
    /// <summary>The shortest time between two refreshes: a quarter of a second, so at most four per second.</summary>
    public const double MinRefreshIntervalSeconds = 0.25;

    private const double DefaultPollIntervalSeconds = 1.0;

    private double _secondsSinceRefresh;
    private bool _hasRefreshed;

    /// <summary>Raised after each refresh, once the properties hold the new values.</summary>
    public event Action? Refreshed;

    /// <summary>
    /// Gets how long the view-model waits before refreshing on its own when nothing marked it dirty. It is never shorter
    /// than <see cref="MinRefreshIntervalSeconds"/>.
    /// </summary>
    public virtual double PollIntervalSeconds => DefaultPollIntervalSeconds;

    /// <summary>Gets a value indicating whether something asked for a refresh that has not happened yet.</summary>
    public bool IsDirty { get; private set; } = true;

    /// <summary>Asks for a refresh at the next opportunity, which is never sooner than <see cref="MinRefreshIntervalSeconds"/> after the last one.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>
    /// Advances the throttle by one frame and refreshes if one is due. The first call always refreshes, so a new view shows
    /// real numbers on its first frame.
    /// </summary>
    /// <param name="deltaSeconds">The real time since the last call, in seconds; not negative.</param>
    /// <returns><see langword="true"/> if the view-model refreshed during this call.</returns>
    public bool Advance(double deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        _secondsSinceRefresh += deltaSeconds;

        if (_hasRefreshed)
        {
            if (_secondsSinceRefresh < MinRefreshIntervalSeconds)
            {
                return false;
            }

            if (!IsDirty && _secondsSinceRefresh < Math.Max(PollIntervalSeconds, MinRefreshIntervalSeconds))
            {
                return false;
            }
        }

        _secondsSinceRefresh = 0;
        _hasRefreshed = true;
        IsDirty = false;

        OnRefresh();
        Refreshed?.Invoke();
        return true;
    }

    /// <summary>Reads the queries and updates the properties. Called by <see cref="Advance"/>, never more than four times a second.</summary>
    protected abstract void OnRefresh();
}
