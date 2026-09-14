using ForgottenIsle.Core.Logging;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Player;
using ForgottenIsle.Game.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// Fills in whatever a freshly loaded zone scene does not provide: ground, light, entry anchor,
    /// player rig and camera. Authored content always wins; this only closes gaps.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS: Phase 1 zone scenes are empty, because an empty scene has no serialized
    /// component references and therefore cannot drift from the code or be malformed. That is a good
    /// property to keep, but an empty scene is also unplayable — the previous behaviour was for
    /// <c>AppBootstrap</c> to look for a player rig, find none, and silently return, leaving the
    /// player in a loaded zone with no body and no camera. Furnishing at runtime is what makes
    /// "empty scene" and "playable" both true at once.
    /// <para>
    /// EVERY OBJECT IS OPTIONAL. Each check asks "does the scene already have one?" and skips if so.
    /// When real Ribcage art lands, its own ground, lighting and spawn point displace these
    /// placeholders with no code change — the furnisher simply finds nothing to do.
    /// </para>
    /// <para>
    /// Everything created here is parented under a single marked root so it is obvious in the
    /// hierarchy that these objects are not authored content.
    /// </para>
    /// </remarks>
    public sealed class ZoneFurnisher
    {
        /// <summary>Name of the root every furnished object is parented to.</summary>
        public const string FurnishedRootName = "[Furnished — runtime placeholder]";

        private const float GroundSize = 60f;
        private const float AnchorHeight = 1f;
        private const float EyeHeight = 1.6f;
        private const float CapsuleHeight = 2f;

        private readonly SessionService _session;
        private readonly InputRouter _input;
        private readonly ICoreLog _log;

        /// <param name="session">Run state the rig reads and writes its pose through.</param>
        /// <param name="input">Input source handed to the rig.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public ZoneFurnisher(SessionService session, InputRouter input, ICoreLog log)
        {
            _session = session;
            _input = input;
            _log = log;
        }

        /// <summary>
        /// Ensures <paramref name="scene"/> has everything needed to stand in and move around, then
        /// initializes the rig.
        /// </summary>
        /// <param name="scene">The newly loaded zone scene.</param>
        /// <returns>The rig now serving this zone, or null if the scene was not valid.</returns>
        public PlayerRig Furnish(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Warn("furnish called on an invalid or unloaded scene");
                return null;
            }

            var anchor = ZoneEntryAnchor.FindInScene(scene);
            if (anchor == null)
            {
                anchor = CreateAnchor(scene);
            }

            EnsureGround(scene, anchor);
            EnsureLight(scene);

            var rig = FindRigIn(scene);
            if (rig == null)
            {
                rig = CreateRig(scene, anchor);
            }

            EnsureCamera(scene, rig);

            rig.Initialize(_session, _input, _log);
            return rig;
        }

        private static PlayerRig FindRigIn(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var rig = roots[i].GetComponentInChildren<PlayerRig>(true);
                if (rig != null)
                {
                    return rig;
                }
            }

            return null;
        }

        private static GameObject FurnishedRoot(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == FurnishedRootName)
                {
                    return roots[i];
                }
            }

            var root = new GameObject(FurnishedRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private ZoneEntryAnchor CreateAnchor(Scene scene)
        {
            var go = new GameObject("PlayerSpawn (furnished)");
            go.transform.SetParent(FurnishedRoot(scene).transform, false);
            go.transform.position = new Vector3(0f, AnchorHeight, 0f);
            return go.AddComponent<ZoneEntryAnchor>();
        }

        private void EnsureGround(Scene scene, ZoneEntryAnchor anchor)
        {
            if (HasColliderBelow(scene, anchor))
            {
                return;
            }

            // A plain quad with a box collider rather than a Unity primitive plane: the primitive
            // carries a MeshCollider, and a 60 m MeshCollider is a heavier physics asset than a box
            // for something a greybox capsule only ever stands on.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground (furnished)";
            ground.transform.SetParent(FurnishedRoot(scene).transform, false);
            ground.transform.localScale = new Vector3(GroundSize, 1f, GroundSize);

            // Top face at y = 0, so the anchor's 1 m sits a metre above the floor.
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
        }

        private static bool HasColliderBelow(Scene scene, ZoneEntryAnchor anchor)
        {
            var roots = scene.GetRootGameObjects();
            var origin = anchor != null ? anchor.Position : Vector3.up;

            for (var i = 0; i < roots.Length; i++)
            {
                var colliders = roots[i].GetComponentsInChildren<Collider>(true);
                for (var c = 0; c < colliders.Length; c++)
                {
                    // Bounds rather than a raycast: a raycast needs physics to have ticked at least
                    // once, and this runs in the frame the scene finished loading.
                    var bounds = colliders[c].bounds;
                    if (bounds.max.y <= origin.y + 0.01f &&
                        bounds.min.x <= origin.x && bounds.max.x >= origin.x &&
                        bounds.min.z <= origin.z && bounds.max.z >= origin.z)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureLight(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].GetComponentInChildren<Light>(true) != null)
                {
                    return;
                }
            }

            var go = new GameObject("Directional Light (furnished)");
            go.transform.SetParent(FurnishedRoot(scene).transform, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.94f, 0.95f, 0.90f);
            light.intensity = 1.1f;
        }

        private PlayerRig CreateRig(Scene scene, ZoneEntryAnchor anchor)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Player (furnished)";
            body.transform.SetParent(FurnishedRoot(scene).transform, false);
            body.transform.position = anchor != null
                ? anchor.Position + new Vector3(0f, CapsuleHeight * 0.5f, 0f)
                : new Vector3(0f, CapsuleHeight * 0.5f, 0f);

            // The capsule's own collider would push the camera around and trap the rig on its own
            // ground. Phase 1 locomotion is transform-driven with no physics; Phase 2 replaces the
            // whole rig with a real character controller.
            var collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            return body.AddComponent<PlayerRig>();
        }

        private void EnsureCamera(Scene scene, PlayerRig rig)
        {
            if (rig == null)
            {
                return;
            }

            if (rig.CameraPivot != null)
            {
                return;
            }

            var existing = FindCameraIn(scene);
            if (existing != null)
            {
                rig.AttachCameraPivot(existing.transform);
                return;
            }

            var pivot = new GameObject("Camera Pivot (furnished)");
            pivot.transform.SetParent(rig.transform, false);
            pivot.transform.localPosition = new Vector3(0f, EyeHeight - CapsuleHeight * 0.5f, 0f);

            var camera = pivot.AddComponent<Camera>();

            // 62° vertical, locked, per the Mobile UX Plan's comfort commitment. Never animated.
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.backgroundColor = new Color(0.04f, 0.06f, 0.05f);

            pivot.AddComponent<AudioListener>();

            rig.AttachCameraPivot(pivot.transform);
        }

        private static Camera FindCameraIn(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var camera = roots[i].GetComponentInChildren<Camera>(true);
                if (camera != null)
                {
                    return camera;
                }
            }

            return null;
        }

        private void Warn(string detail)
        {
            if (_log != null)
            {
                _log.Warn(LogCode.CatalogMissing, "furnish: " + detail);
            }
        }
    }
}
