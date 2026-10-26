using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// Content variants for the slice-level remap tests, built in temp folders and loaded through the real loader. The
/// extra-good overlay adds an item that sorts before every slice good, shifting every good ID by one; the
/// missing-windmill copy deletes the building and repairs the two references the linter would flag.
/// </summary>
internal static class ContentOverlays
{
    /// <summary>Gets the real slice content directory.</summary>
    public static string BaseContentDirectory => Path.Combine(WorldBuilder.RepoRoot, "content");

    /// <summary>Loads the base content plus an overlay that adds a good sorting before every slice good.</summary>
    /// <param name="content">The loaded content, with one more good.</param>
    /// <param name="map">The starter island on that content.</param>
    /// <returns>The overlay directory, for debugging; it is recreated on every call.</returns>
    public static string ExtraGood(out ContentDb content, out MapDef map)
    {
        string overlay = PrepareDirectory("extra-good");
        Write(overlay, "goods/amber.json", """{ "id": "amber", "category": "raw", "era": 1, "tags": ["resin"] }""");
        Write(overlay, "recipes/amber_loop.json",
            """{ "id": "amber_loop", "inputs": { "amber": 1 }, "outputs": { "amber": 2 }, "cycleSeconds": 30, "start": true }""");
        Write(overlay, "locale/en/amber.json",
            """{ "good.amber.name": "Amber", "good.amber.desc": "Resin that sorts first.", "recipe.amber_loop.name": "Amber loop", "recipe.amber_loop.desc": "Turns amber over." }""");

        content = ContentLoader.Load([BaseContentDirectory, overlay]).GetDbOrThrow();
        map = MapLoader.Load([BaseContentDirectory, overlay], "starter", content).GetMapOrThrow();
        return overlay;
    }

    /// <summary>
    /// Loads the base content plus one extra building that sorts before every slice building, so every building ID
    /// shifts by one. The addition is inert (nothing builds it): the frozen v1 save must remap by name, losing nothing.
    /// </summary>
    /// <param name="content">The loaded content, with one more building.</param>
    /// <returns>The overlay directory, for debugging; it is recreated on every call.</returns>
    public static string ExtraBuildingFirst(out ContentDb content)
    {
        string overlay = PrepareDirectory("extra-building-first");
        Write(overlay, "buildings/aaa_first_shed.json",
            """{ "id": "aaa_first_shed", "category": "infrastructure", "era": 1, "footprint": [1, 1], "placement": { "roadAccess": false }, "cost": { "timber": 1 }, "buildDays": 1, "start": true }""");

        content = ContentLoader.Load([BaseContentDirectory, overlay]).GetDbOrThrow();
        return overlay;
    }

    /// <summary>Loads a full copy of the content with the windmill building deleted and its references repaired.</summary>
    /// <param name="content">The loaded content, without the windmill.</param>
    /// <param name="map">The starter island on that content.</param>
    /// <returns>The copied directory, for debugging; it is recreated on every call.</returns>
    public static string MissingWindmill(out ContentDb content, out MapDef map)
    {
        string directory = PrepareDirectory("no-windmill");
        CopyDirectory(BaseContentDirectory, directory);

        File.Delete(Path.Combine(directory, "buildings", "windmill.json"));
        PatchMilling(Path.Combine(directory, "techs", "milling.json"));
        PatchGrindFlour(Path.Combine(directory, "recipes", "grind_flour.json"));

        content = ContentLoader.Load(directory).GetDbOrThrow();
        map = MapLoader.Load([directory], "starter", content).GetMapOrThrow();
        return directory;
    }

