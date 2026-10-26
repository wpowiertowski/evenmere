// Shared river shader chunks (M2-32): the uniform declarations used identically by the classic and
// hand-drawn river shaders. Look-specific values (warm tint) stay in each file, so classic keeps its
// M1 look pixel-exact.
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

// The scene ambient (Main.tscn WorldEnvironment colour x energy) lifts every band; the material setup reads
// the live environment so the single source stays in the scene.
uniform vec3 ambient_color : source_color = vec3(0.51, 0.53, 0.55);

// Band boundaries in NdotL, matching the terrain pass.
uniform float band_high = 0.5;
uniform float band_low = 0.0;
uniform float band_softness = 0.02;
uniform float band_mid = 0.75;
uniform float band_shade = 0.5;

uniform sampler2D ripple_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float has_ripple_tex = 0.0;
const float RIPPLE_MID = 0.2159;
const float RIPPLE_WORLD_TILES = 12.0;
