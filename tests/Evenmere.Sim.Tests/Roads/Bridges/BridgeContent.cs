using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Roads.Bridges;

/// <summary>
/// The content the bridge tests run on: the roads pack from <see cref="RoadContent"/> plus the two Era I bridge types, so
/// bridge spans and road costs come from content like in the game. The shared files are the same JSON, so every shared
/// string ID keeps the roads pack's index (both bridges sort after them); the tests rely on that for the dirt-road
/// shortcuts on <see cref="RoadsHarness"/> and assert it in <see cref="BridgeValidationTests"/>.
/// </summary>
internal static class BridgeContent
{
    private static readonly Lazy<ContentDb> _db = new(Load);

    // File name (under the content root) and JSON. The first nine are RoadContent's files verbatim.
    private static readonly (string Path, string Json)[] _files =
    [
        ("buildings/cobbled_road.json", Road("cobbled_road", 800)),
        ("buildings/paved_road.json", Road("paved_road", 650)),
        ("buildings/market.json", """{ "id": "market", "category": "service", "era": 1, "footprint": [3, 3], "cost": { "timber": 6 }, "buildDays": 2, "workforce": { "tier": "settlers", "count": 2 }, "service": { "kind": "market", "coverage": 16 }, "start": true }"""),
        ("buildings/shrine.json", """{ "id": "shrine", "category": "culture", "era": 1, "footprint": [2, 2], "cost": { "timber": 6 }, "buildDays": 2, "workforce": { "tier": "settlers", "count": 1 }, "service": { "kind": "shrine", "coverage": 14 }, "start": true }"""),
        ("buildings/depot.json", """{ "id": "depot", "category": "infrastructure", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "workforce": { "tier": "settlers", "count": 1 }, "storage": { "reach": 5, "carts": 1 }, "start": true }"""),
        ("buildings/stall.json", """{ "id": "stall", "category": "service", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "workforce": { "tier": "settlers", "count": 1 }, "service": { "kind": "market", "coverage": 4 }, "start": true }"""),
        ("buildings/hut.json", """{ "id": "hut", "category": "residence", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "start": true, "tags": ["house"] }"""),
        ("buildings/timber_bridge.json", Bridge("timber_bridge", 1500, 3)),
        ("buildings/stone_bridge.json", Bridge("stone_bridge", 2000, 8)),
        ("terrains/highlands.json", """{ "id": "highlands", "buildable": true, "blocksRoads": false, "access": "highlands" }"""),
        ("terrains/marsh.json", """{ "id": "marsh", "buildable": false, "blocksRoads": true, "access": "marsh_drainage" }"""),
    ];

    /// <summary>Gets the content.</summary>
    public static ContentDb Db => _db.Value;

    /// <summary>Gets the timber bridge type (span 3).</summary>
    public static BuildingTypeId Timber => Building("timber_bridge");

    /// <summary>Gets the stone bridge type (span 8).</summary>
    public static BuildingTypeId Stone => Building("stone_bridge");

    /// <summary>Gets the dirt road type.</summary>
    public static BuildingTypeId Dirt => Building("dirt_road");

    /// <summary>Gets a building type by its string ID.</summary>
    /// <param name="name">The ID, such as <c>timber_bridge</c>.</param>
    /// <returns>The typed ID.</returns>
    public static BuildingTypeId Building(string name) => Db.Buildings.IdOf(name);

    /// <summary>Gets a terrain by its string ID.</summary>
    /// <param name="name">The ID, such as <c>plains</c>.</param>
    /// <returns>The typed ID.</returns>
    public static TerrainId Terrain(string name) => Db.Terrains.IdOf(name);

    private static string Road(string id, int cost) =>
        $$"""{ "id": "{{id}}", "category": "infrastructure", "era": 1, "footprint": [1, 1], "placement": { "roadAccess": false }, "buildDays": 0, "roadCost": {{cost}}, "start": true, "tags": ["road"] }""";

    private static string Bridge(string id, int cost, int span) =>
        $$"""{ "id": "{{id}}", "category": "infrastructure", "era": 1, "footprint": [1, 1], "placement": { "roadAccess": false }, "buildDays": 0, "roadCost": {{cost}}, "bridgeSpan": {{span}}, "start": true, "tags": ["road"] }""";

    private static ContentDb Load()
    {
        string root = Path.Combine(Path.GetTempPath(), "evenmere-bridges-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach ((string path, string json) in _files)
            {
                string full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, json);
            }

            return ContentLoader.Load([WorldBuilder.MinimalPackPath, root]).GetDbOrThrow();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
