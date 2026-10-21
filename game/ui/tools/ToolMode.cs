namespace Evenmere.Game.Ui.Tools;

/// <summary>
/// Which placement tool the <see cref="ToolController"/> is running. The build menu (M1-23) and the inspector (M1-24)
/// drive it through <c>BeginBuild</c>, <c>BeginRoad</c>, <c>BeginDemolish</c> and <c>Cancel</c>; the controller raises
/// <c>ToolChanged</c> whenever the mode changes and <c>BuildingClicked</c> when the player clicks a building while idle
/// (M1-24 opens the inspector from that event).
/// </summary>
public enum ToolMode
{
    /// <summary>No tool is active: clicks select buildings instead of placing.</summary>
    Idle = 0,

    /// <summary>Placing a building type: the ghost follows the pointer and a click enqueues <c>PlaceBuilding</c>.</summary>
    Build = 1,

    /// <summary>Laying a road type: press and drag previews an L-shaped path and releasing enqueues <c>PlaceRoad</c>.</summary>
    Road = 2,

    /// <summary>Removing buildings and roads: a click demolishes one target, a drag selects several.</summary>
    Demolish = 3,
}
