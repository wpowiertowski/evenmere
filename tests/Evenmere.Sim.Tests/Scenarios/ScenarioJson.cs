using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// The System.Text.Json settings for reading a command's <c>args</c>. Property names match the record's constructor
/// parameters ignoring case, unknown and missing properties are errors, and the Core value types that System.Text.Json
/// would otherwise read wrongly (and silently, as zero) have converters: content IDs are written as their string id,
/// <see cref="Amount"/> as a decimal, <see cref="EntityId"/> as an integer, <see cref="TileCoord"/> as <c>[x, y]</c> and
/// <see cref="Rotation"/> as 0 to 3. Arrays of these (<c>TechId[]</c>, <c>TileCoord[]</c>) need nothing extra. A task that
/// adds a command parameter of another value type adds its converter here.
/// </summary>
internal static class ScenarioJson
{
    /// <summary>Creates the options for one scenario run. They are bound to the run's content, which resolves string ids.</summary>
    /// <param name="content">The content the scenario runs on.</param>
    /// <returns>The options.</returns>
    public static JsonSerializerOptions CreateOptions(ContentDb content) => new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters =
        {
            new AmountJsonConverter(),
            new EntityIdJsonConverter(),
            new TileCoordJsonConverter(),
            new RotationJsonConverter(),
            new TraditionSlotTypeJsonConverter(),
            new PointKindJsonConverter(),
            new ContentIdJsonConverterFactory(content),
        },
    };

    /// <summary>Reads an <see cref="Amount"/> from a decimal string such as <c>"12.5"</c> or a JSON number, with <see cref="Amount.Parse"/>.</summary>
    private sealed class AmountJsonConverter : JsonConverter<Amount>
    {
        public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
                _ => throw new JsonException("An amount must be a decimal string such as \"12.5\" or a JSON number."),
            };

            try
            {
                return Amount.Parse(text ?? string.Empty);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new JsonException(ex.Message, ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, Amount value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>Reads an <see cref="EntityId"/> from a JSON integer.</summary>
    private sealed class EntityIdJsonConverter : JsonConverter<EntityId>
    {
        public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)
                ? new EntityId(value)
                : throw new JsonException("An entity id must be a whole number.");

        public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    /// <summary>
    /// Reads a <see cref="TileCoord"/> from a two-number JSON array <c>[x, y]</c>. An array of tiles (<c>TileCoord[]</c>) needs
    /// no converter of its own: System.Text.Json reads it element by element, as <c>[[x, y], …]</c>.
    /// </summary>
    private sealed class TileCoordJsonConverter : JsonConverter<TileCoord>
    {
        public override TileCoord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            const string Expected = "A tile must be a two-number array [x, y].";
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(Expected);
            }

            int[] parts = new int[2];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out parts[i]))
                {
                    throw new JsonException(Expected);
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException(Expected);
            }

            return new TileCoord(parts[0], parts[1]);
        }

        public override void Write(Utf8JsonWriter writer, TileCoord value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Reads a <see cref="Rotation"/> from the number of quarter turns, 0 to 3. Without this converter System.Text.Json would
    /// accept any number and produce an undefined enum value.
    /// </summary>
    private sealed class RotationJsonConverter : JsonConverter<Rotation>
    {
        public override Rotation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int quarters) && quarters is >= 0 and <= 3
                ? (Rotation)quarters
                : throw new JsonException("A rotation must be a number of quarter turns, 0 to 3.");

        public override void Write(Utf8JsonWriter writer, Rotation value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((int)value);
    }

    /// <summary>
    /// Reads a <see cref="TraditionSlotType"/> from its snake_case name (<c>hearth</c>, <c>craft</c>, <c>heritage</c>,
    /// <c>wild</c>), case-insensitively. Added for <c>SetTradition</c> (M2-07): without it System.Text.Json would only
    /// accept the enum's numbers.
    /// </summary>
    private sealed class TraditionSlotTypeJsonConverter : JsonConverter<TraditionSlotType>
    {
        /// <inheritdoc />
        public override TraditionSlotType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
            && Enum.TryParse<TraditionSlotType>(reader.GetString(), ignoreCase: true, out TraditionSlotType slot)
            && Enum.IsDefined(slot)
                ? slot
                : throw new JsonException("A tradition slot type is one of \"hearth\", \"craft\", \"heritage\" or \"wild\".");

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TraditionSlotType value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }

    /// <summary>Reads a <see cref="PointKind\"/> from its lowercase name (for example <c>\"knowledge\"</c>).</summary>
    private sealed class PointKindJsonConverter : JsonConverter<PointKind>
    {
        /// <inheritdoc />
        public override PointKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
            && Enum.TryParse<PointKind>(reader.GetString(), ignoreCase: true, out PointKind kind)
            && Enum.IsDefined(kind)
                ? kind
                : throw new JsonException("A point kind is one of \"knowledge\", \"culture\" or \"coin\".");

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, PointKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }

    /// <summary>Makes a converter for each content ID type (<c>GoodId</c>, <c>BuildingTypeId</c> and so on) that reads the item's string id.</summary>
    private sealed class ContentIdJsonConverterFactory(ContentDb content) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsValueType && typeToConvert.GetInterfaces().Any(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>) && i.GetGenericArguments()[0] == typeToConvert);

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter?)Activator.CreateInstance(typeof(ContentIdJsonConverter<>).MakeGenericType(typeToConvert), content);
    }

    /// <summary>
    /// The generated-map source for scenarios (M2-02b): <c>"generated:&lt;seed&gt;:&lt;size&gt;"</c>, for example
    /// <c>"generated:7:320"</c>. The runner loads it through <see cref="IslandGenerator"/> instead of
    /// <c>MapLoader</c>, so scenarios can play on generated islands. Hand-authored map names never contain a colon.
    /// </summary>
    internal static class GeneratedSource
    {
        /// <summary>Gets the prefix every generated-map source starts with.</summary>
        public const string Prefix = "generated:";

        /// <summary>
        /// Reads a generated-map source.
        /// </summary>
        /// <param name="map">The scenario's map value.</param>
        /// <param name="seed">The island seed.</param>
        /// <param name="size">The island size in tiles across.</param>
        /// <returns><see langword="true" /> when the value names a generated island.</returns>
        public static bool TryParse(string? map, out int seed, out int size)
        {
            seed = 0;
            size = 0;
            if (map is null || !map.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = map.Split(':');
            return parts.Length == 3 && int.TryParse(parts[1], out seed) && int.TryParse(parts[2], out size);
        }

        /// <summary>
        /// Loads a generated island for a scenario.
        /// </summary>
        /// <param name="seed">The island seed.</param>
        /// <param name="size">The island size in tiles across: 256, 320 or 384.</param>
        /// <param name="content">The content the scenario runs on.</param>
        /// <returns>The finished island.</returns>
        /// <exception cref="ScenarioException">The size is not a generated-island size.</exception>
        public static MapDef Load(int seed, int size, ContentDb content)
        {
            try
            {
                return IslandGenerator.Generate(seed, size, content);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new ScenarioException($"map: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// A building the scenario runner places for the file: a <c>PlaceBuilding</c> whose args carry
    /// <c>"site": true</c> instead of coordinates (generated-map scenarios only). The site is picked at enqueue time
    /// through the same spiral the balance bot uses: rings of ascending Chebyshev distance from the Founders' Hall,
    /// then rows, columns and rotations 0 to 3, validated through <c>IPlacementQueries.Validate</c>. The first spot
    /// with no errors and no warnings wins, then the first with no <c>LowYield</c> warning, then the first placeable
    /// spot. Every footprint tile must be reachable from the hall without crossing a river or open water, so the
    /// settlement never strands itself across an unbridged river.
    /// </summary>
    /// <param name="Type">The building type to place.</param>
    internal sealed record SiteRequest(BuildingTypeId Type);

    /// <summary>
    /// Reads a site request from a command's args, stripping the marker so the upfront deserialisation sees plain
    /// coordinates. Only <c>PlaceBuilding</c> supports it, and only on generated-map scenarios.
    /// </summary>
    /// <param name="commandType">The simple type name of the command record.</param>
    /// <param name="args">The args object as written in the file.</param>
    /// <param name="content">The content the scenario runs on, which resolves the building type.</param>
    /// <param name="where">Where the command sits, for error messages.</param>
    /// <returns>The site request and the args to deserialise, with placeholder coordinates.</returns>
    /// <exception cref="ScenarioException">The marker is misused.</exception>
    public static (SiteRequest? Request, JsonElement Args) ReadSite(string commandType, JsonElement args, ContentDb content, string where)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("site", out JsonElement site))
        {
            return (null, args);
        }

        if (!commandType.Equals("PlaceBuilding", StringComparison.Ordinal))
        {
            throw new ScenarioException($"{where}: {{ \"site\": true }} fits only PlaceBuilding, not {commandType}.");
        }

        if (site.ValueKind != JsonValueKind.True)
        {
            throw new ScenarioException($"{where}.args.site: must be true; site-resolved placements omit x, y and rotation.");
        }

        if (!args.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
        {
            throw new ScenarioException($"{where}.args.type: a site-resolved placement still names its building type.");
        }

        foreach (string fixedCoord in new[] { "x", "y", "rotation" })
        {
            if (args.TryGetProperty(fixedCoord, out _))
            {
                throw new ScenarioException($"{where}.args.{fixedCoord}: a site-resolved placement omits coordinates; the search picks them.");
            }
        }

        string name = type.GetString() ?? string.Empty;
        if (!content.TryGet(name, out BuildingTypeId id))
        {
            throw new ScenarioException($"{where}.args.type: no building named \"{name}\" in the loaded content.");
        }

        var node = JsonNode.Parse(args.GetRawText());
        if (node is not JsonObject patched)
        {
            throw new ScenarioException($"{where}.args: must be a JSON object.");
        }

        patched.Remove("site");
        patched["x"] = -1;
        patched["y"] = -1;
        patched["rotation"] = 0;
        return (new SiteRequest(id), JsonDocument.Parse(patched.ToJsonString()).RootElement);
    }

    /// <summary>
    /// A placed building plus the dirt connector that joins it to the settlement's roads. The road comes first when
    /// the runner enqueues the pair, so the building applies with its access warning cleared.
    /// </summary>
    /// <param name="X">The column of the footprint's top-left tile.</param>
    /// <param name="Y">The row of the footprint's top-left tile.</param>
    /// <param name="Rotation">The orientation.</param>
    /// <param name="Road">The connector tiles in order, empty when the footprint already touches a road.</param>
    internal sealed record SitePlacement(int X, int Y, Rotation Rotation, TileCoord[] Road);

    /// <summary>
    /// Picks sites for one scenario run on a generated island: the hall anchor, the same-bank flood and the spiral
    /// search. The map never changes during a scenario, so the anchor and flood are computed once; every placement
    /// validates against the live island, so later buildings see earlier ones as occupied.
    /// </summary>
    internal sealed class GeneratedSiteContext
    {
        /// <summary>Gets how far the spiral looks from the hall, in tiles.</summary>
        public const int MaxRadius = 64;

        /// <summary>Gets how many warned candidates' connectors are tried before giving up.</summary>
        public const int MaxFallbackTries = 200;

        // Neighbour offsets in a fixed order, so connector paths are identical everywhere. Four-directional: the
        // storehouse fields and the road validator's bridge runs all work in 4-connectivity, so diagonal road steps
        // would lay isolated tiles that carry no reach.
        private static readonly int[] _bx = [-1, 1, 0, 0];
        private static readonly int[] _by = [0, 0, -1, 1];

        private readonly ContentDb _content;
        private readonly TileCoord _hall;
        private readonly int _hallWidth;
        private readonly int _hallHeight;
        private readonly int _hallReach;
        private readonly bool[] _reachable;
        private readonly int _width;
        private readonly int _height;
        private readonly List<Rect> _reserved = new();
        private readonly bool[] _roadTiles;

        private GeneratedSiteContext(
            ContentDb content,
            TileCoord hall,
            int hallWidth,
            int hallHeight,
            int hallReach,
            bool[] reachable,
            int width,
            int height)
        {
            _content = content;
            _hall = hall;
            _hallWidth = hallWidth;
            _hallHeight = hallHeight;
            _hallReach = hallReach;
            _reachable = reachable;
            _width = width;
            _height = height;
            _roadTiles = new bool[width * height];
        }

        /// <summary>
        /// Creates the site context for a run: the Founders' Hall anchor and every tile reachable from it without
        /// crossing a river or open water.
        /// </summary>
        /// <param name="simulation">The simulation, read between ticks.</param>
        /// <param name="content">The content the scenario runs on.</param>
        /// <returns>The context.</returns>
        /// <exception cref="ScenarioException">The island holds no Founders' Hall.</exception>
        public static GeneratedSiteContext Create(ISimulation simulation, ContentDb content)
        {
            ArgumentNullException.ThrowIfNull(simulation);
            ArgumentNullException.ThrowIfNull(content);

            BuildingTypeId hall = content.Required.FoundersHall;
            TileCoord anchor = default;
            bool found = false;
            foreach (BuildingInfo building in simulation.Query.Map.Buildings)
            {
                if (building.Type.Equals(hall))
                {
                    anchor = building.Origin;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new ScenarioException("map: the island holds no Founders' Hall, so site-resolved placements have no anchor.");
            }

            int width = simulation.Query.Map.Width;
            int height = simulation.Query.Map.Height;
            BuildingDef hallDef = content.Get(hall);
            Footprint hallSize = hallDef.Footprint;
            bool[] reachable = Flood(content, simulation, width, height, anchor);
            int reach = hallDef.Storage?.Reach ?? int.MaxValue;
            return new GeneratedSiteContext(
                content, anchor, hallSize.Width, hallSize.Height, reach, reachable, width, height);
        }

        /// <summary>
        /// Finds a site for a building: the spiral from the hall, validated live, preferring warning-free spots on
        /// the hall's river bank, plus the dirt connector that joins the footprint to the settlement's roads.
        /// </summary>
        /// <param name="simulation">The simulation, read between ticks.</param>
        /// <param name="type">The building type to place.</param>
        /// <param name="width">The footprint width before rotation.</param>
        /// <param name="height">The footprint height before rotation.</param>
        /// <returns>The top-left tile, rotation and connector tiles.</returns>
        /// <exception cref="ScenarioException">No placeable spot lies within radius, or no road reaches it.</exception>
        public SitePlacement FindSite(ISimulation simulation, BuildingTypeId type, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(simulation);

            int mapWidth = simulation.Query.Map.Width;
            int mapHeight = simulation.Query.Map.Height;
            var keptYields = new List<PlacementCandidate>();
            var placeables = new List<PlacementCandidate>();
            int seen = 0;
            int bankFail = 0;
            int invalid = 0;
            int reservedFail = 0;
            int connectorFail = 0;
            int warned = 0;
            int reachFail = 0;
            int[] hallDist = HallDistances(simulation);
            for (int ring = 0; ring <= MaxRadius; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring)
                        {
                            continue;
                        }

                        int x = _hall.X + dx;
                        int y = _hall.Y + dy;
                        for (int r = 0; r < 4; r++)
                        {
                            var rotation = (Rotation)r;
                            int w = rotation.SwapsAxes() ? height : width;
                            int h = rotation.SwapsAxes() ? width : height;
                            if (x < 0 || y < 0 || x + w > mapWidth || y + h > mapHeight)
                            {
                                continue;
                            }

                            // A 4-directional road can never beat the Chebyshev distance, so anything this far
                            // out is beyond the hall's reach whatever path it takes.
                            if (_hallReach < int.MaxValue / 2 && ChebyshevToHall(x, y, w, h) > _hallReach + 2)
                            {
                                reachFail++;
                                continue;
                            }

                            seen++;
                            if (!BankReaches(x, y, w, h))
                            {
                                bankFail++;
                                continue;
                            }

                            PlacementResult validation = simulation.Query.Placement.Validate(type, x, y, rotation);
                            if (!validation.CanPlace)
                            {
                                invalid++;
                                continue;
                            }

                            if (IsReserved(x, y, w, h) || OverlapsRoad(x, y, w, h))
                            {
                                reservedFail++;
                                continue;
                            }

                            var candidate = new PlacementCandidate(x, y, rotation, w, h, validation);
                            if (validation.Warnings.IsDefaultOrEmpty)
                            {
                                // A warning-free spot whose connector fails is skipped, not fatal: a later spot
                                // may still join. Reservations wall pockets off as the settlement grows, so the
                                // connector proves each candidate rather than the bank flood alone.
                                SitePlacement? placed = TryPlace(simulation, candidate, hallDist);
                                if (placed is not null)
                                {
                                    return placed;
                                }

                                connectorFail++;
                                continue;
                            }

                            warned++;

                            // Warnings are computed against the island without this building's connector, so a
                            // spot that will join up still warns here. Collect warned candidates (capped) and try
                            // their connectors below, preferring good yields like the bot does.
                            warned++;
                            if (!validation.Has(PlacementIssue.LowYield))
                            {
                                if (keptYields.Count < MaxFallbackTries) { keptYields.Add(candidate); }
                            }
                            else if (placeables.Count < MaxFallbackTries)
                            {
                                placeables.Add(candidate);
                            }
                        }
                    }
                }
            }

            int fallbackTried = 0;
            foreach (PlacementCandidate fallback in keptYields.Concat(placeables))
            {
                fallbackTried++;
                SitePlacement? placed = TryPlace(simulation, fallback, hallDist);
                if (placed is not null)
                {
                    return placed;
                }
            }

            throw new ScenarioException(
                $"site: no placeable spot for a building within {MaxRadius} tiles of the Founders' Hall " +
                $"(seen={seen} bankFail={bankFail} invalid={invalid} reservedFail={reservedFail} warned={warned} connectorFail={connectorFail} fallbackTried={fallbackTried} reachFail={reachFail}).");
        }

        // Reserves a picked footprint and plans its dirt connector back to the settlement's roads. A connector
        // that cannot be planned returns null instead of throwing, so the search keeps spiralling.
        private SitePlacement? TryPlace(ISimulation simulation, PlacementCandidate candidate, int[] hallDist)
        {
            // Reserved before planning, so the connector never crosses its own footprint; unreserved when no
            // connector reaches it, so later searches may use the ground.
            var rect = new Rect(candidate.X, candidate.Y, candidate.Width, candidate.Height);
            _reserved.Add(rect);
            TileCoord[] road;
            try
            {
                road = PlanConnector(simulation, candidate.X, candidate.Y, candidate.Width, candidate.Height, hallDist);
            }
            catch (ScenarioException)
            {
                _reserved.Remove(rect);
                return null;
            }

            foreach (TileCoord tile in road)
            {
                _roadTiles[(tile.Y * _width) + tile.X] = true;
            }

            return new SitePlacement(candidate.X, candidate.Y, candidate.Rotation, road);
        }

        // The shortest dirt path from the footprint's ring to the road network or the hall: a breadth-first search
        // over tiles a dirt road may pave (buildable, not blocking roads, highlands excluded while their access is
        // locked, no rivers, and free of buildings except roads already laid). Four-directional like the bank flood
        // and the storehouse fields, so every laid tile joins the network. The path ends adjacent to the network,
        // so placing it joins the footprint up. Empty when the footprint already touches a road.
        private TileCoord[] PlanConnector(ISimulation simulation, int x, int y, int width, int height, int[] hallDist)
        {
            int[] parent = new int[_width * _height];
            Array.Fill(parent, -2);
            var queue = new Queue<int>();
            foreach (TileCoord start in Ring(x, y, width, height))
            {
                int s = (start.Y * _width) + start.X;
                if (parent[s] != -2 || !RoadPaveable(simulation, start.X, start.Y))
                {
                    continue;
                }

                parent[s] = -1;
                if (IsRoad(simulation, start.X, start.Y))
                {
                    return [];
                }

                queue.Enqueue(s);
            }

            int goal = -1;
            while (queue.Count > 0 && goal < 0)
            {
                int at = queue.Dequeue();
                int ax = at % _width;
                int ay = at / _width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ax + _bx[d];
                    int ny = ay + _by[d];
                    if ((uint)nx >= (uint)_width || (uint)ny >= (uint)_height)
                    {
                        continue;
                    }

                    int n = (ny * _width) + nx;
                    if (parent[n] != -2 || !RoadPaveable(simulation, nx, ny))
                    {
                        continue;
                    }

                    if (IsRoad(simulation, nx, ny) || TouchesHall(nx, ny))
                    {
                        parent[n] = at;
                        goal = n;
                        break;
                    }

                    parent[n] = at;
                    queue.Enqueue(n);
                }
            }

            if (goal < 0)
            {
                throw new ScenarioException("site: no dirt road reaches the building from the settlement's roads.");
            }

            // The joined network must reach back to the hall within its storehouse reach: dirt costs 1000 a tile
            // and the reach counts tiles, so the trail plus the joined road's hall distance is the exact cost.
            // A trail that joins the hall itself costs only its own tiles.
            int join = IsRoad(simulation, goal % _width, goal / _width) ? hallDist[goal] : 0;
            int cursor = goal;
            int trailLength = 0;
            while (cursor >= 0)
            {
                if (!IsRoad(simulation, cursor % _width, cursor / _width))
                {
                    trailLength++;
                }

                cursor = parent[cursor];
            }

            if (join < 0 || (_hallReach < int.MaxValue / 2 && join + trailLength > _hallReach))
            {
                throw new ScenarioException("site: no dirt road reaches the building from the settlement's roads.");
            }

            // Walk back to the footprint, dropping the network tile the path joins: the road ends adjacent to it.
            // The goal itself is included when it is not a road yet (a single tile joining the hall, for example).
            var trail = new List<TileCoord>();
            int tip = goal;
            while (tip >= 0)
            {
                if (!IsRoad(simulation, tip % _width, tip / _width))
                {
                    trail.Add(new TileCoord(tip % _width, tip / _width));
                }

                tip = parent[tip];
            }

            trail.Reverse();
            return [.. trail];
        }

        // The ring around a footprint without its four corners: the tiles whose road gives the building access.
        private List<TileCoord> Ring(int x, int y, int width, int height)
        {
            var ring = new List<TileCoord>();
            for (int ty = y - 1; ty <= y + height; ty++)
            {
                for (int tx = x - 1; tx <= x + width; tx++)
                {
                    bool outsideColumns = tx < x || tx >= x + width;
                    bool outsideRows = ty < y || ty >= y + height;
                    if (outsideColumns == outsideRows)
                    {
                        continue;
                    }

                    if (tx < 0 || ty < 0 || tx >= _width || ty >= _height)
                    {
                        continue;
                    }

                    ring.Add(new TileCoord(tx, ty));
                }
            }

            return ring;
        }

        private bool IsRoad(ISimulation simulation, int x, int y) =>
            _roadTiles[(y * _width) + x] || simulation.Query.Roads.RoadAt(x, y) is not null;

        // Whether a tile touches the hall's footprint along an edge (corners do not join the road network).
        private bool TouchesHall(int x, int y) =>
            ((x == _hall.X - 1 || x == _hall.X + _hallWidth) && y >= _hall.Y && y < _hall.Y + _hallHeight)
            || ((y == _hall.Y - 1 || y == _hall.Y + _hallHeight) && x >= _hall.X && x < _hall.X + _hallWidth);

        // Whether a dirt road may ever pave a tile: the same ground rules the road validator enforces, minus
        // occupancy. The bank flood uses this too, so a picked site is always joinable (no peaks or locked highlands
        // between the hall and the building that roads cannot cross).
        private static bool RoadGround(ContentDb content, ISimulation simulation, int x, int y)
        {
            TileInfo tile = simulation.Query.Map.GetTile(x, y);
            if (tile.Has(TileFeatures.River))
            {
                return false;
            }

            TerrainDef terrain = content.Get(tile.Terrain);
            if (!terrain.Buildable || terrain.BlocksRoads)
            {
                return false;
            }

            if (terrain.Access is { } access && !simulation.Query.Progression.HasAccess(access))
            {
                return false;
            }

            return true;
        }

        private bool RoadPaveable(ISimulation simulation, int x, int y)
        {
            if (!RoadGround(_content, simulation, x, y))
            {
                return false;
            }

            foreach (Rect reserved in _reserved)
            {
                if (x >= reserved.X && x < reserved.X + reserved.Width && y >= reserved.Y && y < reserved.Y + reserved.Height)
                {
                    return false;
                }
            }

            return simulation.Query.Map.BuildingAt(x, y).IsNone || IsRoad(simulation, x, y);
        }

        // The Chebyshev distance from a footprint to the hall's footprint (0 when they touch or overlap).
        private int ChebyshevToHall(int x, int y, int width, int height)
        {
            int dx = 0;
            if (x + width - 1 < _hall.X) { dx = _hall.X - (x + width - 1); }
            else if (x > _hall.X + _hallWidth - 1) { dx = x - (_hall.X + _hallWidth - 1); }
            int dy = 0;
            if (y + height - 1 < _hall.Y) { dy = _hall.Y - (y + height - 1); }
            else if (y > _hall.Y + _hallHeight - 1) { dy = y - (_hall.Y + _hallHeight - 1); }
            return System.Math.Max(dx, dy);
        }

        // Road-tile distances from the hall in tiles, over the roads laid so far (simulated and planned): the
        // storehouse field seeds from the hall's adjacent roads, so this is the reach each new connector joins.
        // Unreached tiles keep -1. Computed once per search; the network only grows on success.
        private int[] HallDistances(ISimulation simulation)
        {
            int[] dist = new int[_width * _height];
            Array.Fill(dist, -1);
            var queue = new Queue<int>();
            for (int y = _hall.Y - 1; y <= _hall.Y + _hallHeight; y++)
            {
                for (int x = _hall.X - 1; x <= _hall.X + _hallWidth; x++)
                {
                    if (x < 0 || y < 0 || x >= _width || y >= _height) { continue; }
                    if (!TouchesHall(x, y) || !IsRoad(simulation, x, y)) { continue; }
                    int s = (y * _width) + x;
                    if (dist[s] < 0)
                    {
                        dist[s] = 0;
                        queue.Enqueue(s);
                    }
                }
            }

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                int ax = at % _width;
                int ay = at / _width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ax + _bx[d];
                    int ny = ay + _by[d];
                    if ((uint)nx >= (uint)_width || (uint)ny >= (uint)_height) { continue; }
                    int n = (ny * _width) + nx;
                    if (dist[n] >= 0 || !IsRoad(simulation, nx, ny)) { continue; }
                    dist[n] = dist[at] + 1;
                    queue.Enqueue(n);
                }
            }

            return dist;
        }

        private bool BankReaches(int x, int y, int width, int height)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (!_reachable[((y + dy) * _width) + x + dx])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool[] Flood(ContentDb content, ISimulation simulation, int width, int height, TileCoord hall)
        {
            bool[] reached = new bool[width * height];
            int[] stack = new int[width * height];
            int top = 0;
            stack[top++] = (hall.Y * width) + hall.X;
            reached[(hall.Y * width) + hall.X] = true;
            while (top > 0)
            {
                int at = stack[--top];
                int x = at % width;
                int y = at / width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + _bx[d];
                    int ny = y + _by[d];
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        continue;
                    }

                    int n = (ny * width) + nx;
                    if (reached[n] || !RoadGround(content, simulation, nx, ny))
                    {
                        continue;
                    }

                    reached[n] = true;
                    stack[top++] = n;
                }
            }

            return reached;
        }

        // A resolved footprint: commands enqueue before they apply, so later searches must treat earlier picks as
        // occupied even though the simulation has not applied them yet. A rejected pick leaves a phantom reservation,
        // which only makes the search more conservative.
        private readonly record struct Rect(int X, int Y, int Width, int Height);

        // Whether a footprint would cover a connector already planned this run: roads block buildings, so later
        // footprints keep clear of earlier connectors (which the simulation has not applied yet either).
        private bool OverlapsRoad(int x, int y, int width, int height)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (_roadTiles[((y + dy) * _width) + x + dx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsReserved(int x, int y, int width, int height)
        {
            foreach (Rect reserved in _reserved)
            {
                if (x < reserved.X + reserved.Width && reserved.X < x + width
                    && y < reserved.Y + reserved.Height && reserved.Y < y + height)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed record PlacementCandidate(int X, int Y, Rotation Rotation, int Width, int Height, PlacementResult Validation);
    }

    private sealed class ContentIdJsonConverter<TId>(ContentDb content) : JsonConverter<TId>
        where TId : struct, IContentId<TId>
    {
        public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"A {typeof(TId).Name} must be the item's string id, such as \"timber\".");
            }

            string name = reader.GetString() ?? string.Empty;
            return content.TryGet(name, out TId id)
                ? id
                : throw new JsonException($"No {typeof(TId).Name} named \"{name}\" in the loaded content.");
        }

        public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(content.NameOf(value));
    }
}
