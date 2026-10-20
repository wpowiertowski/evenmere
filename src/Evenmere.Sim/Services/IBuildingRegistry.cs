using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>
/// The write side of the building registry: the one place buildings are added, removed, changed and given a state.
/// Implemented by the map service (M1-01), which keeps the registry and the occupancy grid consistent, marks the chunks
/// dirty and tells the <see cref="IBuildingLifecycle"/> listeners. The read side is <c>IMapQueries</c>. Roads are not
/// buildings: they live in the road layer.
/// </summary>
internal interface IBuildingRegistry
{
    /// <summary>
    /// Registers a new building and allocates its entity ID. A building added in the <see cref="BuildingState.Active"/>
    /// state raises <see cref="IBuildingLifecycleListener.OnActivated"/>; a site does not, until it becomes active.
    /// </summary>
    /// <param name="type">The building type.</param>
    /// <param name="x">The column of the footprint's top-left tile after rotation.</param>
    /// <param name="y">The row of the footprint's top-left tile after rotation.</param>
    /// <param name="rotation">The orientation; the footprint's width and height swap on a quarter or three-quarter turn.</param>
    /// <param name="state">The state to register it in: <see cref="BuildingState.Site"/> for a placement, <see cref="BuildingState.Active"/> for a pre-placed building, <see cref="BuildingState.Ruin"/> when loading a save whose type is gone.</param>
    /// <returns>The new building's entity ID.</returns>
    /// <exception cref="ArgumentException">The footprint leaves the map or overlaps another building. Callers validate placement first.</exception>
    EntityId Add(BuildingTypeId type, int x, int y, Rotation rotation, BuildingState state);

    /// <summary>
    /// Removes a building and frees its footprint. Raises <see cref="IBuildingLifecycleListener.OnRemoved"/> whatever the
    /// building's state was.
    /// </summary>
    /// <param name="building">The building's entity ID.</param>
    /// <returns><see langword="true"/> if the building existed.</returns>
    bool Remove(EntityId building);

    /// <summary>
    /// Changes a building's state. Moving a <see cref="BuildingState.Site"/> to <see cref="BuildingState.Active"/> raises
    /// <see cref="IBuildingLifecycleListener.OnActivated"/>; moving between Active and Paused raises nothing. Setting
    /// <see cref="BuildingState.Ruin"/> is reserved for save loading and raises nothing.
    /// </summary>
    /// <param name="building">The building's entity ID.</param>
    /// <param name="state">The new state.</param>
    /// <exception cref="KeyNotFoundException">The building does not exist.</exception>
    void SetState(EntityId building, BuildingState state);

    /// <summary>Changes a building's type in place (an upgrade, or a house changing tier where the content uses distinct house types). The footprint must stay the same. Raises <see cref="IBuildingLifecycleListener.OnTypeChanged"/>.</summary>
    /// <param name="building">The building's entity ID.</param>
    /// <param name="type">The new building type.</param>
    /// <exception cref="KeyNotFoundException">The building does not exist.</exception>
    void SetType(EntityId building, BuildingTypeId type);

    /// <summary>Sets a site's construction stage (see <see cref="BuildingInfo.Stage"/>). Raises nothing.</summary>
    /// <param name="building">The building's entity ID.</param>
    /// <param name="stage">The number of build steps complete.</param>
    /// <exception cref="KeyNotFoundException">The building does not exist.</exception>
    void SetStage(EntityId building, int stage);
}
