// A flat checkerboard with a tile grid and a heavier line every 8 tiles, one cell per tile. It exists so the camera and
// tile picking can be judged by eye before terrain rendering (M1-19) lands. Fine detail fades out with distance so a
// whole-island view does not shimmer.
shader_type spatial;
render_mode unshaded;

varying vec2 ground_xz;

void vertex() {
	ground_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

float grid_line(vec2 p, float width_px) {
	vec2 distance_to_line = abs(fract(p - 0.5) - 0.5) / max(fwidth(p), vec2(0.0001));
	return 1.0 - min(min(distance_to_line.x, distance_to_line.y) / width_px, 1.0);
}

void fragment() {
	vec2 tile = floor(ground_xz);
	float pixels_per_tile = 1.0 / max(max(fwidth(ground_xz.x), fwidth(ground_xz.y)), 0.0001);
	float checker_fade = clamp((pixels_per_tile - 4.0) / 8.0, 0.0, 1.0);
	float grid_fade = clamp((pixels_per_tile - 3.0) / 6.0, 0.0, 1.0);

	float checker = mod(tile.x + tile.y, 2.0) * checker_fade;
	vec3 colour = mix(vec3(0.56, 0.71, 0.49), vec3(0.52, 0.67, 0.45), checker);
	colour = mix(colour, vec3(0.40, 0.53, 0.36), grid_line(ground_xz, 1.0) * 0.55 * grid_fade);
	colour = mix(colour, vec3(0.26, 0.37, 0.25), grid_line(ground_xz / 8.0, 1.5) * 0.85);
	ALBEDO = colour;
}
