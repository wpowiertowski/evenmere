using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>
/// A system that wants to know when buildings come and go, such as Roads (distance fields), Production (buffers),
/// Population (houses) and Logistics (jobs). The registry calls it synchronously, from inside the registry call that
/// caused the change, so by the time the call returns every listener has seen it. A listener may read other systems
/// through <see cref="SimContext.Queries"/> and write its own state, but must not add, remove or change buildings
/// itself.
/// </summary>
internal interface IBuildingLifecycleListener
{
    /// <summary>A building became active: a site finished, or a building was added already active. Not raised when a paused building resumes.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="building">A copy of the building as it now stands.</param>
    void OnActivated(SimContext ctx, BuildingInfo building);

    /// <summary>A building was removed, in any state (a site may be demolished before it ever activates). It is already gone from the registry.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="building">A copy of the building as it stood just before removal.</param>
    void OnRemoved(SimContext ctx, BuildingInfo building);

    /// <summary>A building's type changed in place.</summary>
    /// <param name="ctx">The simulation context.</param>
    /// <param name="building">A copy of the building with its new type.</param>
    /// <param name="previousType">The type it had before.</param>
    void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType);
}

/// <summary>
/// The subscription list of the building lifecycle. Listeners are called in the order they subscribed, and
/// <c>SystemRoster</c> subscribes them in pipeline order (services first, then the ticked systems in tick order), so
/// the order is deterministic and the same in every run. Implemented by the map service (M1-01) alongside the registry.
/// </summary>
internal interface IBuildingLifecycle
{
    /// <summary>Adds a listener after the ones already subscribed.</summary>
    /// <param name="listener">The listener.</param>
    void Subscribe(IBuildingLifecycleListener listener);
}
