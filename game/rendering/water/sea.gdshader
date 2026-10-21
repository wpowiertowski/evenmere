// One animated sea plane for the whole island. Gentle vertex waves move the surface; the baked shoreline texture
// (R = distance to land in tiles, G = water type, B = land mask) paints foam near the shore and tints reef and deep
// water. The plane is translucent so the sea floor and reef shelf meshes faintly show through in the shallows.
shader_type spatial;
render_mode blend_mix;

uniform sampler2D shore_tex : filter_linear_mipmap, repeat_disable;
uniform vec2 map_size = vec2(128.0, 128.0);
uniform vec3 shallow_color : source_color = vec3(0.23, 0.52, 0.58);
uniform vec3 reef_color : source_color = vec3(0.2, 0.6, 0.55);
uniform vec3 deep_color : source_color = vec3(0.04, 0.2, 0.36);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.95);
uniform float foam_width_tiles = 2.5;
uniform float max_distance_tiles = 12.0;

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

	vec3 col = mix(shallow_color, reef_color, smoothstep(0.25, 0.6, water_type));
	col = mix(col, deep_color, smoothstep(0.6, 1.0, water_type));

	float foam_band = 1.0 - smoothstep(0.0, foam_width_tiles, dist_tiles);
	float foam_edge = sin(dist_tiles * 4.0 - TIME * 1.6) * 0.5 + 0.5;
	float foam = foam_band * (0.55 + 0.45 * foam_edge) * (1.0 - is_land);
	col = mix(col, foam_color, clamp(foam, 0.0, 1.0) * 0.8);

	ALBEDO = col;
	ROUGHNESS = 0.35;
	SPECULAR = 0.6;
	ALPHA = 0.88;
}
