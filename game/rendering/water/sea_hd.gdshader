// One animated sea plane for the whole island. Gentle vertex waves move the surface; the baked shoreline texture
// (R = distance to land in tiles, G = water type, B = land mask) paints foam near the shore and tints reef and deep
// water. The plane is translucent so the sea floor and reef shelf meshes faintly show through in the shallows.
//
// M2-29 hand-drawn water: hand-placed ripple strokes (procedural, wobbled, dense near the shore and tangent-aligned
// near coasts), a broken dash on the foam edge, and the same three-band toon key light as the terrain, so the water
// reads as paint. Shadows fall across the surface through the engine attenuation.
shader_type spatial;
// M2-32: the hand-drawn sea is opaque. The look post pass composites the opaque frame over itself,
// so transparent water would be erased (the post quad paints the pre-transparency capture over it).
// Classic keeps blend_mix in sea.gdshader; the colours below are shared data, not duplicated logic.

#include "sea_common.gdshaderinc"

// M2-32: hand-drawn overrides of the shared defaults above (foam width, warm tint, offshore density).
// Classic keeps its M1 values in sea.gdshader.
uniform float foam_width_tiles = 1.2;
uniform vec3 warm_key = vec3(1.3, 1.0, 1.0); // M2-32 calibration lift, hand-drawn only.
uniform float stroke_density_offshore = 0.18;

// M2-32 hand-drawn top-up (currently zero; dialled in by measurement).
uniform vec3 ambient_hd : source_color = vec3(0.0, 0.0, 0.0);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += sin(TIME * 0.8 + VERTEX.x * 0.35 + VERTEX.z * 0.27) * 0.06
		+ sin(TIME * 1.7 + VERTEX.z * 0.9 - VERTEX.x * 0.5) * 0.03;
}

void fragment() {
	vec2 uv = world_pos.xz / map_size;
	vec4 shore = texture(shore_tex, uv);
	float dist_tiles = shore.r * max_distance_tiles;
	float water_type = shore.g;
	float is_land = shore.b;

	// Past the baked field the island sits in open sea: hold the deep-water tone. The 4.5x plane
	// extends past every view, and the background clear colour matches this tone, so there is no seam.
	vec2 centred = (world_pos.xz - 0.5 * map_size) / map_size;
	if (max(abs(centred.x), abs(centred.y)) > 0.5) {
		dist_tiles = max_distance_tiles;
		water_type = 1.0;
		is_land = 0.0;
	}

	// The shallow band follows the coastline only: far from land the water is exactly the deep tone,
	// so the baked map border leaves no step against the open sea held past the field.
	water_type = mix(water_type, 1.0, smoothstep(4.0, 10.0, dist_tiles));

	vec3 col = mix(shallow_color, reef_color, smoothstep(0.25, 0.6, water_type));
	col = mix(col, deep_color, smoothstep(0.6, 1.0, water_type));

	// The land mask interpolates across the tile boundary (linear filtering), so the waterline follows
	// a smooth contour instead of the stepped tile edge.
	float land_soft = smoothstep(0.35, 0.65, is_land);
	float water_mask = 1.0 - land_soft;

	// Foam with a broken hand-drawn dash along the shore.
	float foam_band = 1.0 - smoothstep(0.0, foam_width_tiles, dist_tiles);
	float foam_edge = sin(dist_tiles * 4.0 - TIME * 1.6) * 0.5 + 0.5;
	float dash = step(0.4, fract(dot(world_pos.xz, vec2(0.9, 0.45)) * 0.5));
	float dash_mix = mix(1.0, 0.5 + 0.5 * dash, look_amount);
	float foam = foam_band * (0.55 + 0.45 * foam_edge) * dash_mix * water_mask;
	col = mix(col, foam_color, clamp(foam, 0.0, 1.0) * 0.8);

	// A soft inked waterline on the smooth contour.
	float coast_line = smoothstep(0.42, 0.5, is_land) * (1.0 - smoothstep(0.5, 0.58, is_land));
	coast_line *= 1.0 - smoothstep(1.5, 3.0, dist_tiles);
	col = mix(col, ripple_color, coast_line * 0.7);

	// Ripple strokes over the foam: wobbled arcs, tangent-aligned near the coast, west-east offshore.
	// Two layers share the tangent: fine strokes for street and default zoom, and a broad painterly
	// layer (about four times the wavelength) that carries the read at overview zoom, where the fine
	// layer would alias. Each fades by the on-screen pixels per tile, so nothing shimmers in between.
	float px_per_tile = 1.0 / max(max(fwidth(world_pos.x), fwidth(world_pos.z)), 0.0001);
	float fine_w = smoothstep(4.0, 9.0, px_per_tile);
	float broad_w = 1.0 - smoothstep(10.0, 22.0, px_per_tile);
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
	float stroke = clamp(procedural * 0.85 + painted * 0.9 * has_ripple_tex, 0.0, 1.0) * fine_w;
	col = mix(col, ripple_color, stroke * 0.55 * water_mask);
	// Broad swells (island zoom): sparse and irregular by construction — a per-cell phase jitter breaks
	// the bands into varied dashes, and the ink stays faint against the deep tone so the sea stays calm.
	vec2 broadCell = floor(world_pos.xz / 5.0);
	float cellPhase = hash12(broadCell + 4.7) * 6.2831;
	float broad = smoothstep(0.7, 0.995, sin(along * 0.45 + 1.7 * sin(along * 0.13 + cellPhase) + cellPhase * 0.5) * 0.5 + 0.5);
	broad *= step(hash12(broadCell + 9.1), density);
	col = mix(col, ripple_color, broad * broad_w * 0.3 * water_mask);
	// A whisper of foam on the broad swells so the open sea reads at island zoom, where dark ink
	// alone would vanish into the deep tone. Kept faint: the sea must stay calm.
	col = mix(col, foam_color, broad * broad_w * 0.05 * water_mask);

	ALBEDO = col;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

// The water keeps its glints: a small hand-rolled specular beside the banded diffuse, both shadowed.
void light() {
	float ndl = dot(normalize(NORMAL), normalize(LIGHT));
	float band = mix(band_shade, band_mid, smoothstep(band_low - band_softness, band_low + band_softness, ndl));
	band = mix(band, 1.0, smoothstep(band_high - band_softness, band_high + band_softness, ndl));
	float key = mix(clamp(ndl, 0.0, 1.0), band, look_amount);
	vec3 key_tint = mix(vec3(1.0), warm_key, look_amount);
	vec3 amb = mix(ambient_color, ambient_color + ambient_hd, look_amount);
	DIFFUSE_LIGHT += ALBEDO * (LIGHT_COLOR * key_tint * key * ATTENUATION + amb);
	vec3 half_vec = normalize(normalize(LIGHT) + normalize(VIEW));
	float spec_power = mix(30.0, 80.0, look_amount);
	float spec = pow(clamp(dot(normalize(NORMAL), half_vec), 0.0, 1.0), spec_power);
	float spec_amount = mix(0.6, 0.35, look_amount);
	SPECULAR_LIGHT += LIGHT_COLOR * spec * spec_amount * ATTENUATION;
}
