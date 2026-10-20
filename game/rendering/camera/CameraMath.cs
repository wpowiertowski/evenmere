using System.Numerics;

namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// The camera's curves and integrators as plain functions of numbers: no Godot types, no state, so they can be unit
/// tested without the engine. Every integrator is exact for its model, which makes camera motion independent of the
/// frame rate: stepping 1 s as one 1 s frame or sixty 1/60 s frames lands on the same value.
/// </summary>
public static class CameraMath
{
    private const float HalfPi = MathF.PI / 2f;

    /// <summary>Gets a quarter turn in radians, the size of one camera rotation step.</summary>
    public static float QuarterTurn => HalfPi;

    /// <summary>
    /// Gets how far a zoom distance sits between street level and island view, measured in log space so that each
    /// mouse-wheel notch (a constant ratio) moves the same distance along the curve.
    /// </summary>
    /// <param name="zoom">The camera distance from the focus point, in world units.</param>
    /// <param name="minZoom">The street-level distance; must be above zero.</param>
    /// <param name="maxZoom">The island-view distance; must be above <paramref name="minZoom"/>.</param>
    /// <returns>0 at <paramref name="minZoom"/> and 1 at <paramref name="maxZoom"/>, clamped.</returns>
    public static float ZoomFraction(float zoom, float minZoom, float maxZoom)
    {
        float span = MathF.Log(maxZoom / minZoom);
        if (!(span > 0f))
        {
            return 0f;
        }

        return Math.Clamp(MathF.Log(zoom / minZoom) / span, 0f, 1f);
    }

    /// <summary>
    /// Gets the camera pitch for a zoom distance. It blends from the street-level pitch to the island-view pitch along a
    /// smooth step of <see cref="ZoomFraction"/>, so the tilt eases in and out at both ends instead of changing at a
    /// constant rate.
    /// </summary>
    /// <param name="zoom">The camera distance from the focus point, in world units.</param>
    /// <param name="minZoom">The street-level distance.</param>
    /// <param name="maxZoom">The island-view distance.</param>
    /// <param name="streetDegrees">The pitch at street level, in degrees below the horizon.</param>
    /// <param name="islandDegrees">The pitch at island view, in degrees below the horizon.</param>
    /// <returns>The pitch in radians below the horizon.</returns>
    public static float PitchRadians(float zoom, float minZoom, float maxZoom, float streetDegrees, float islandDegrees)
    {
        float t = SmoothStep(ZoomFraction(zoom, minZoom, maxZoom));
        float degrees = streetDegrees + ((islandDegrees - streetDegrees) * t);
        return degrees * (MathF.PI / 180f);
    }

    /// <summary>Gets the classic 3t² − 2t³ ease, which is 0 at 0, 1 at 1 and flat at both ends.</summary>
    /// <param name="t">A value from 0 to 1; it is clamped.</param>
    /// <returns>The eased value.</returns>
    public static float SmoothStep(float t)
    {
        float c = Math.Clamp(t, 0f, 1f);
        return c * c * (3f - (2f * c));
    }

    /// <summary>
    /// Gets where the camera sits relative to the focus point: <paramref name="zoom"/> units away, looking down at
    /// <paramref name="pitchRadians"/>, on the side the rig's <paramref name="yawRadians"/> turns it to.
    /// </summary>
    /// <param name="zoom">The distance from the focus point, in world units.</param>
    /// <param name="pitchRadians">The pitch below the horizon, in radians.</param>
    /// <param name="yawRadians">The rotation about the vertical axis, in radians. Zero puts the camera on the +Z side, looking toward -Z.</param>
    /// <returns>The offset from the focus point to the camera, in world units.</returns>
    public static Vector3 CameraOffset(float zoom, float pitchRadians, float yawRadians)
    {
        float horizontal = MathF.Cos(pitchRadians) * zoom;
        float height = MathF.Sin(pitchRadians) * zoom;
        return new Vector3(MathF.Sin(yawRadians) * horizontal, height, MathF.Cos(yawRadians) * horizontal);
    }

