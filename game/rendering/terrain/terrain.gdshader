// The island surface: flat-shaded terrain from per-face normals and vertex colours, with an optional tile-grid
// overlay (view_grid_toggle, off by default) and banded toon light (M2-29: exactly three diffuse bands — lit, mid,
// shade — with soft long shadows). Culling is off because neighbouring chunk borders share edges but never vertices;
// the lighting comes from GEOMETRY normals, never from winding. The key light bands here; the cool sky fill and the
// warm late-afternoon grade come from the scene ambient and the look post pass.
shader_type spatial;
render_mode cull_disabled;

uniform bool show_grid = false;
uniform vec4 grid_color : source_color = vec4(0.08, 0.1, 0.08, 1.0);

// The late-afternoon warmth on the neutral key, linear space (the light.key family); the post pass
// grades the frame warmer still. The scene sun stays neutral white in every mode.
uniform vec3 warm_key = vec3(1.3, 1.0, 1.0); // M2-32 calibration lift (hand-drawn only, gated below).

// Beach-blend mask (M2-32): baked coast distance, 1.0 on shoreline land fading inland, for smooth beach
// transitions. Blended only when the look is on; classic keeps its hard tile edges pixel-exact.
uniform sampler2D coast_tex : filter_linear, repeat_disable;
uniform vec2 coast_map_size = vec2(128.0, 128.0);
uniform vec3 beach_color : source_color = vec3(0.85, 0.78, 0.58);

// Seasonal tint and snow (M2-24): one multiplier plus a snow cover, both no-ops in classic (gated by
// look_amount below), so the M1 look stays pixel-identical. Snow settles only above the waterline, read from
// the world height, so the seabed under the opaque sea never shows snow.
uniform vec3 season_tint : source_color = vec3(1.0, 1.0, 1.0);
uniform float season_snow = 0.0;
uniform vec3 snow_color : source_color = vec3(1.0, 0.972549, 0.909804); // paper.highlight.

// The scene ambient (Main.tscn WorldEnvironment colour x energy) lifts every band; the material setup reads
// the live environment so the single source stays in the scene.
uniform vec3 ambient_color : source_color = vec3(0.51, 0.53, 0.55);

// The look toggle: 0 restores the M1 smooth shading, 1 bands the key light. The renderer sets it from the
// active look mode; the default keeps classic runs on the M1 materials.
uniform float look_amount = 0.0;

// Band boundaries in NdotL: above the high line is lit, between the lines is mid, below the low line is shade.
// The small softness anti-aliases the boundary over about a pixel instead of dithering it.
uniform float band_high = 0.5;
uniform float band_low = 0.0;
uniform float band_softness = 0.02;
uniform float band_mid = 0.75;
uniform float band_shade = 0.45;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 p, float width_px) {
	vec2 distance_to_line = abs(fract(p - 0.5) - 0.5) / max(fwidth(p), vec2(0.0001));
	return 1.0 - min(min(distance_to_line.x, distance_to_line.y) / width_px, 1.0);
}

void fragment() {
	float beachBlend = texture(coast_tex, world_pos.xz / coast_map_size).r;
	ALBEDO = mix(COLOR.rgb, beach_color, beachBlend * look_amount);
	// The seasonal grade multiplies every lit band through ALBEDO; the snow cover then mixes toward paper white.
	ALBEDO *= mix(vec3(1.0), season_tint, look_amount);
	// Snow settles on open ground: the red channel of the (linear) vertex colour separates pale open tiles
	// (plains, fields, hills) from dark forest greens, which keep their contrast, so the biome pattern reads
	// through the cover. Ceiling 0.22: a light tint, never a whiteout. Snow stays warm paper white.
	float openGround = smoothstep(0.08, 0.20, COLOR.rgb.r);
	float snowMask = smoothstep(-0.05, 0.45, world_pos.y) * season_snow * openGround * look_amount;
	ALBEDO = mix(ALBEDO, snow_color, clamp(snowMask, 0.0, 0.22));
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;

	if (show_grid) {
		float pixels_per_tile = 1.0 / max(max(fwidth(world_pos.x), fwidth(world_pos.z)), 0.0001);
		float fade = clamp((pixels_per_tile - 3.0) / 6.0, 0.0, 1.0);
		float line = grid_line(world_pos.xz, 1.2);
		ALBEDO = mix(ALBEDO, grid_color.rgb, line * 0.6 * fade);
	}
}

// Banded key light with the engine's shadows: every closed solid reads as flat watercolour facets, and long soft
// shadows still fall where the directional light is occluded. Ambient and sky fill apply normally around this.
void light() {
	float ndl = dot(normalize(NORMAL), normalize(LIGHT));
	float band = mix(band_shade, band_mid, smoothstep(band_low - band_softness, band_low + band_softness, ndl));
	band = mix(band, 1.0, smoothstep(band_high - band_softness, band_high + band_softness, ndl));
	float key = mix(clamp(ndl, 0.0, 1.0), band, look_amount);
	vec3 key_tint = mix(vec3(1.0), warm_key, look_amount);
	DIFFUSE_LIGHT += ALBEDO * (LIGHT_COLOR * key_tint * key * ATTENUATION + ambient_color);
}

