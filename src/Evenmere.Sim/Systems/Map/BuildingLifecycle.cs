using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// The subscription list of the building lifecycle. Listeners are called synchronously, in the order they subscribed, from
/// inside the registry call that caused the change. While they run, the registry refuses further changes
/// (<see cref="IsDispatching"/>), so a listener cannot reorder events or leave the registry half-updated for the next one.
/// </summary>
internal sealed class BuildingLifecycle : IBuildingLifecycle, IBindsContext
{
    private readonly List<IBuildingLifecycleListener> _listeners = [];
    private SimContext? _context;

    /// <summary>Gets a value indicating whether a listener is being called right now.</summary>
    public bool IsDispatching { get; private set; }

    /// <inheritdoc />
    public void Subscribe(IBuildingLifecycleListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (IsDispatching)
        {
            throw new InvalidOperationException("A lifecycle listener cannot subscribe another one while notifications are being sent.");
        }

        if (_listeners.Contains(listener))
        {
            throw new ArgumentException("The listener is already subscribed.", nameof(listener));
        }

        _listeners.Add(listener);
    }

    /// <inheritdoc />
    public void Bind(SimContext context) => _context = context;

    /// <summary>Tells every listener that a building became active.</summary>
    /// <param name="building">The building as it now stands.</param>
    public void NotifyActivated(BuildingInfo building)
    {
        if (_listeners.Count == 0)
        {
            return;
        }

        SimContext ctx = RequireContext();
        IsDispatching = true;
        try
        {
            for (int i = 0; i < _listeners.Count; i++)
            {
                _listeners[i].OnActivated(ctx, building);
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    /// <summary>Tells every listener that a building was removed.</summary>
    /// <param name="building">The building as it stood just before removal.</param>
    public void NotifyRemoved(BuildingInfo building)
    {
        if (_listeners.Count == 0)
        {
            return;
        }

        SimContext ctx = RequireContext();
        IsDispatching = true;
        try
        {
            for (int i = 0; i < _listeners.Count; i++)
            {
                _listeners[i].OnRemoved(ctx, building);
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    /// <summary>Tells every listener that a building changed type.</summary>
    /// <param name="building">The building with its new type.</param>
    /// <param name="previousType">The type it had before.</param>
    public void NotifyTypeChanged(BuildingInfo building, BuildingTypeId previousType)
    {
        if (_listeners.Count == 0)
        {
            return;
        }

        SimContext ctx = RequireContext();
        IsDispatching = true;
        try
        {
            for (int i = 0; i < _listeners.Count; i++)
            {
                _listeners[i].OnTypeChanged(ctx, building, previousType);
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    private SimContext RequireContext() =>
        _context ?? throw new InvalidOperationException("The building lifecycle has no simulation context yet; SimContext binds it when it is created.");
}
