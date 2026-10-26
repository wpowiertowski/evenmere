// The fog sheet (M2-23): a calm paper-white haze over unrevealed regions, drawn after the look
// post pass (priority 20, shared with the overlays) so the inked frame survives underneath. Unshaded,
// so it reads the same under the hand-drawn look and --look=classic. The ink edge lives in the
// outline meshes (FogRenderer), not here: one soft sheet plus one crisp edge.
// The sheets write depth (unlike most translucents): on slopes the view ray can cross several
// neighbouring quads, and without self-occlusion their alphas would stack toward opaque grey.
// With depth, the nearest quad wins and every angle blends exactly once, like the flat lowlands.
// The fade uniform drives the reveal animation from code; hover never touches this material.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always;

uniform vec4 haze_color : source_color = vec4(0.957, 0.941, 0.878, 0.7);
uniform float fade : hint_range(0.0, 1.0) = 1.0;

void fragment() {
	// A breath of paper grain from the sheet UVs (they span the map), so large fogged
	// regions never read as flat plastic. One swell per ~50 tiles: broad enough to stay soft
	// at street zoom instead of aliasing into a per-tile quilt, ±1% luminance, far below the ink edge.
	float grain = sin(UV.x * 43.0) * sin(UV.y * 37.0);
	vec3 paper = haze_color.rgb * (0.995 + 0.005 * grain);
	ALBEDO = paper;
	ALPHA = haze_color.a * fade;
}
