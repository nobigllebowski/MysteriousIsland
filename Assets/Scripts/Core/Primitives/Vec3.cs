using System;
using System.Globalization;

namespace ForgottenIsle.Core.Primitives
{
    /// <summary>
    /// An engine-free three-component position or direction in world space, in metres.
    /// </summary>
    /// <remarks>
    /// WHY this type exists at all when Unity already has <c>Vector3</c>: ForgottenIsle.Core is compiled
    /// with <c>noEngineReferences</c>, so nothing under Scripts/Core may name a UnityEngine type. Player
    /// position lives in <see cref="ForgottenIsle.Core.State.PlayerState"/>, which is captured into saves
    /// and replayed deterministically, so it must be expressible without the engine. The Game layer
    /// converts at the boundary. Fields are public and readonly rather than properties because this
    /// struct is copied per frame and the JIT elides the field reads more reliably than property calls.
    /// </remarks>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        /// <summary>East/west axis, matching Unity's X.</summary>
        public readonly float X;

        /// <summary>Up axis, matching Unity's Y.</summary>
        public readonly float Y;

        /// <summary>North/south axis, matching Unity's Z.</summary>
        public readonly float Z;

        /// <summary>The origin. Also the value of <c>default(Vec3)</c>.</summary>
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(Vec3 other)
        {
            // Exact comparison on purpose: this is used for save round-trip verification and for
            // "did the authored spawn point change" checks, where a tolerance would hide real drift.
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Vec3 left, Vec3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Vec3 left, Vec3 right)
        {
            return !left.Equals(right);
        }

        /// <summary>Invariant-culture round-trip form, for logs and save diffs. Never player-facing.</summary>
        public override string ToString()
        {
            return "(" + X.ToString("R", CultureInfo.InvariantCulture)
                   + ", " + Y.ToString("R", CultureInfo.InvariantCulture)
                   + ", " + Z.ToString("R", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// Arithmetic over <see cref="Vec3"/>, kept out of the struct so the struct stays a pure data carrier.
    /// </summary>
    /// <remarks>
    /// WHY a separate static class: <see cref="Vec3"/> is serialized field-for-field by the save codec and
    /// is compared for exact equality. Keeping every operation that can introduce floating point drift in
    /// one place makes it obvious which code paths can perturb a saved position.
    /// </remarks>
    public static class Vec3Math
    {
        /// <summary>Below this length a vector is treated as having no direction, so normalising it is undefined.</summary>
        public const float NormalizeEpsilon = 1e-12f;

        public static Vec3 Add(in Vec3 a, in Vec3 b)
        {
            return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vec3 Subtract(in Vec3 a, in Vec3 b)
        {
            return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vec3 Scale(in Vec3 v, float factor)
        {
            return new Vec3(v.X * factor, v.Y * factor, v.Z * factor);
        }

        /// <summary>
        /// Squared distance between two points. Prefer this to <see cref="Distance"/> for range tests:
        /// comparing against a squared radius avoids a square root per candidate.
        /// </summary>
        public static float DistanceSquared(in Vec3 a, in Vec3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        public static float Distance(in Vec3 a, in Vec3 b)
        {
            return (float)Math.Sqrt(DistanceSquared(in a, in b));
        }

        public static float Length(in Vec3 v)
        {
            return (float)Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        }

        public static float LengthSquared(in Vec3 v)
        {
            return (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);
        }

        /// <summary>
        /// Linear interpolation from <paramref name="a"/> to <paramref name="b"/>.
        /// </summary>
        /// <param name="t">
        /// Blend factor, clamped to [0, 1]. WHY clamp: callers feed this from elapsed/duration ratios that
        /// overshoot on a long frame, and an unclamped result would fling the player past the target.
        /// </param>
        public static Vec3 Lerp(in Vec3 a, in Vec3 b, float t)
        {
            if (t <= 0f)
            {
                return a;
            }

            if (t >= 1f)
            {
                return b;
            }

            return new Vec3(
                a.X + ((b.X - a.X) * t),
                a.Y + ((b.Y - a.Y) * t),
                a.Z + ((b.Z - a.Z) * t));
        }

        /// <summary>
        /// Returns <paramref name="v"/> scaled to unit length, or <see cref="Vec3.Zero"/> when it is too
        /// short to have a meaningful direction. Never divides by zero and never returns NaN.
        /// </summary>
        public static Vec3 Normalize(in Vec3 v)
        {
            var lengthSquared = LengthSquared(in v);
            if (lengthSquared < NormalizeEpsilon)
            {
                return Vec3.Zero;
            }

            var inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
            return new Vec3(v.X * inverseLength, v.Y * inverseLength, v.Z * inverseLength);
        }
    }
}
