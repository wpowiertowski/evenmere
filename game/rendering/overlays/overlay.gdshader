// The overlay sheet (M2-20): per-tile fills from the vertex colours the renderer writes, one flat
// colour per tile so layer edges stay crisp. Real alpha blending, unshaded: the C# side composites the
// sheet after the look post pass (see OverlayRenderer.AfterLookPriority), so it tints the finished,
// inked frame instead of being replaced by it.
//
// Unlit tiles are pure black and discarded by luminance: per-vertex alpha does not reach the blend
// factors on this renderer, so translucency comes from the per-layer <c>overlay_alpha</c> uniform and
// the on/off mask lives in the colours. The luminance floor keeps the darkest deposit (coal) far above
// the discard line.
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

uniform float overlay_alpha = 0.4;

void fragment()
{
	if (dot(COLOR.rgb, vec3(1.0)) < 0.01)
	{
		discard;
	}

	ALBEDO = COLOR.rgb;
	ALPHA = overlay_alpha;
}
