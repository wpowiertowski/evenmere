using Evenmere.Core.Time;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Look;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Seasons;

/// <summary>
/// The year's visible turn (M2-24): a seasonal tint and snow cover on the terrain plus a seasonal key-light colour,
/// all blended over two game days, and a subtle blossom drift in spring. The season is read from
/// <see cref="SimHost.Clock"/> between ticks; no sim state is ever held. The tint and snow ride opaque terrain
/// uniforms (gated by the look, so classic stays pixel-identical); the blossom quads are fully opaque, so nothing
/// here fights the look's post pass. A debug-only <c>--season=&lt;name&gt;</c> flag forces one season for screenshots.
/// </summary>
public sealed partial class SeasonRenderer : Node3D
{
    private const string SeasonPrefix = "--season=";

    // Blossom petals: tiny opaque capsules (petal-like, never square) in soft pink, spotted only where trees
    // are — orchard buildings first, then forest tiles — a few dozen at most. Opaque, so the look's post pass
    // inks them like the rest of the frame.
    private const int MaxBlossomEmitters = 10;
    private const int PetalsPerEmitter = 8;
    private const float BlossomRefreshSeconds = 2f;
    private const float BlossomLifetime = 5f;

    // Soft petal pink. There is no blossom token in the shared palette (read-only here); the proposed token
    // is <c>flower.blossom</c>, noted in M2-24 instead of editing the palette.
    private static readonly Color _blossomColour = new(0.95f, 0.70f, 0.75f);

    private SimHost? _host;
    private TerrainRenderer? _terrain;
    private DirectionalLight3D? _sun;
    private readonly List<GpuParticles3D> _petals = new();
    private readonly List<Vector3> _blossomSpots = new();
    private float _blossomTimer;
    private Season? _forced;

