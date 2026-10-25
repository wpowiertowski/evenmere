using System.Text.Json;

namespace Evenmere.Game.Rendering.Buildings.Kit;

/// <summary>One composed element of a kit spec: the part kind plus its typed parameters.</summary>
/// <param name="Part">The part kind, such as <c>walls</c>.</param>
public abstract record KitPart(string Part);

/// <summary>Dry-stone footing course under a building.</summary>
public sealed record FootingPart(float X0, float Z0, float X1, float Z1, float H, string Material) : KitPart("footing");

/// <summary>Wall body with optional corner posts.</summary>
public sealed record WallsPart(float X0, float Z0, float X1, float Z1, float H, string Material, bool Posts) : KitPart("walls");

/// <summary>Gable roof with the ridge along X.</summary>
public sealed record GableRoofPart(float X0, float X1, float Z0, float Z1, float WallTop, float RidgeTop, string Roof, string Gable) : KitPart("gableRoof");

/// <summary>Hip roof.</summary>
public sealed record HipRoofPart(float X0, float X1, float Z0, float Z1, float WallTop, float RidgeTop, string Roof) : KitPart("hipRoof");

/// <summary>Single-slope shed roof, high on the z0 edge.</summary>
public sealed record ShedRoofPart(float X0, float X1, float Z0, float Z1, float TopHigh, float TopLow, string Roof, string Cheek) : KitPart("shedRoof");

/// <summary>Pyramid cap: mill ogee, shrine crown, hayrick cone.</summary>
public sealed record PyramidCapPart(float X0, float Z0, float X1, float Z1, float BaseY, float ApexY, string Material) : KitPart("pyramidCap");

/// <summary>Chimney stack with a cap slab.</summary>
public sealed record ChimneyPart(float X, float Z, float BaseY, float TopY, string Material) : KitPart("chimney");

/// <summary>Door slab proud of one face with a lintel. Face 0 = +Z, 1 = −Z, 2 = +X, 3 = −X.</summary>
public sealed record DoorPart(float X, float Z, float W, float H, byte Face, string Material) : KitPart("door");

/// <summary>Window with frame, optional shutters and flower box. Faces follow <see cref="DoorPart"/>.</summary>
public sealed record WindowPart(float X, float Y, float Z, float W, float H, byte Face, bool Shutters, bool Flowers) : KitPart("window");

/// <summary>Four corner posts with top plates.</summary>
public sealed record PostsPart(float X0, float Z0, float X1, float Z1, float H, string Material) : KitPart("posts");

/// <summary>Crossed log pile. Axis 0 runs along X, 1 along Z.</summary>
public sealed record LogPilePart(float X, float Z, float Len, byte Axis) : KitPart("logPile");

/// <summary>Leaning plank stack. Axis 0 fans along Z, 1 along X.</summary>
public sealed record PlankStackPart(float X, float Z, float W, float H, byte Axis, string Material) : KitPart("plankStack");

/// <summary>Crate cube, stacked <c>Count</c> high.</summary>
public sealed record CratePart(float X, float Z, float S, int Count, string Material) : KitPart("crate");

/// <summary>Grain sack with a tied neck.</summary>
public sealed record SackPart(float X, float Z, float S, string Material) : KitPart("sack");

/// <summary>Barrel with hoop bands.</summary>
public sealed record BarrelPart(float X, float Z) : KitPart("barrel");

/// <summary>Hand cart. Rot 0 faces +X, 1 faces +Z.</summary>
public sealed record CartPart(float X, float Z, byte Rot) : KitPart("cart");

/// <summary>Net-drying frame facing +Z.</summary>
public sealed record NetFramePart(float X, float Z, float W, float H) : KitPart("netFrame");

/// <summary>A-frame drying rack with a row of blanks. Axis 0 runs along X, 1 along Z.</summary>
public sealed record DryingRackPart(float X, float Z, float Len, byte Axis, string Blanks) : KitPart("dryingRack");

/// <summary>Post-and-rail fence around a rectangle. Gap is one of <c>none</c>, <c>N</c>, <c>S</c>, <c>E</c>, <c>W</c>.</summary>
public sealed record FencePart(float X0, float Z0, float X1, float Z1, string Gap) : KitPart("fence");

/// <summary>Crop rows across a field.</summary>
public sealed record CropRowsPart(float X0, float Z0, float X1, float Z1, int Rows, string Crop, string Soil) : KitPart("cropRows");

/// <summary>Four-sail windmill cross facing +Z.</summary>
public sealed record SailsPart(float X, float Y, float Z, float Radius) : KitPart("sails");

