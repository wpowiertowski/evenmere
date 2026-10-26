// Flat river strips at each river tile's own elevation, just under the bank. Diagonal scrolling bands read as flow;
// the world-space position keeps the pattern continuous across tiles. One compact shader shared by the whole strip
// mesh.
//
// M2-29: the river is water now (terrain.river), not a ditch — soft inked banks along the strip edges (the per-vertex
// COLOR.r bank factor counts the non-river neighbours of each corner, so the ink follows the banks and fades where
// the river runs on), foam where the river meets the sea, and the same three-band toon key light as the terrain.
shader_type spatial;
render_mode blend_mix;

// The M1 strip colours; the renderer sets the water-blue pair when the hand-drawn look is active.
#include "river_common.gdshaderinc"

// M2-32: warm tint stays here (classic neutral) versus river_hd.gdshader (calibration lift).
uniform vec3 warm_key = vec3(1.0, 1.0, 1.0); // Neutral: the warmth lives on the scene sun (LookScene).

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float along = (world_pos.x + world_pos.z) * 2.0 - TIME * 1.2;
	float band_flow = sin(along) * 0.5 + 0.5;
	float glint = smoothstep(0.85, 1.0, sin(along * 2.7 + 1.3) * 0.5 + 0.5);
	vec3 col = mix(river_color, streak_color, band_flow * 0.12 + glint * 0.12);

	// Painted ripple detail over the flow bands, continuous with the sea at the same world scale.
	float painted = clamp((texture(ripple_tex, world_pos.xz / RIPPLE_WORLD_TILES).r - RIPPLE_MID) / (1.0 - RIPPLE_MID) * 1.2, 0.0, 1.0);
	col = mix(col, bank_ink, painted * 0.25 * has_ripple_tex * look_amount);

	float mask = COLOR.r * 15.0;
	vec2 local = fract(world_pos.xz);
	float bank_dist = 10.0;
	if (mod(mask, 2.0) > 0.5) { bank_dist = min(bank_dist, local.y); }
	if (mod(floor(mask / 2.0), 2.0) > 0.5) { bank_dist = min(bank_dist, 1.0 - local.x); }
	if (mod(floor(mask / 4.0), 2.0) > 0.5) { bank_dist = min(bank_dist, 1.0 - local.y); }
	if (mod(floor(mask / 8.0), 2.0) > 0.5) { bank_dist = min(bank_dist, local.x); }
	float halo = (1.0 - smoothstep(0.0, bank_halo_tiles, bank_dist)) * look_amount;
	col = mix(col, foam_color, halo * 0.3);
	float line = (1.0 - smoothstep(0.0, bank_line_tiles, bank_dist)) * look_amount;
	col = mix(col, bank_ink, line * 0.7);

	ALBEDO = col;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	ALPHA = 0.92;
}

// Flat water in the hand-drawn look: no facet banding on the strips (their face normals would stripe tile to
// tile). Shadows from banks and bridges still fall across the surface through the attenuation.
void light() {
	float ndl = dot(normalize(NORMAL), normalize(LIGHT));
	float key = mix(clamp(ndl, 0.0, 1.0), 1.0, look_amount);
	DIFFUSE_LIGHT += ALBEDO * (LIGHT_COLOR * warm_key * key * ATTENUATION + ambient_color);
}
