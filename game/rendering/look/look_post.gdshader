// The hand-drawn look pass (M2-29): a camera-child quad that composites ink outlines, paper grain and a warm
// grade over the finished frame. It reads the opaque scene colour plus everything transparent drawn before it
// (the quad sits closest to the camera, so it composites last) and replaces the picture: there is no extra
// framebuffer and no compositor plugin.
//
// Ink comes from two honest signals, each at roughly its bible weight (style-guide §2):
//   - depth jumps at the ground radius (~0.5 px taps, ~1 px soft lines): silhouettes, coasts, river banks;
//   - normal angles at the building radius (~1.0 px taps, ~2 px lines): roof and wall creases, frames, props.
// The normal threshold sits above the terrain's facet angles, so facets never draw lines — only closed solids do.
// Taps are clamped inside the frame, so no stray line runs along the screen borders.
//
// ART-04 texture slots: paper_grain_tex and wash_tex carry the painted grain. They default to white (a no-op),
// and the C# side reports has_*_tex so the shader blends painted over procedural instead of multiplying blindly.
// Masks are mid-grey (sRGB 128) data through the standard sampling path: MASK_MID is that grey as linear.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
uniform sampler2D normal_tex : hint_normal_roughness_texture, filter_linear;

uniform vec4 ink_color : source_color = vec4(0.18, 0.15, 0.13, 1.0);
uniform float ground_radius_px = 0.5;
uniform float building_radius_px = 1.0;
uniform float edge_threshold = 0.004;
uniform float edge_softness = 2.0;
uniform float normal_threshold = 0.25;
uniform float normal_softness = 1.8;
uniform float grain_strength = 0.06;
uniform float blotch_strength = 0.10;
uniform float pool_darkening = 0.06;
uniform vec3 warm_tint : source_color = vec3(1.03, 1.0, 0.96);
uniform vec3 cool_shadow : source_color = vec3(0.97, 1.0, 1.05);
uniform sampler2D paper_grain_tex : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D wash_tex : hint_default_white, filter_linear, repeat_enable;
uniform float has_paper_tex = 0.0;
uniform float has_wash_tex = 0.0;
const float MASK_MID = 0.2159;
uniform vec2 texture_tiling = vec2(16.0, 9.0);
uniform vec2 pixel_size = vec2(0.000625, 0.001111);

float hash12(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

// Smooth value noise over a stable lattice, interpolated with a smooth step: no cells, no edges, no tiling.
// Coordinates arrive in aspect-correct screen units (about 8 across the height), so the grain keeps a fixed
// on-screen scale at any window size.
float vnoise(vec2 p)
{
	vec2 cell = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(cell);
	float b = hash12(cell + vec2(1.0, 0.0));
	float c = hash12(cell + vec2(0.0, 1.0));
	float d = hash12(cell + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float wash_noise(vec2 p)
{
	return vnoise(p) * 0.65 + vnoise(p * 2.13 + 7.7) * 0.35;
}

vec2 clamp_tap(vec2 tap_uv, vec2 px)
{
	return clamp(tap_uv, px * 1.5, vec2(1.0) - px * 1.5);
}

void fragment()
{
	vec2 frag = FRAGCOORD.xy;
	vec2 px = pixel_size;

	// Screen-space units for the grain: aspect-correct, resolution-shaped, never repeating across the frame.
	vec2 screenuv = vec2(SCREEN_UV.x * (pixel_size.y / max(pixel_size.x, 0.00001)), SCREEN_UV.y);

	// One rigid wobble per pixel from smooth low-frequency noise, so the ink wanders like a pen, never in blocks.
	float wobble_angle = wash_noise(screenuv * 24.0) * 6.2831853;
	vec2 wobble = vec2(cos(wobble_angle), sin(wobble_angle)) * (0.5 * px * building_radius_px);

	vec2 center_uv = clamp_tap(SCREEN_UV, px);
	float center_depth = texture(depth_tex, center_uv).x;
	vec3 center_normal = normalize(texture(normal_tex, center_uv).rgb * 2.0 - 1.0);

	float depth_edge = 0.0;
	float normal_edge = 0.0;
	vec2 gr = ground_radius_px * px;
	vec2 br = building_radius_px * px;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.7853982;
		vec2 dir = vec2(cos(a), sin(a));
		vec2 guv = clamp_tap(SCREEN_UV + dir * gr + wobble * 0.5, px);
		depth_edge = max(depth_edge, abs(texture(depth_tex, guv).x - center_depth));
		vec2 buv = clamp_tap(SCREEN_UV + dir * br + wobble, px);
		vec3 tap_normal = normalize(texture(normal_tex, buv).rgb * 2.0 - 1.0);
		normal_edge = max(normal_edge, 1.0 - dot(center_normal, tap_normal));
	}
	float ink = max(
		smoothstep(edge_threshold, edge_threshold * edge_softness, depth_edge),
		smoothstep(normal_threshold, normal_threshold * normal_softness, normal_edge));

	vec3 col = texture(screen_tex, SCREEN_UV).rgb;

	// Paper tooth: fine white noise broken up by a slow organic variation, amplitude-capped, static in screen space.
	float tooth = hash12(frag) * (0.55 + 0.45 * wash_noise(screenuv * 40.0));
	float paper_dev = (texture(paper_grain_tex, SCREEN_UV * texture_tiling).r - MASK_MID) * 4.0;
	float paper_effect = mix(tooth, clamp(paper_dev + tooth * 0.3, -1.0, 1.0), has_paper_tex);
	col *= 1.0 - grain_strength * paper_effect;

	// Watercolour blotches: smooth low-frequency lightness variation inside large flat areas, no cells anywhere.
	float wash_dev = (texture(wash_tex, SCREEN_UV * texture_tiling * 0.23).r - MASK_MID) * 2.0;
	float wash_effect = mix(wash_noise(screenuv * 9.0), clamp(wash_dev, -1.0, 1.0), has_wash_tex);
	col *= 1.0 - blotch_strength * wash_effect;

	// Warm late-afternoon grade with a cool fill in the shadows.
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	col *= warm_tint;
	col = mix(col * cool_shadow, col, smoothstep(0.0, 0.45, lum));

	// Watercolour pooling: large surfaces darken slightly toward the ink lines.
	float halo = smoothstep(edge_threshold * 0.2, edge_threshold, depth_edge) * (1.0 - ink);
	col *= 1.0 - pool_darkening * halo;

	col = mix(col, ink_color.rgb, ink);
	ALBEDO = col;
	ALPHA = 1.0;
}