/// <summary>Beehive oven dome with a dark mouth.</summary>
public sealed record OvenPart(float X, float Z, float R) : KitPart("oven");

/// <summary>Bottle-kiln stack.</summary>
public sealed record KilnStackPart(float X, float Z) : KitPart("kilnStack");

/// <summary>Banner pole with a pennant streaming +X.</summary>
public sealed record BannerPart(float X, float Z, float H, string Material) : KitPart("banner");

/// <summary>Muster-bell frame.</summary>
public sealed record BellFramePart(float X, float Z) : KitPart("bellFrame");

/// <summary>Three offering bowls in a row, with flames when lit.</summary>
public sealed record BowlsPart(float X, float Z, bool Lit) : KitPart("bowls");

/// <summary>Quarry derrick with a block on the hook.</summary>
public sealed record DerrickPart(float X, float Z, float H) : KitPart("derrick");

/// <summary>Row of dressed stone blocks. Axis 0 runs along X, 1 along Z.</summary>
public sealed record StoneBlocksPart(float X, float Z, int Count, byte Axis, string Material) : KitPart("stoneBlocks");

/// <summary>Spoil heap: a flattened pyramid.</summary>
public sealed record SpoilHeapPart(float X, float Z, float R, float H, string Material) : KitPart("spoilHeap");

/// <summary>Orchard tree with an optional apple-dot crop.</summary>
public sealed record TreePart(float X, float Z, string Canopy, bool Fruit) : KitPart("tree");

/// <summary>Sheep. Rot 0 faces +X, 1 faces +Z.</summary>
public sealed record SheepPart(float X, float Z, byte Rot) : KitPart("sheep");

/// <summary>Hayrick cone.</summary>
public sealed record HayrickPart(float X, float Z, float R) : KitPart("hayrick");

/// <summary>Harvest stook.</summary>
public sealed record StookPart(float X, float Z) : KitPart("stook");

/// <summary>Scarecrow.</summary>
public sealed record ScarecrowPart(float X, float Z) : KitPart("scarecrow");

/// <summary>Carved story post.</summary>
public sealed record StoryPostPart(float X, float Z, float H) : KitPart("storyPost");

/// <summary>Log bench. Axis 0 runs along X, 1 along Z.</summary>
public sealed record BenchLogPart(float X, float Z, float Len, byte Axis) : KitPart("benchLog");

/// <summary>Fire pit with a flame when lit.</summary>
public sealed record FirePitPart(float X, float Z, float R, bool Lit) : KitPart("firePit");

/// <summary>Harvest maypole with bunting down to four corner posts.</summary>
public sealed record MaypolePart(float X, float Z, float H, float CX0, float CZ0, float CX1, float CZ1, float PostH) : KitPart("maypole");

/// <summary>Plaza medallion: a brick ring with a sun disc.</summary>
public sealed record MedallionPart(float X, float Z, float R) : KitPart("medallion");

/// <summary>Stone bench slab. Axis 0 runs along X, 1 along Z.</summary>
public sealed record BenchSlabPart(float X, float Z, float Len, byte Axis) : KitPart("benchSlab");

/// <summary>Planter with leaf fill and flower dots.</summary>
public sealed record PlanterPart(float X, float Z) : KitPart("planter");

/// <summary>Canoe on a rack. Axis 0 runs along X, 1 along Z.</summary>
public sealed record CanoePart(float X, float Z, float Len, byte Axis) : KitPart("canoe");

/// <summary>Parchment drying line with three sheets.</summary>
public sealed record ParchmentLinePart(float X0, float Z0, float X1, float Z1) : KitPart("parchmentLine");

/// <summary>Jetty finger: a deck on pile pairs.</summary>
public sealed record JettyPart(float X0, float Z0, float X1, float Z1) : KitPart("jetty");

/// <summary>Overturned coracle.</summary>
public sealed record CoraclePart(float X, float Z) : KitPart("coracle");

/// <summary>Shallow puddle quad.</summary>
public sealed record PuddlePart(float X, float Z, float R) : KitPart("puddle");

/// <summary>Turf mound for the standing stones.</summary>
public sealed record MoundPart(float X0, float Z0, float X1, float Z1, float H) : KitPart("mound");

/// <summary>Rough standing stone.</summary>
public sealed record StandingStonePart(float X, float Z, float W, float H, float D) : KitPart("standingStone");

/// <summary>Trilithon: two uprights with a lintel.</summary>
public sealed record TrilithonPart(float X, float Z) : KitPart("trilithon");

