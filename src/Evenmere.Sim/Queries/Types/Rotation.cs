using Evenmere.Content.Definitions;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// A building's orientation in quarter turns clockwise (0 to 3). A quarter or three-quarter turn swaps the width and
/// height of the footprint. The numeric values are part of the save format and of scenario files (<c>"rotation": 1</c>).
/// </summary>
public enum Rotation : byte
{
    /// <summary>No turn: the footprint keeps the width and height its definition gives.</summary>
    Deg0 = 0,

    /// <summary>One quarter turn clockwise: width and height swap.</summary>
    Deg90 = 1,

    /// <summary>A half turn: width and height are as defined.</summary>
    Deg180 = 2,

    /// <summary>Three quarter turns clockwise: width and height swap.</summary>
    Deg270 = 3,
}

/// <summary>Helpers over <see cref="Rotation"/>.</summary>
public static class RotationExtensions
{
    /// <summary>Tests whether a value is one of the four defined rotations.</summary>
    /// <param name="rotation">The rotation; an enum can hold any byte, so check values that come from outside the simulation.</param>
    /// <returns><see langword="true"/> for 0 to 3.</returns>
    public static bool IsValid(this Rotation rotation) => (byte)rotation <= (byte)Rotation.Deg270;

    /// <summary>Tests whether the rotation swaps the width and height of a footprint.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns><see langword="true"/> for a quarter or three-quarter turn.</returns>
    public static bool SwapsAxes(this Rotation rotation) => ((byte)rotation & 1) == 1;

    /// <summary>Gets a footprint as it stands after the rotation.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <param name="footprint">The size in tiles before rotation.</param>
    /// <returns>The size in tiles after rotation.</returns>
    public static Footprint Rotate(this Rotation rotation, Footprint footprint) =>
        rotation.SwapsAxes() ? new Footprint(footprint.Height, footprint.Width) : footprint;

    /// <summary>Gets the rotation one quarter turn further clockwise, wrapping from 270 back to 0.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The next rotation.</returns>
    public static Rotation Next(this Rotation rotation) => (Rotation)(((byte)rotation + 1) & 3);

    /// <summary>Gets the rotation one quarter turn back, wrapping from 0 to 270.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The previous rotation.</returns>
    public static Rotation Previous(this Rotation rotation) => (Rotation)(((byte)rotation + 3) & 3);
}
