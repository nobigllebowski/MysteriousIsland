using ForgottenIsle.Core.Primitives;
using UnityEngine;

// NAMESPACE NOTE: the file sits under Interop/ because that folder is where this project keeps its one
// engine boundary, but the namespace is ForgottenIsle.Game.Player — there is no Interop namespace in the
// project's sanctioned list, and Unity does not tie namespaces to folders. Player is the honest home: the
// only Core type in Phase 1 that has an engine counterpart is Vec3, and the only Core field of that type is
// PlayerState.Position. If a second subsystem ever needs the bridge, it moves; it does not get copied.

namespace ForgottenIsle.Game.Player
{
    /// <summary>
    /// THE ONLY BRIDGE between UnityEngine types and Core types. Nothing else in the codebase is permitted
    /// to convert between the two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE BRIDGE IS A SINGLE FILE. Core may not reference UnityEngine in any form — that is what makes
    /// it testable without a player loop, portable to a headless server, and immune to a Unity upgrade
    /// changing the meaning of its data. The rule survives exactly as long as the conversions stay in one
    /// place. The moment a <c>new Vector3(state.Position.X, ...)</c> appears inline in a controller, the
    /// boundary stops being a boundary and becomes a convention, and conventions do not fail the build.
    /// One file means an audit of engine leakage is a single grep, and it means a future change to Core's
    /// coordinate convention has exactly one call site to fix.
    /// </para>
    /// <para>
    /// Extension methods rather than a converter class because the call sites read as the thing they are —
    /// <c>transform.position = state.Position.ToVector3()</c> — which is short enough that nobody is
    /// tempted to hand-roll it, and that is the entire enforcement mechanism.
    /// </para>
    /// <para>
    /// The conversions are lossless in both directions: <see cref="Vec3"/> and <see cref="Vector3"/> both
    /// hold three <see cref="float"/>s with the same handedness and the same axis meanings. This is
    /// deliberately a rename, not a transform. If Core ever adopts a different convention, the conversion
    /// belongs here and the fact that it is no longer free belongs in this comment.
    /// </para>
    /// </remarks>
    public static class CoreInterop
    {
        /// <summary>Converts a Core position or direction into the engine's vector type.</summary>
        public static Vector3 ToVector3(this Vec3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        /// <summary>Converts an engine vector into Core's, for storing in state that will be serialized.</summary>
        public static Vec3 ToVec3(this Vector3 value)
        {
            return new Vec3(value.x, value.y, value.z);
        }

        /// <summary>
        /// Flattens an engine vector onto the ground plane as a Core vector, discarding height.
        /// </summary>
        /// <remarks>
        /// Movement intent arrives from <c>InputRouter</c> as a 2D vector on the stick's plane, and the
        /// island's traversal is horizontal. Spelling the mapping out here — stick Y becomes world Z —
        /// keeps the one place that decides which way "forward" is from being a literal buried in a
        /// character controller.
        /// </remarks>
        public static Vec3 ToGroundVec3(this Vector2 value)
        {
            return new Vec3(value.x, 0f, value.y);
        }

        /// <summary>
        /// Converts Core's stored facing into an engine rotation about the world up axis.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Core stores facing as a single <c>YawDegrees</c> float rather than a quaternion, and this is the
        /// seam where that pays off. A quaternion is four floats with an invariant (unit length) that a
        /// save file cannot enforce: a truncated or hand-edited save can contain a non-normalised
        /// quaternion, and the result is a player who loads in scaled, sheared, or facing nowhere. A single
        /// angle has no invariant to violate — every float is a valid heading — and the character cannot
        /// pitch or roll anyway.
        /// </para>
        /// <para>
        /// The angle is not normalised on the way out. <see cref="Quaternion.Euler(float,float,float)"/>
        /// handles any magnitude, and normalising here would mean a value saved as 370 comes back as 10,
        /// making a save round trip visibly lossy for no benefit.
        /// </para>
        /// <para>
        /// NOT an extension method, unlike the vector conversions above. An extension on <c>float</c> would
        /// attach itself to every number in the project and turn a focused bridge into ambient API noise.
        /// </para>
        /// </remarks>
        public static Quaternion YawToRotation(float yawDegrees)
        {
            return Quaternion.Euler(0f, yawDegrees, 0f);
        }

        /// <summary>
        /// Extracts the yaw Core stores from an engine rotation, discarding pitch and roll.
        /// </summary>
        /// <remarks>
        /// Returns the angle in the 0..360 range Unity's <see cref="Quaternion.eulerAngles"/> produces.
        /// Callers must not assume -180..180; the value exists to be written to <c>PlayerState</c> and read
        /// back through <see cref="YawToRotation"/>, which is insensitive to the range.
        /// <para>Static rather than an extension, for the reason given on <see cref="YawToRotation"/>.</para>
        /// </remarks>
        public static float RotationToYaw(Quaternion rotation)
        {
            return rotation.eulerAngles.y;
        }
    }
}