    /// <inheritdoc />
    public override void _Ready()
    {
        Node? scene = GetTree().CurrentScene;
        _host = scene?.GetNodeOrNull<SimHost>("SimHost");
        _terrain = scene?.GetNodeOrNull<TerrainRenderer>("Terrain");
        _sun = scene?.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (OS.IsDebugBuild())
        {
            _forced = ForcedSeason(OS.GetCmdlineUserArgs());
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        GameClock clock = _host.Clock;
        Season season = _forced ?? clock.Season;
        int day = _forced.HasValue ? SeasonPalette.BlendDays : clock.DayOfSeason;
        SeasonTint tint = SeasonPalette.Sample(season, day);

        _terrain?.SetSeasonTint(new Color(tint.TintR, tint.TintG, tint.TintB), tint.Snow);

        if (_sun is not null && LookSettings.ActiveLook == LookMode.HandDrawn)
        {
            // The same two-day blend the palette samples, through the shared seasonal light colour.
            float blend = day >= SeasonPalette.BlendDays ? 1f : day / (float)SeasonPalette.BlendDays;
            _sun.LightColor = LookScene.SeasonKeyColor(SeasonPalette.Previous(season))
                .Lerp(LookScene.SeasonKeyColor(season), blend);
        }

        _blossomTimer -= (float)delta;
        if (_blossomTimer <= 0f)
        {
            _blossomTimer = BlossomRefreshSeconds;
            RefreshBlossomSpots();
        }

        UpdateBlossom(tint.Blossom);
    }

    // Parses --season=<spring|summer|autumn|winter> from this file's own flag, so no other file is touched.
    // Unknown values warn and keep the live season. Release builds never call this.
    private static Season? ForcedSeason(string[] userArgs)
    {
        foreach (string arg in userArgs)
        {
            if (!arg.StartsWith(SeasonPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = arg[SeasonPrefix.Length..];
            foreach (Season season in Enum.GetValues<Season>())
            {
                if (string.Equals(season.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    GD.Print($"[Evenmere] Seasons: forcing {season} for screenshots.");
                    return season;
                }
            }

            GD.PushWarning($"[Evenmere] ignoring unknown --season '{name}'; want spring, summer, autumn or winter.");
        }

        return null;
    }

    private void UpdateBlossom(float weight)
    {
        while (_petals.Count < _blossomSpots.Count)
        {
            _petals.Add(CreateBlossomPetal());
        }

        int shown = weight > 0f ? _blossomSpots.Count : 0;
        for (int i = 0; i < _petals.Count; i++)
        {
            GpuParticles3D emitter = _petals[i];
            if (i < shown)
            {
                emitter.Position = _blossomSpots[i];
                emitter.Visible = true;
                emitter.Emitting = true;
            }
            else
            {
                emitter.Emitting = false;
                emitter.Visible = false;
            }
        }
    }

    // Blossom spots in deterministic order: orchard crowns first (ascending building ID), then forest tiles
    // strided evenly across the island, so the drift spreads instead of clumping. Rebuilt every couple of
    // seconds; trees never move, but orchards come and go.
    private void RefreshBlossomSpots()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        _blossomSpots.Clear();
        foreach (BuildingInfo building in _host.Query.Map.ActiveBuildings)
        {
            if (_blossomSpots.Count >= MaxBlossomEmitters)
            {
                break;
            }

            if (_host.Content.Buildings.Names[building.Type.Value] != "orchard")
            {
                continue;
            }

            GroundSampler.FootprintHeights(
                _host.Query.Map,
                _host.Content,
                building.Origin.X,
                building.Origin.Y,
                building.Width,
                building.Height,
                out float highest,
                out _);
            _blossomSpots.Add(new Vector3(
                building.Origin.X + (building.Width / 2f),
                highest + 0.8f,
                building.Origin.Y + (building.Height / 2f)));
        }

        if (_blossomSpots.Count < MaxBlossomEmitters)
        {
            int forests = CountForestTiles();
            int want = MaxBlossomEmitters - _blossomSpots.Count;
            int step = Math.Max(1, forests / Math.Max(1, want));
            int seen = 0;
            int taken = 0;
            IMapQueries map = _host.Query.Map;
            for (int y = 0; y < map.Height && taken < want; y++)
            {
                for (int x = 0; x < map.Width && taken < want; x++)
                {
                    if (!IsForestTile(map, x, y))
                    {
                        continue;
                    }

                    if (seen % step == 0)
                    {
                        _blossomSpots.Add(new Vector3(
                            x + 0.5f,
                            TerrainSurface.GroundHeight(map.GetTile(x, y).Elevation) + 1f,
                            y + 0.5f));
                        taken++;
                    }

                    seen++;
                }
            }
        }
    }

    // Tiny camera-facing capsules in petal pink, drifting down around one grove: opaque, so the look's post
    // pass inks them like the rest of the frame instead of erasing them.
    private GpuParticles3D CreateBlossomPetal()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = _blossomColour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var petal = new CapsuleMesh
        {
            Radius = 0.045f,
            Height = 0.135f,
            RadialSegments = 6,
            Rings = 1,
            Material = material,
        };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(2f, 1.2f, 2f),
            Direction = new Vector3(0.3f, -0.4f, 0.1f),
            Spread = 25f,
            InitialVelocityMin = 0.3f,
            InitialVelocityMax = 0.7f,
            Gravity = new Vector3(0f, -0.15f, 0f),
            ScaleMin = 0.8f,
            ScaleMax = 1.2f,
        };
        var blossom = new GpuParticles3D
        {
            Name = $"Blossom_{_petals.Count}",
            Amount = PetalsPerEmitter,
            Lifetime = BlossomLifetime,
            Preprocess = BlossomLifetime,
            Explosiveness = 0f,
            Randomness = 0.6f,
            ProcessMaterial = process,
            DrawPass1 = petal,
            VisibilityAabb = new Aabb(new Vector3(-4f, -4f, -4f), new Vector3(8f, 8f, 8f)),
        };
        AddChild(blossom);
        GD.Print($"[Evenmere] Seasons: blossom drift on ({_petals.Count + 1} grove(s), {PetalsPerEmitter} petals each).");
        return blossom;
    }

    private int CountForestTiles()
    {
        if (_host is not { IsRunning: true })
        {
            return 0;
        }

        int count = 0;
        IMapQueries map = _host.Query.Map;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (IsForestTile(map, x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool IsForestTile(IMapQueries map, int x, int y)
    {
        if (_host is null)
        {
            return false;
        }

        TileInfo tile = map.GetTile(x, y);
        return _host.Content.Terrains.Names[tile.Terrain.Value] == "forest";
    }
}
