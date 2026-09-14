using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Player
{
    /// <summary>
    /// Marks the spot a zone scene puts the player on when they arrive in it. One per zone scene,
    /// authored on the scene's <c>Spawns/PlayerSpawn</c> object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A COMPONENT AND NOT A COORDINATE IN DATA. The spawn point is a fact about the geometry — it
    /// has to be on the floor, clear of the rocks, and facing into the zone rather than out of it — and
    /// the only person who can tell whether it is, is the person dragging the rocks around. Authoring it
    /// as a transform in the scene means the answer is validated by looking at it, and it moves when the
    /// floor moves. A number in a config file would be correct on the day it was written and silently
    /// wrong the first time the shelf was re-blocked.
    /// </para>
    /// <para>
    /// WHY THE POSE IS THE TRANSFORM'S OWN, with no offset field: an offset is a second opinion about
    /// where the player stands, and two opinions drift. The gizmo below draws exactly what the rig will
    /// use, so what the author sees in the Scene view is where the capsule ends up.
    /// </para>
    /// <para>
    /// A zone with no anchor is not an error — Phase 1 zones are allowed to be empty geometry — so
    /// <see cref="FindInScene"/> answers null rather than throwing and <see cref="PlayerRig"/> falls back
    /// to leaving the capsule where the scene author parked the prefab.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Vardholm/Zone Entry Anchor")]
    public sealed class ZoneEntryAnchor : MonoBehaviour
    {
        /// <summary>Radius of the authoring gizmo, in metres. Roughly a standing figure's footprint.</summary>
        private const float GizmoRadius = 0.4f;

        /// <summary>Length of the facing needle the gizmo draws, in metres.</summary>
        private const float GizmoFacingLength = 1.5f;

        /// <summary>World position the arriving player is placed at.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// Facing the arriving player is given, in degrees about the world up axis.
        /// </summary>
        /// <remarks>
        /// Yaw only, because that is all <c>PlayerState</c> stores and all the capsule can express. Pitch
        /// and roll authored on this object are ignored rather than silently applied, so an anchor that
        /// was nudged in the Scene view cannot tip the player over.
        /// </remarks>
        public float YawDegrees => CoreInterop.RotationToYaw(transform.rotation);

        /// <summary>
        /// Returns the entry anchor authored in <paramref name="scene"/>, or null when it has none.
        /// </summary>
        /// <remarks>
        /// Scoped to one scene's roots rather than searched globally: with zones loaded additively there
        /// can be more than one scene holding an anchor during a transition, and a global search would be
        /// free to return the wrong one. Inactive objects are included so a spawn marker can be authored
        /// switched off — it exists to be measured, not to be rendered.
        /// <para>
        /// Allocating (<c>GetRootGameObjects</c> returns a fresh array) and therefore called once, on
        /// zone entry, behind the loading curtain — never from a per-frame path.
        /// </para>
        /// </remarks>
        /// <param name="scene">The loaded zone scene to search.</param>
        public static ZoneEntryAnchor FindInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var anchor = roots[i].GetComponentInChildren<ZoneEntryAnchor>(true);
                if (anchor != null)
                {
                    return anchor;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draws the footprint and the facing so the spawn point is visible without selecting it.
        /// </summary>
        /// <remarks>
        /// Editor-only. The spawn marker has no renderer of its own, and an invisible spawn point is one
        /// nobody checks against the geometry until a tester lands inside a cliff.
        /// </remarks>
        private void OnDrawGizmos()
        {
            var origin = transform.position;
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(origin, GizmoRadius);
            Gizmos.DrawLine(origin, origin + (transform.forward * GizmoFacingLength));
        }
#endif
    }
}