    private static string PrepareDirectory(string name)
    {
        string directory = Path.Combine(Path.GetTempPath(), "evenmere-save-tests", name);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Write(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text + "\n");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static void PatchMilling(string path)
    {
        string text = File.ReadAllText(path);
        const string Unlock = """    { "unlockBuilding": "windmill" },""";
        if (!text.Contains(Unlock, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The milling tech no longer unlocks the windmill the way the test expects.");
        }

        File.WriteAllText(path, text.Replace(Unlock, string.Empty, StringComparison.Ordinal));
    }

    private static void PatchGrindFlour(string path)
    {
        string text = File.ReadAllText(path);
        const string Anchor = """  "cycleSeconds": 30""";
        if (!text.Contains(Anchor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The grind_flour recipe no longer has the shape the test expects.");
        }

        File.WriteAllText(path, text.Replace(Anchor, "  \"cycleSeconds\": 30,\n  \"start\": true", StringComparison.Ordinal));
    }

    /// <summary>
    /// Loads the base content plus one overlay item sorting first in every content kind the tables cover, so every
    /// ID of every kind shifts by one. The additions are inert (nothing in the slice builds, researches or adopts them).
    /// </summary>
    /// <param name="content">The loaded content, with one more item per kind.</param>
    /// <param name="map">The starter island on that content.</param>
    /// <returns>The overlay directory, for debugging; it is recreated on every call.</returns>
    public static string ShiftEveryKind(out ContentDb content, out MapDef map)
    {
        string overlay = PrepareDirectory("shift-every-kind");
        Write(overlay, "goods/aaa_amber.json",
            """{ "id": "aaa_amber", "category": "raw", "era": 1, "tags": ["resin"] }""");
        Write(overlay, "recipes/aaa_amber_loop.json",
            """{ "id": "aaa_amber_loop", "inputs": { "aaa_amber": 1 }, "outputs": { "aaa_amber": 2 }, "cycleSeconds": 30 }""");
        Write(overlay, "buildings/aaa_shed.json",
            """{ "id": "aaa_shed", "category": "monument", "era": 1, "footprint": [1, 1], "cost": { "wood": 1 }, "buildDays": 1, "recipe": "aaa_amber_loop", "start": true }""");
        Write(overlay, "tiers/aaa_drifters.json",
            """{ "id": "aaa_drifters", "order": 3, "era": 1, "capacity": 4, "start": true }""");
        Write(overlay, "techs/aaa_alchemy.json",
            """{ "id": "aaa_alchemy", "era": 1, "cost": 10, "requires": ["cultivation"] }""");
        Write(overlay, "techs/zzz_gateway_tech.json",
            """{ "id": "zzz_gateway_tech", "era": 1, "cost": 10, "requires": ["cultivation"], "gateway": true }""");
        Write(overlay, "civics/aaa_customs.json",
            """{ "id": "aaa_customs", "era": 1, "cost": 10, "requires": ["hearth_and_shrine"] }""");
        Write(overlay, "civics/zzz_gateway_civic.json",
            """{ "id": "zzz_gateway_civic", "era": 1, "cost": 10, "requires": ["hearth_and_shrine"], "gateway": true }""");
        Write(overlay, "traditions/aaa_tales.json",
            """{ "id": "aaa_tales", "slot": "hearth", "start": true }""");
        Write(overlay, "festivals/aaa_lanterns.json",
            """{ "id": "aaa_lanterns", "cost": { "wood": 1 }, "days": 3, "culturePerDay": 10, "start": true }""");
        Write(overlay, "greatworks/aaa_menhir.json",
            """{ "id": "aaa_menhir", "source": "discovery", "cultureByEra": [5] }""");
        Write(overlay, "monuments/aaa_cairn.json",
            """{ "id": "aaa_cairn", "building": "aaa_shed", "stages": [{ "wood": 1 }] }""");
        Write(overlay, "happenings/aaa_omen.json",
            """{ "id": "aaa_omen", "trigger": { "produced": { "good": "wood", "atLeast": 1 } }, "chancePerMille": 10, "cooldownDays": 30, "options": [{ "effects": [] }] }""");
        Write(overlay, "eras/aaa_dawn.json",
            """{ "id": "aaa_dawn", "order": 2, "requires": { "tech": "zzz_gateway_tech", "civic": "zzz_gateway_civic" } }""");
        Write(overlay, "terrains/aaa_mud.json",
            """{ "id": "aaa_mud", "buildable": true }""");
        Write(overlay, "locale/en/aaa.json",
            """{ "good.aaa_amber.name": "Amber", "good.aaa_amber.desc": "Resin that sorts first.", "recipe.aaa_amber_loop.name": "Amber loop", "recipe.aaa_amber_loop.desc": "Turns amber over.", "building.aaa_shed.name": "Shed", "building.aaa_shed.desc": "A small shed.", "tier.aaa_drifters.name": "Drifters", "tier.aaa_drifters.desc": "Drifters.", "tech.aaa_alchemy.name": "Alchemy", "tech.aaa_alchemy.desc": "Alchemy.", "tech.zzz_gateway_tech.name": "Gateway tech", "tech.zzz_gateway_tech.desc": "Gateway tech.", "civic.aaa_customs.name": "Customs", "civic.aaa_customs.desc": "Customs.", "civic.zzz_gateway_civic.name": "Gateway civic", "civic.zzz_gateway_civic.desc": "Gateway civic.", "tradition.aaa_tales.name": "Tales", "tradition.aaa_tales.desc": "Tales.", "festival.aaa_lanterns.name": "Lanterns", "festival.aaa_lanterns.desc": "Lanterns.", "greatwork.aaa_menhir.name": "Menhir", "greatwork.aaa_menhir.desc": "Menhir.", "monument.aaa_cairn.name": "Cairn", "monument.aaa_cairn.desc": "Cairn.", "happening.aaa_omen.name": "Omen", "happening.aaa_omen.desc": "Omen.", "era.aaa_dawn.name": "Dawn", "era.aaa_dawn.desc": "Dawn.", "terrain.aaa_mud.name": "Mud", "terrain.aaa_mud.desc": "Mud." }""");

        content = ContentLoader.Load([BaseContentDirectory, overlay]).GetDbOrThrow();
        map = MapLoader.Load([BaseContentDirectory, overlay], "starter", content).GetMapOrThrow();
        return overlay;
    }
}
