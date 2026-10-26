using Godot;

namespace Evenmere.Game.Rendering.Look;

/// <summary>
/// The hand-drawn material recipe for mesh builders (M2-29): one helper that turns a <see cref="StandardMaterial3D"/>
/// into the look's matte toon surface. The terrain, sea, river and road passes band their key light in their own
/// shaders; this helper is the same recipe for StandardMaterial builders. The factory adopts it for the hand-drawn
/// look; classic keeps the M1 materials.
/// </summary>
/// <remarks>
/// Banding needs two things from the caller: vertex colours as the albedo source
/// (<c>VertexColorUseAsAlbedo</c>, set by the mesh builder) authored in linear space (the kit linearises every part
/// colour at build, so <c>VertexColorIsSrgb</c> stays off), and matte parameters. The warm key comes from the scene
/// sun (<see cref="LookScene"/>), shared with every other material.
/// </remarks>
public static class LookMaterials
{
    /// <summary>
    /// Applies the hand-drawn matte toon surface to a StandardMaterial3D: banded toon diffuse with no specular,
    /// reading linear vertex colours.
    /// </summary>
    /// <param name="material">The material to configure, kept by the caller.</param>
    public static void ApplyHandDrawn(StandardMaterial3D material)
    {
        material.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon;
        material.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
        material.VertexColorIsSrgb = false;
    }
}
