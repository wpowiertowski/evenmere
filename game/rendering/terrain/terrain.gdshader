// The island surface: flat-shaded terrain from per-face normals and vertex colours, with an optional tile-grid
// overlay (view_grid_toggle, off by default). Culling is off because neighbouring chunk borders share edges but never
// vertices; the lighting comes from GEOMETRY normals, never from winding.
shader_type spatial;
render_mode cull_disabled;

uniform bool show_grid = false;
uniform vec4 grid_color : source_color = vec4(0.08, 0.1, 0.08, 1.0);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 p, float width_px) {
	vec2 distance_to_line = abs(fract(p - 0.5) - 0.5) / max(fwidth(p), vec2(0.0001));
	return 1.0 - min(min(distance_to_line.x, distance_to_line.y) / width_px, 1.0);
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;

	if (show_grid) {
		float pixels_per_tile = 1.0 / max(max(fwidth(world_pos.x), fwidth(world_pos.z)), 0.0001);
		float fade = clamp((pixels_per_tile - 3.0) / 6.0, 0.0, 1.0);
		float line = grid_line(world_pos.xz, 1.2);
		ALBEDO = mix(ALBEDO, grid_color.rgb, line * 0.6 * fade);
	}
}
