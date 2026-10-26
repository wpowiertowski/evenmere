// Shared water shader chunks (M2-32): the helpers and uniform declarations used identically by the
// classic and hand-drawn sea shaders. Look-specific values (foam width, warm tint, offshore density)
// stay in each file as uniforms there, so classic keeps its M1 look pixel-exact.
uniform sampler2D shore_tex : filter_linear_mipmap, repeat_disable;
uniform vec2 map_size = vec2(128.0, 128.0);
uniform vec3 shallow_color : source_color = vec3(0.23, 0.52, 0.58);
uniform vec3 reef_color : source_color = vec3(0.2, 0.6, 0.55);
uniform vec3 deep_color : source_color = vec3(0.04, 0.2, 0.36);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.95);
uniform vec3 ripple_color : source_color = vec3(0.18, 0.29, 0.33);
uniform float max_distance_tiles = 12.0;

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
uniform float stroke_density_shore = 1.0;
uniform float stroke_sharpness = 0.9;

float hash12(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}
