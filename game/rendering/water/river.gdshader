// Flat river strips at each river tile's own elevation, just under the bank. Diagonal scrolling bands read as flow;
// the world-space position keeps the pattern continuous across tiles. One compact shader shared by the whole strip mesh.
shader_type spatial;
render_mode blend_mix;

uniform vec3 river_color : source_color = vec3(0.3, 0.52, 0.55);
uniform vec3 streak_color : source_color = vec3(0.75, 0.88, 0.88);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float along = (world_pos.x + world_pos.z) * 2.0 - TIME * 1.2;
	float band = sin(along) * 0.5 + 0.5;
	float glint = smoothstep(0.85, 1.0, sin(along * 2.7 + 1.3) * 0.5 + 0.5);
	vec3 col = mix(river_color, streak_color, band * 0.12 + glint * 0.12);
	ALBEDO = col;
	ROUGHNESS = 0.55;
	SPECULAR = 0.3;
	ALPHA = 0.92;
}
