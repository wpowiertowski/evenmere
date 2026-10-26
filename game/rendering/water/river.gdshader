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
uniform vec3 river_color : source_color = vec3(0.3, 0.52, 0.55);
uniform vec3 streak_color : source_color = vec3(0.75, 0.88, 0.88);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.95);
uniform vec3 bank_ink : source_color = vec3(0.18, 0.29, 0.33);

// The look toggle: 0 restores the M1 smooth strip with no banks, 1 inks the banks with a foam halo.
uniform float look_amount = 0.0;

// Banks from the per-tile edge mask in COLOR.r (bit 0 north, 1 east, 2 south, 3 west): the line runs along
// exactly the edges that border land, about 1 px soft with a wider foam halo, and never covers the middle.
uniform float bank_halo_tiles = 0.3;
uniform float bank_line_tiles = 0.1;

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
uniform float band_shade = 0.5;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

uniform sampler2D ripple_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float has_ripple_tex = 0.0;
const float RIPPLE_MID = 0.2159;
const float RIPPLE_WORLD_TILES = 12.0;

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
