using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>Implements <see cref="IAdvisorsQueries"/> over the advisors state: the showing records, ordered by priority, then age.</summary>
internal sealed class AdvisorsQueries : IAdvisorsQueries
{
    private readonly AdvisorsState _state;

    /// <summary>Initializes a new instance of the <see cref="AdvisorsQueries"/> class.</summary>
    /// <param name="state">The advisors state to read.</param>
    public AdvisorsQueries(AdvisorsState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
    }

    /// <inheritdoc />
    public ReadOnlySpan<AdviceView> Active()
    {
        int count = 0;
        foreach (AdviceRecord record in _state.Records)
        {
            if (record.Showing)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        var views = new AdviceView[count];
        int next = 0;
        foreach (AdviceRecord record in _state.Records)
        {
            if (record.Showing)
            {
                views[next++] = new AdviceView(
                    record.Id, record.Kind, record.Subject, record.FirstSeenTick, record.LastSeenTick);
            }
        }

        Array.Sort(views, static (left, right) =>
        {
            int priority = AdvicePriority.Of(left.Kind).CompareTo(AdvicePriority.Of(right.Kind));
            if (priority != 0)
            {
                return priority;
            }

            int firstSeen = left.FirstSeenTick.CompareTo(right.FirstSeenTick);
            if (firstSeen != 0)
            {
                return firstSeen;
            }

            return left.Id.CompareTo(right.Id);
        });
        return views;
    }
}
