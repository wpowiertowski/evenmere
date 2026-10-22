# 0013. Art direction: hand-drawn isometric

Status: Accepted · Date: 2026-10-21

## Context

M1 shipped with low-poly placeholder art: flat-shaded terrain facets, box-and-roof buildings and palette tints. The original plan deferred the art direction to M4-14. After the M1 playtest, the developer set the long-term target. It should look like a SimCity or Anno city builder, with a **hand-drawn look**. The reference is [Permit Pending](https://permit.petercai.com) (Peter Cai), whose marketing screenshots show:

- **Projection:** an orthographic isometric view. Lines stay parallel, with no perspective convergence. The camera is yawed about 45° and looks down at roughly 30°, so streets run diagonally across the screen.
- **Ink outlines:** dark, slightly irregular lines around buildings, roofs and props, as if inked over the models.
- **Painted surfaces:** flat colour with watercolour and paper grain, gentle colour variation, and banded rather than smooth shading. The light is warm late-afternoon light with long, soft cast shadows.
- **Painterly water:** hand-drawn ripple strokes, with the shadows lying across the surface.
- **Density and palette:** many small props (fences, vehicles, tanks, solar panels, trees), and a warm, slightly desaturated palette: saturated greens, cream pavements and ochre lots.

The scenes are 3D models drawn with a non-photorealistic pass, which suits Evenmere's low-poly 3D pipeline.

## Decision

- **The target look** is a hand-drawn, illustrated isometric city builder, following the reference above.
- **The geometry stays 3D and low-poly.** Placeholder meshes and later glTF models are drawn through a non-photorealistic (NPR) pass rather than pre-rendered sprites. The 90° rotation, smooth zoom and 3D picking therefore keep working.
- **Camera:**
  - The default is an **orthographic** camera with a fixed yaw offset and a moderate downward angle, about 30°.
  - Rotation keeps 90° steps from that offset.
  - The offset is a single setting. M1-32 sets it to 30° at the developer's request. The look-development task (M2-29) compares 30° with the reference's 45° and the developer picks.
  - Zoom maps to the orthographic size. Street-level close-ups may keep a gentle perspective blend if the prototype shows that orthographic feels flat up close.
- **Rendering pass:**
  - **Outlines:** ink outlines from a screen-space edge detection over depth and normals, with a little noise-driven wobble and per-object line weight.
  - **Lighting:** banded (toon) diffuse lighting, a warm key light with soft long shadows, and a cool fill.
  - **Surface texture:** paper and watercolour grain, through a screen-space or triplanar grain texture, with light edge darkening.
  - **Water:** painterly, from hand-drawn stroke textures over the existing distance-field foam.
- **Palette:** warm and slightly desaturated, defined once in `game/data/visuals` and shared by the terrain, building, road and UI accents.
- **Art pipeline:** Blender → glTF, unchanged. Models carry flat colours or a shared hand-painted palette atlas, and the NPR pass provides the drawn look, so no per-model line art is needed.

## Consequences

- **The camera must change:** an orthographic projection (or a tested perspective blend), a yaw offset, and picking that works with orthographic rays. TilePicker already casts rays from the camera, but it must be checked under an orthographic projection.
- **Placeholders stay valid.** The NPR pass applies to any mesh, so M1's placeholder buildings already approach the target once it lands. Real art can arrive gradually through `game/data/visuals`.
- **Performance** must be re-measured. Edge detection and grain are full-screen passes, and they must fit the Steam Deck frame budget at 1280 × 800.
- **Screenshot comparisons** against the reference become part of look-development reviews. Don't copy the reference's images into the repository.
- **The look-development prototype comes early**, in M2-29, so later rendering tasks (seasons, effects, LOD) are built for the target. M4-14 becomes the style guide and the art-source decision built on this direction, instead of choosing a direction from scratch.
