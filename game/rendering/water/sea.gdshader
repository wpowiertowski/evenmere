// One animated sea plane for the whole island. Gentle vertex waves move the surface; the baked shoreline texture
// (R = distance to land in tiles, G = water type, B = land mask) paints foam near the shore and tints reef and deep
// water. The plane is translucent so the sea floor and reef shelf meshes faintly show through in the shallows.
//
// M2-29 hand-drawn water: hand-placed ripple strokes (procedural, wobbled, dense near the shore and tangent-aligned
// near coasts), a broken dash on the foam edge, and the same three-band toon key light as the terrain, so the water
// reads as paint. Shadows fall across the surface through the engine attenuation.
shader_type spatial;
render_mode blend_mix;

uniform sampler2D shore_tex : filter_linear_mipmap, repeat_disable;
uniform vec2 map_size = vec2(128.0, 128.0);
uniform vec3 shallow_color : source_color = vec3(0.23, 0.52, 0.58);
uniform vec3 reef_color : source_color = vec3(0.2, 0.6, 0.55);
uniform vec3 deep_color : source_color = vec3(0.04, 0.2, 0.36);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.95);
uniform vec3 ripple_color : source_color = vec3(0.18, 0.29, 0.33);
uniform float foam_width_tiles = 2.5;
uniform float max_distance_tiles = 12.0;

// The late-afternoon warmth on the neutral key, linear space (the light.key family); the post pass
// grades the frame warmer still. The scene sun stays neutral white in every mode.
uniform vec3 warm_key = vec3(1.0, 1.0, 1.0); // Neutral: the warmth lives on the scene sun (LookScene).

// The scene ambient (Main.tscn WorldEnvironment colour x energy) lifts every band; the material setup reads
// the live environment so the single source stays in the scene.
uniform vec3 ambient_color : source_color = vec3(0.51, 0.53, 0.55);

// Band boundaries in NdotL, matching the terrain pass.
uniform float band_high = 0.5;
uniform float band_low = 0.0;
uniform float band_softness = 0.02;
uniform float band_mid = 0.75;
uniform float band_shade = 0.45;

// The look toggle: 0 restores the M1 smooth shading and still water, 1 bands the key light and draws the
// ripple strokes with the broken foam. The renderer sets it from the active look mode.
uniform float look_amount = 0.0;

// Ripple strokes: the ART-04 hand-placed arcs where bound (about 40 per tile at a 12-tile period, so roughly one
// stroke per four tiles), procedural wobbled arcs otherwise. The painted tile runs west-east; near shores the
// transposed sample blends in by the shore tangent, which stays seamless (transpose maps tiles to tiles).
uniform sampler2D ripple_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float has_ripple_tex = 0.0;
const float RIPPLE_MID = 0.2159;
const float RIPPLE_WORLD_TILES = 12.0;
uniform float stroke_density_offshore = 0.5;
uniform float stroke_density_shore = 1.0;
uniform float stroke_sharpness = 0.9;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += sin(TIME * 0.8 + VERTEX.x * 0.35 + VERTEX.z * 0.27) * 0.06
		+ sin(TIME * 1.7 + VERTEX.z * 0.9 - VERTEX.x * 0.5) * 0.03;
}

float hash12(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

void fragment() {
	vec2 uv = world_pos.xz / map_size;
	vec4 shore = texture(shore_tex, uv);
	float dist_tiles = shore.r * max_distance_tiles;
	float water_type = shore.g;
	float is_land = shore.b;

	vec3 col = mix(shallow_color, reef_color, smoothstep(0.25, 0.6, water_type));
	col = mix(col, deep_color, smoothstep(0.6, 1.0, water_type));

	// Foam with a broken hand-drawn dash along the shore.
	float foam_band = 1.0 - smoothstep(0.0, foam_width_tiles, dist_tiles);
	float foam_edge = sin(dist_tiles * 4.0 - TIME * 1.6) * 0.5 + 0.5;
	float dash = step(0.4, fract(dot(world_pos.xz, vec2(0.9, 0.45)) * 0.5));
	float dash_mix = mix(1.0, 0.5 + 0.5 * dash, look_amount);
	float foam = foam_band * (0.55 + 0.45 * foam_edge) * dash_mix * (1.0 - is_land);
	col = mix(col, foam_color, clamp(foam, 0.0, 1.0) * 0.8);

	// Ripple strokes over the foam: wobbled arcs, tangent-aligned near the coast, west-east offshore.
	vec2 texel = vec2(1.0 / map_size.x, 1.0 / map_size.y);
	float gx = texture(shore_tex, uv + vec2(texel.x, 0.0)).r - texture(shore_tex, uv - vec2(texel.x, 0.0)).r;
	float gy = texture(shore_tex, uv + vec2(0.0, texel.y)).r - texture(shore_tex, uv - vec2(0.0, texel.y)).r;
	vec2 grad = vec2(gx, gy) * max_distance_tiles;
	vec2 tangent = length(grad) > 0.02 ? normalize(vec2(-grad.y, grad.x)) : vec2(1.0, 0.0);
	vec2 wobbled = world_pos.xz + 0.35 * vec2(sin(world_pos.z * 1.7 + TIME * 0.35), cos(world_pos.x * 1.4 - TIME * 0.3));
	float along = dot(wobbled, tangent);
	float procedural = smoothstep(stroke_sharpness, 0.995, sin(along * 2.0) * 0.5 + 0.5);
	float density = mix(stroke_density_shore, stroke_density_offshore, smoothstep(0.0, 4.0, dist_tiles));
	procedural *= step(hash12(floor(world_pos.xz / 4.0)), density);
	vec2 ruv = world_pos.xz / RIPPLE_WORLD_TILES;
	float along_u = (texture(ripple_tex, ruv).r - RIPPLE_MID) / (1.0 - RIPPLE_MID);
	float along_v = (texture(ripple_tex, vec2(ruv.y, ruv.x)).r - RIPPLE_MID) / (1.0 - RIPPLE_MID);
	float painted = clamp(mix(along_u, along_v, abs(tangent.y)) * 1.4, 0.0, 1.0);
	float stroke = mix(procedural, painted, has_ripple_tex);
	col = mix(col, ripple_color, stroke * 0.55 * (1.0 - is_land) * look_amount);

	ALBEDO = col;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	ALPHA = 0.88;
}

// The water keeps its glints: a small hand-rolled specular beside the banded diffuse, both shadowed.
void light() {
	float ndl = dot(normalize(NORMAL), normalize(LIGHT));
	float band = mix(band_shade, band_mid, smoothstep(band_low - band_softness, band_low + band_softness, ndl));
	band = mix(band, 1.0, smoothstep(band_high - band_softness, band_high + band_softness, ndl));
	float key = mix(clamp(ndl, 0.0, 1.0), band, look_amount);
	DIFFUSE_LIGHT += ALBEDO * (LIGHT_COLOR * warm_key * key * ATTENUATION + ambient_color);
	vec3 half_vec = normalize(normalize(LIGHT) + normalize(VIEW));
	float spec_power = mix(30.0, 80.0, look_amount);
	float spec = pow(clamp(dot(normalize(NORMAL), half_vec), 0.0, 1.0), spec_power);
	float spec_amount = mix(0.6, 0.35, look_amount);
	SPECULAR_LIGHT += LIGHT_COLOR * spec * spec_amount * ATTENUATION;
}