/// <summary>Flat ground quad: worn pasture, sett paving.</summary>
public sealed record GroundPart(float X0, float Z0, float X1, float Z1, float Y, string Material) : KitPart("ground");

/// <summary>Market stall table with goods dots. Rot 0 runs along Z, 1 along X.</summary>
public sealed record StallTablePart(float X, float Z, byte Rot, string Goods) : KitPart("stallTable");

/// <summary>
/// One building's kit spec: the footprint it was authored for and the parts composed in order. Godot-free on
/// purpose, so specs validate without a GPU. Footprints come from content JSON, never from the spec: when content
/// lands for a building, the factory warns if the spec's footprint disagrees.
/// </summary>
/// <param name="Id">The content building ID, matching the file name.</param>
/// <param name="Width">The authored footprint width in tiles.</param>
/// <param name="Depth">The authored footprint depth in tiles.</param>
/// <param name="Parts">The parts, composed in order.</param>
public sealed record KitSpec(string Id, float Width, float Depth, IReadOnlyList<KitPart> Parts)
{
    /// <summary>
    /// Parses and validates a kit spec. Bad parts are skipped with one warning each through
    /// <paramref name="warn"/>, so one typo never blanks a building; a spec with no valid parts returns
    /// <see langword="null"/> and the caller keeps the placeholder.
    /// </summary>
    /// <param name="id">The expected content building ID (the file name).</param>
    /// <param name="json">The file text.</param>
    /// <param name="warn">Receives each validation warning.</param>
    /// <returns>The spec, or <see langword="null"/> when nothing usable remains.</returns>
    public static KitSpec? Load(string id, string json, Action<string> warn)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            warn($"{id}: file is not valid JSON ({ex.Message}); keeping the placeholder.");
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warn($"{id}: root must be an object; keeping the placeholder.");
                return null;
            }

            string fileId = Str(root, "id", id);
            if (!string.Equals(fileId, id, StringComparison.Ordinal))
            {
                warn($"{id}: id '{fileId}' does not match the file name; keeping the placeholder.");
                return null;
            }

            float width = 0f;
            float depth = 0f;
            if (root.TryGetProperty("footprint", out JsonElement footprint) && footprint.ValueKind == JsonValueKind.Array && footprint.GetArrayLength() == 2)
            {
                width = Num(footprint[0], 0f);
                depth = Num(footprint[1], 0f);
            }

            if (width <= 0f || depth <= 0f)
            {
                warn($"{id}: footprint must be [width, depth] with positive numbers; keeping the placeholder.");
                return null;
            }

            if (!root.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array)
            {
                warn($"{id}: missing 'parts' array; keeping the placeholder.");
                return null;
            }

            var parsed = new List<KitPart>();
            foreach (JsonElement element in parts.EnumerateArray())
            {
                KitPart? part = ParsePart(id, element, warn);
                if (part is not null)
                {
                    parsed.Add(part);
                }
            }

            if (parsed.Count == 0)
            {
                warn($"{id}: no valid parts; keeping the placeholder.");
                return null;
            }

            return new KitSpec(id, width, depth, parsed);
        }
    }

    private static KitPart? ParsePart(string id, JsonElement element, Action<string> warn)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("part", out JsonElement kindElement) || kindElement.ValueKind != JsonValueKind.String)
        {
            warn($"{id}: a part must be an object with a string 'part' name; skipped.");
            return null;
        }

        string kind = kindElement.GetString() ?? string.Empty;
        return kind switch
        {
            "footing" => new FootingPart(X0(element), Z0(element), X1(element), Z1(element), H(element, 0.12f), Mat(element, "material", "stone.mid", id, warn)),
            "walls" => new WallsPart(X0(element), Z0(element), X1(element), Z1(element), H(element, 0.65f), Mat(element, "material", "wall.wattle", id, warn), Bool(element, "posts", true)),
            "gableRoof" => Gable(element, id, warn),
            "hipRoof" => new HipRoofPart(X0(element), Z0(element), X1(element), Z1(element), Num(element, "wallTop", 0.65f), Num(element, "ridgeTop", 1.4f), Mat(element, "roof", "roof.thatch.light", id, warn)),
            "shedRoof" => Shed(element, id, warn),
            "pyramidCap" => new PyramidCapPart(X0(element), Z0(element), X1(element), Z1(element), Num(element, "baseY", 0.65f), Num(element, "apexY", 1.2f), Mat(element, "material", "roof.thatch.light", id, warn)),
            "chimney" => new ChimneyPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "baseY", 0.8f), Num(element, "topY", 1.9f), Mat(element, "material", "brick.fired", id, warn)),
            "door" => new DoorPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "w", 0.25f), Num(element, "h", 0.5f), Face(element, id, warn), Mat(element, "material", "wood.plank.mid", id, warn)),
            "window" => new WindowPart(Num(element, "x", 0f), Num(element, "y", 0.4f), Num(element, "z", 0f), Num(element, "w", 0.18f), Num(element, "h", 0.23f), Face(element, id, warn), Bool(element, "shutters", true), Bool(element, "flowers", false)),
            "posts" => new PostsPart(X0(element), Z0(element), X1(element), Z1(element), H(element, 0.65f), Mat(element, "material", "wood.beam.dark", id, warn)),
            "logPile" => new LogPilePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "len", 0.8f), Axis(element, id, warn)),
            "plankStack" => new PlankStackPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "w", 0.6f), Num(element, "h", 0.5f), Axis(element, id, warn), Mat(element, "material", "wood.timber.light", id, warn)),
            "crate" => new CratePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "s", 0.18f), Int(element, "count", 1), Mat(element, "material", "wood.timber.light", id, warn)),
            "sack" => new SackPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "s", 0.2f), Mat(element, "material", "good.flour", id, warn)),
            "barrel" => new BarrelPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "cart" => new CartPart(Num(element, "x", 0f), Num(element, "z", 0f), Rot(element, id, warn)),
            "netFrame" => new NetFramePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "w", 0.8f), Num(element, "h", 0.5f)),
            "dryingRack" => new DryingRackPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "len", 0.9f), Axis(element, id, warn), Mat(element, "blanks", "clay.raw", id, warn)),
            "fence" => new FencePart(X0(element), Z0(element), X1(element), Z1(element), Gap(element, id, warn)),
            "cropRows" => new CropRowsPart(X0(element), Z0(element), X1(element), Z1(element), Int(element, "rows", 6), Mat(element, "crop", "field.grain", id, warn), Mat(element, "soil", "field.soil", id, warn)),
            "sails" => new SailsPart(Num(element, "x", 0f), Num(element, "y", 1.2f), Num(element, "z", 0f), Num(element, "radius", 0.6f)),
            "oven" => new OvenPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.25f)),
            "kilnStack" => new KilnStackPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "banner" => new BannerPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "h", 0.9f), Mat(element, "material", "ui.accent", id, warn)),
            "bellFrame" => new BellFramePart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "bowls" => new BowlsPart(Num(element, "x", 0f), Num(element, "z", 0f), Bool(element, "lit", true)),
            "derrick" => new DerrickPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "h", 1.5f)),
            "stoneBlocks" => new StoneBlocksPart(Num(element, "x", 0f), Num(element, "z", 0f), Int(element, "count", 3), Axis(element, id, warn), Mat(element, "material", "stone.light", id, warn)),
            "spoilHeap" => new SpoilHeapPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.5f), Num(element, "h", 0.3f), Mat(element, "material", "stone.light", id, warn)),
            "tree" => new TreePart(Num(element, "x", 0f), Num(element, "z", 0f), Mat(element, "canopy", "leaf.orchard", id, warn), Bool(element, "fruit", false)),
            "sheep" => new SheepPart(Num(element, "x", 0f), Num(element, "z", 0f), Rot(element, id, warn)),
            "hayrick" => new HayrickPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.3f)),
            "stook" => new StookPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "scarecrow" => new ScarecrowPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "storyPost" => new StoryPostPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "h", 0.75f)),
            "benchLog" => new BenchLogPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "len", 0.7f), Axis(element, id, warn)),
            "firePit" => new FirePitPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.25f), Bool(element, "lit", true)),
            "maypole" => Maypole(element),
            "medallion" => new MedallionPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.3f)),
            "benchSlab" => new BenchSlabPart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "len", 0.6f), Axis(element, id, warn)),
            "planter" => new PlanterPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "canoe" => new CanoePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "len", 0.8f), Axis(element, id, warn)),
            "parchmentLine" => new ParchmentLinePart(X0(element), Z0(element), X1(element), Z1(element)),
            "jetty" => new JettyPart(X0(element), Z0(element), X1(element), Z1(element)),
            "coracle" => new CoraclePart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "puddle" => new PuddlePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "r", 0.5f)),
            "mound" => new MoundPart(X0(element), Z0(element), X1(element), Z1(element), Num(element, "h", 0.25f)),
            "standingStone" => new StandingStonePart(Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "w", 0.2f), Num(element, "h", 0.7f), Num(element, "d", 0.15f)),
            "trilithon" => new TrilithonPart(Num(element, "x", 0f), Num(element, "z", 0f)),
            "ground" => new GroundPart(X0(element), Z0(element), X1(element), Z1(element), Num(element, "y", 0.02f), Mat(element, "material", "field.fallow", id, warn)),
            "stallTable" => new StallTablePart(Num(element, "x", 0f), Num(element, "z", 0f), Rot(element, id, warn), Mat(element, "goods", "good.bread", id, warn)),
            _ => WarnUnknown(id, kind, warn),
        };
    }

    private static KitPart? WarnUnknown(string id, string kind, Action<string> warn)
    {
        warn($"{id}: unknown part '{kind}'; skipped.");
        return null;
    }

    private static GableRoofPart Gable(JsonElement element, string id, Action<string> warn)
    {
        string roof = Mat(element, "roof", "roof.thatch.light", id, warn);
        string gable = element.TryGetProperty("gable", out JsonElement gableElement) && gableElement.ValueKind == JsonValueKind.String
            ? Mat(element, "gable", roof, id, warn)
            : roof;
        return new GableRoofPart(X0(element), X1(element), Z0(element), Z1(element), Num(element, "wallTop", 0.65f), Num(element, "ridgeTop", 1.4f), roof, gable);
    }

    private static ShedRoofPart Shed(JsonElement element, string id, Action<string> warn)
    {
        string roof = Mat(element, "roof", "roof.thatch.light", id, warn);
        string cheek = element.TryGetProperty("cheek", out _) ? Mat(element, "cheek", roof, id, warn) : roof;
        return new ShedRoofPart(X0(element), X1(element), Z0(element), Z1(element), Num(element, "topHigh", 0.9f), Num(element, "topLow", 0.6f), roof, cheek);
    }

    private static MaypolePart Maypole(JsonElement element) => new(
        Num(element, "x", 0f), Num(element, "z", 0f), Num(element, "h", 1.25f),
        X0(element), Z0(element), X1(element), Z1(element), Num(element, "postH", 0.9f));

    private static float X0(JsonElement element) => Num(element, "x0", 0f);
    private static float Z0(JsonElement element) => Num(element, "z0", 0f);
    private static float X1(JsonElement element) => Num(element, "x1", 0f);
    private static float Z1(JsonElement element) => Num(element, "z1", 0f);
    private static float H(JsonElement element, float fallback) => Num(element, "h", fallback);

    private static float Num(JsonElement element, string name, float fallback)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return (float)value.GetDouble();
        }

        return fallback;
    }

    private static float Num(JsonElement value, float fallback) =>
        value.ValueKind == JsonValueKind.Number ? (float)value.GetDouble() : fallback;

    private static bool Bool(JsonElement element, string name, bool fallback)
    {
        if (element.TryGetProperty(name, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return fallback;
    }

    private static int Int(JsonElement element, string name, int fallback)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return fallback;
    }

    private static string Str(JsonElement element, string name, string fallback)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        return fallback;
    }

    private static string Mat(JsonElement element, string name, string fallback, string id, Action<string> warn)
    {
        string token = Str(element, name, fallback);
        if (!KitPalette.TryGetSlot(token, out _))
        {
            warn($"{id}: unknown palette token '{token}'; using '{fallback}'.");
            return fallback;
        }

        return token;
    }

    private static byte Axis(JsonElement element, string id, Action<string> warn)
    {
        int axis = Int(element, "axis", 0);
        if (axis is not (0 or 1))
        {
            warn($"{id}: axis must be 0 or 1; using 0.");
            return 0;
        }

        return (byte)axis;
    }

    private static byte Rot(JsonElement element, string id, Action<string> warn)
    {
        int rot = Int(element, "rot", 0);
        if (rot is not (0 or 1))
        {
            warn($"{id}: rot must be 0 or 1; using 0.");
            return 0;
        }

        return (byte)rot;
    }

    private static byte Face(JsonElement element, string id, Action<string> warn)
    {
        int face = Int(element, "face", 0);
        if (face is < 0 or > 3)
        {
            warn($"{id}: face must be 0 (+Z), 1 (−Z), 2 (+X) or 3 (−X); using 0.");
            return 0;
        }

        return (byte)face;
    }

    private static string Gap(JsonElement element, string id, Action<string> warn)
    {
        string gap = Str(element, "gap", "none");
        if (gap is not ("none" or "N" or "S" or "E" or "W"))
        {
            warn($"{id}: gap must be none, N, S, E or W; using none.");
            return "none";
        }

        return gap;
    }
}
