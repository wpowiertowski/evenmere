using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>
/// The content the roads tests run on: the minimal fixture pack plus a small pack written to a temporary folder, which adds
/// what the minimal pack lacks (cobbled and paved roads, markets and shrines, a short-reach storehouse, 1 × 1 buildings that
/// make geometry easy to reason about, and access-locked highlands). It is loaded once and shared, because
/// <see cref="ContentDb"/> is immutable. The temporary folder is deleted as soon as the content has loaded.
/// </summary>
internal static class RoadContent
{
    private static readonly Lazy<ContentDb> _db = new(Load);

    // File name (under the content root) and JSON.
    private static readonly (string Path, string Json)[] _files =
    [
        ("buildings/cobbled_road.json", Road("cobbled_road", 800)),
        ("buildings/paved_road.json", Road("paved_road", 650)),
        ("buildings/market.json", """{ "id": "market", "category": "service", "era": 1, "footprint": [3, 3], "cost": { "timber": 6 }, "buildDays": 2, "workforce": { "tier": "settlers", "count": 2 }, "service": { "kind": "market", "coverage": 16 }, "start": true }"""),
        ("buildings/shrine.json", """{ "id": "shrine", "category": "culture", "era": 1, "footprint": [2, 2], "cost": { "timber": 6 }, "buildDays": 2, "workforce": { "tier": "settlers", "count": 1 }, "service": { "kind": "shrine", "coverage": 14 }, "start": true }"""),
        ("buildings/depot.json", """{ "id": "depot", "category": "infrastructure", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "workforce": { "tier": "settlers", "count": 1 }, "storage": { "reach": 5, "carts": 1 }, "start": true }"""),
        ("buildings/stall.json", """{ "id": "stall", "category": "service", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "workforce": { "tier": "settlers", "count": 1 }, "service": { "kind": "market", "coverage": 4 }, "start": true }"""),
        ("buildings/hut.json", """{ "id": "hut", "category": "residence", "era": 1, "footprint": [1, 1], "cost": { "timber": 1 }, "buildDays": 1, "start": true, "tags": ["house"] }"""),
        ("terrains/highlands.json", """{ "id": "highlands", "buildable": true, "blocksRoads": false, "access": "highlands" }"""),
        ("terrains/marsh.json", """{ "id": "marsh", "buildable": false, "blocksRoads": true, "access": "marsh_drainage" }"""),
    ];

    /// <summary>Gets the content.</summary>
    public static ContentDb Db => _db.Value;

    /// <summary>Gets a building type by its string ID.</summary>
    /// <param name="name">The ID, such as <c>dirt_road</c>.</param>
    /// <returns>The typed ID.</returns>
    public static BuildingTypeId Building(string name) => Db.Buildings.IdOf(name);

    /// <summary>Gets a terrain by its string ID.</summary>
    /// <param name="name">The ID, such as <c>river</c>.</param>
    /// <returns>The typed ID.</returns>
    public static TerrainId Terrain(string name) => Db.Terrains.IdOf(name);

    private static string Road(string id, int cost) =>
        $$"""{ "id": "{{id}}", "category": "infrastructure", "era": 1, "footprint": [1, 1], "placement": { "roadAccess": false }, "buildDays": 0, "roadCost": {{cost}}, "start": true, "tags": ["road"] }""";

    private static ContentDb Load()
    {
        string root = Path.Combine(Path.GetTempPath(), "evenmere-roads-content-" + Guid.NewGuid().ToString("N"));
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