    /// <summary>
    /// Turns a camera-relative input vector into a world-space one: input X is the camera's right, input Y is toward
    /// the viewer (so "up" on a stick is negative Y and moves the focus away from the camera).
    /// </summary>
    /// <param name="inputX">The sideways input, positive to the camera's right.</param>
    /// <param name="inputY">The forward/back input, negative moving away from the camera.</param>
    /// <param name="yawRadians">The camera's rotation about the vertical axis.</param>
    /// <returns>The world X and Z components of the same vector.</returns>
    public static (float X, float Z) RotateByYaw(float inputX, float inputY, float yawRadians)
    {
        float sin = MathF.Sin(yawRadians);
        float cos = MathF.Cos(yawRadians);
        return ((inputX * cos) + (inputY * sin), (-inputX * sin) + (inputY * cos));
    }

    /// <summary>
    /// Moves a value toward a target the way a cooling cup of tea does: a fixed fraction of the remaining gap per unit
    /// of time. Exact for any step size, so splitting a step in two gives the same answer.
    /// </summary>
    /// <param name="current">The current value.</param>
    /// <param name="target">The value being approached.</param>
    /// <param name="rate">The decay rate per second; the gap shrinks by a factor of e every 1/rate seconds. Zero or less jumps straight to the target.</param>
    /// <param name="deltaSeconds">The step, in seconds.</param>
    /// <returns>The new value.</returns>
    public static float Approach(float current, float target, float rate, float deltaSeconds)
    {
        if (rate <= 0f)
        {
            return target;
        }

        return target + ((current - target) * MathF.Exp(-rate * deltaSeconds));
    }

    /// <summary>
    /// Advances a critically damped spring (the fastest approach with no overshoot, starting and ending gently). It uses
    /// the closed-form solution, so it is exact for any step size.
    /// </summary>
    /// <param name="position">The current position, updated in place.</param>
    /// <param name="velocity">The current velocity, updated in place.</param>
    /// <param name="target">The rest position.</param>
    /// <param name="omega">The natural frequency per second; about 4.6 / omega seconds to settle within 1%.</param>
    /// <param name="deltaSeconds">The step, in seconds.</param>
    public static void SpringStep(ref float position, ref float velocity, float target, float omega, float deltaSeconds)
    {
        if (omega <= 0f)
        {
            position = target;
            velocity = 0f;
            return;
        }

        float offset = position - target;
        float decay = MathF.Exp(-omega * deltaSeconds);
        float temp = (velocity + (omega * offset)) * deltaSeconds;
        position = target + ((offset + temp) * decay);
        velocity = (velocity - (omega * temp)) * decay;
    }

    /// <summary>
    /// Advances a pan velocity that eases toward the velocity the input asks for, and returns how far the focus moved
    /// during the step. The distance is the exact integral of the easing, so a pan covers the same ground at any frame rate.
    /// </summary>
    /// <param name="velocity">The current velocity along one axis, updated in place.</param>
    /// <param name="desired">The velocity the input is asking for.</param>
    /// <param name="response">The easing rate per second; the velocity closes the gap by a factor of e every 1/response seconds. Zero or less snaps to the desired velocity.</param>
    /// <param name="deltaSeconds">The step, in seconds.</param>
    /// <returns>The distance moved along this axis, in world units.</returns>
    public static float PanStep(ref float velocity, float desired, float response, float deltaSeconds)
    {
        if (response <= 0f)
        {
            velocity = desired;
            return desired * deltaSeconds;
        }

        float gap = velocity - desired;
        float decay = MathF.Exp(-response * deltaSeconds);
        float distance = (desired * deltaSeconds) + (gap * (1f - decay) / response);
        velocity = desired + (gap * decay);
        return distance;
    }

    /// <summary>Finds where a ray meets the horizontal plane at a given height.</summary>
    /// <param name="origin">The ray origin.</param>
    /// <param name="direction">The ray direction; it need not be normalised.</param>
    /// <param name="planeY">The height of the plane.</param>
    /// <param name="point">The intersection point when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the ray is parallel to the plane or points away from it.</returns>
    public static bool TryIntersectPlane(Vector3 origin, Vector3 direction, float planeY, out Vector3 point)
    {
        point = default;
        if (MathF.Abs(direction.Y) < 1e-9f)
        {
            return false;
        }

        float t = (planeY - origin.Y) / direction.Y;
        if (t < 0f)
        {
            return false;
        }

        point = origin + (direction * t);
        return true;
    }
}
