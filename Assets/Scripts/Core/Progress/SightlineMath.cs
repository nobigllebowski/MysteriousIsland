using System;

namespace ForgottenIsle.Core.Progress
{
    /// <summary>
    /// The arithmetic of looking down a line: whether a heading is within tolerance of an axis.
    /// </summary>
    /// <remarks>
    /// Engine-free on purpose so the rule can be pinned by a test: the game's foundational
    /// observation verb should not depend on a quaternion to be checked. Headings are compass
    /// yaws in degrees, 0 along +Z and 90 along +X, the convention <c>PlayerRig</c> keeps.
    /// </remarks>
    public static class SightlineMath
    {
        /// <summary>Smallest signed difference between two headings, in -180..180.</summary>
        public static float Difference(float headingDegrees, float axisDegrees)
        {
            var d = (headingDegrees - axisDegrees) % 360f;
            if (d > 180f)
            {
                d -= 360f;
            }
            else if (d < -180f)
            {
                d += 360f;
            }

            return d;
        }

        /// <summary>True when the heading is within <paramref name="toleranceDegrees"/> of the axis.</summary>
        /// <remarks>Looking straight back down the line does not count: a sightline has a direction.</remarks>
        public static bool IsAligned(float headingDegrees, float axisDegrees, float toleranceDegrees)
        {
            return Math.Abs(Difference(headingDegrees, axisDegrees)) <= toleranceDegrees;
        }

        /// <summary>Compass yaw of a direction given as its X and Z components.</summary>
        public static float YawOf(float x, float z)
        {
            var degrees = (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
