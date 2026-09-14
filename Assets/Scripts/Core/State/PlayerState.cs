using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.State
{
    /// <summary>
    /// The player's physical presence in the world: where they stand, which way they face, and
    /// what is in their hands.
    /// </summary>
    /// <remarks>
    /// Position is a Core <see cref="Vec3"/>, not a Unity vector, so this type — and therefore the
    /// whole save envelope — stays testable outside the editor. The Unity layer converts at the
    /// boundary, in exactly one place.
    /// </remarks>
    public sealed class PlayerState
    {
        /// <summary>World-space position, in metres, in the zone named by the session.</summary>
        public Vec3 Position;

        /// <summary>
        /// Facing as a yaw angle in degrees. Only yaw is persisted: pitch is camera state that the
        /// player re-establishes in the first second after a load, and roll is never authored.
        /// </summary>
        public float YawDegrees;

        /// <summary>
        /// Instance id of the equipped tool, or empty for empty-handed. An instance id rather than
        /// a definition id, because two copies of the same tool have different wear.
        /// </summary>
        public string EquippedToolInstanceId = string.Empty;

        /// <summary>
        /// Returns an independent copy. <see cref="Vec3"/> is a readonly struct and is copied by
        /// value; the id is an immutable string. Nothing remains shared with the original.
        /// </summary>
        public PlayerState Clone()
        {
            var copy = new PlayerState();
            copy.Position = Position;
            copy.YawDegrees = YawDegrees;
            copy.EquippedToolInstanceId = EquippedToolInstanceId;
            return copy;
        }
    }
}
