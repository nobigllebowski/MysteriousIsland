using System.Globalization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Player;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Diagnostics;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Session;
using ForgottenIsle.Game.World;
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

        /// <summary>Unity's built-in tag that <c>Camera.main</c> resolves against.</summary>
        private const string MainCameraTag = "MainCamera";

        /// <summary>Console prefix for the per-zone furnish diagnostic.</summary>
        private const string FurnishLogPrefix = "[Vardholm] furnish: ";

        private const float GroundSize = 60f;
        private const float AnchorHeight = 1f;
        private const float EyeHeight = 1.6f;
        private const float CapsuleHeight = 2f;

        private readonly SessionService _session;
        private readonly InputRouter _input;
        private readonly InteractionSystem _interactions;
        private readonly ICoreLog _log;

        /// <param name="session">Run state the rig reads and writes its pose through.</param>
        /// <param name="input">Input source handed to the rig.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        /// <param name="interactions">
        /// Interaction system the zone's markers, pickups and gates register with. Null tolerated,
        /// which yields a zone that can be walked but not acted on.
        /// </param>
        public ZoneFurnisher(
            SessionService session, InputRouter input, InteractionSystem interactions, ICoreLog log)
        {
            _session = session;
            _input = input;
            _interactions = interactions;
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

            // A zone rebuilt from scratch on every entry means the previous visit's interactables are
            // gone with it. Clearing first is what stops the registry accumulating destroyed
            // components across a round trip.
            if (_interactions != null)
            {
                _interactions.Clear();
            }

            var spawn = BuildZoneContent(scene);

            var anchor = ZoneEntryAnchor.FindInScene(scene);
            if (anchor == null)
            {
                anchor = CreateAnchor(scene, spawn);
            }

            // Ground and light are only furnished when the zone built nothing of its own -- an
            // authored scene, or an unrecognised zone key. ZoneBuilder supplies both for the two
            // real zones, and these would otherwise stack a second sun on top of its lighting.
            EnsureGround(scene, anchor);
            EnsureLight(scene);

            var rig = FindRigIn(scene);
            if (rig == null)
            {
                rig = CreateRig(scene, anchor);
            }

            var camera = EnsureCamera(scene, rig);

            // Exactly one camera may be enabled. During a zone transition two zones are briefly
            // resident (ADR-0004's cap), and the outgoing zone's camera would otherwise still be
            // rendering -- two overlapping views of two different islands.
            SuppressForeignCameras(camera);

            if (rig != null)
            {
                rig.Initialize(_session, _input, _interactions, _log);
            }

            // Reported AFTER Initialize, because Initialize is what places the rig: a diagnostic
            // taken before it would print the spawn capsule's construction position rather than
            // where the player actually is.
            var playable = VerifyPlayable(scene, rig, camera);

            // The full structural dump, unconditionally. VerifyPlayable answers "is the rig
            // complete", which was true the whole time the world was invisible; this answers "can
            // any of it reach the screen", which is the question that was actually open.
            ZoneDiagnostics.Dump(scene, camera, rig != null ? rig.transform : null);

            return playable ? rig : null;
        }

        /// <summary>
        /// Prints the state of everything a zone needs to be playable, and reports a gap as an error.
        /// </summary>
        /// <remarks>
        /// WHY THIS EXISTS: "Display 1 - No cameras rendering" is what the player sees when this goes
        /// wrong, and that message names no scene, no object and no cause. Every furnish now states
        /// what it actually produced, so the next failure of this kind is one line in the console
        /// rather than an afternoon of bisecting the bootstrap.
        /// <para>
        /// A missing piece is an <c>Error</c>, not a warning: the zone is loaded and the player is
        /// standing in it, so nothing downstream will fail loudly on its own -- the game just does
        /// not render or does not move, silently.
        /// </para>
        /// </remarks>
        /// <param name="scene">The zone just furnished.</param>
        /// <param name="rig">The rig serving it, if any.</param>
        /// <param name="camera">The camera chosen to render it, if any.</param>
        /// <returns>True when the player, the controller and an enabled camera all exist.</returns>
        private bool VerifyPlayable(Scene scene, PlayerRig rig, Camera camera)
        {
            var controller = rig != null ? rig.GetComponent<CharacterController>() : null;
            var cameraEnabled = camera != null && camera.enabled && camera.gameObject.activeInHierarchy;
            var enabledCameras = CountEnabledCameras();

            // The counts go on the FIRST line, because Unity's console list shows only the first
            // line and the previous version put them at the end where they were cut off -- which
            // cost a round trip for the one number that distinguishes an empty zone from an
            // invisible one.
            var report =
                "zone '" + scene.name + "' · renderers " +
                CountRenderers(scene).ToString(CultureInfo.InvariantCulture) +
                " · cameras " + enabledCameras.ToString(CultureInfo.InvariantCulture) +
                " · colour space " + QualitySettings.activeColorSpace +
                "\n  active scene '" + SceneManager.GetActiveScene().name + "'" +
                " · player " + Describe(rig != null) +
                " · controller " + Describe(controller != null) +
                " · camera " + Describe(camera != null) +
                " (enabled " + Describe(cameraEnabled) + ")" +
                " · tagged " + Describe(camera != null && camera.CompareTag(MainCameraTag)) +
                " · Camera.main " + Describe(Camera.main != null) +
                "\n  camera at " + (camera != null ? camera.transform.position.ToString("F1") : "-") +
                " looking " + (camera != null ? camera.transform.forward.ToString("F2") : "-") +
                " · clear " + (camera != null ? camera.clearFlags.ToString() : "-") +
                " · culling mask 0x" + (camera != null ? camera.cullingMask.ToString("X") : "-");

            // Camera.main is REPORTED but not required for the verdict: it is a cached tag lookup,
            // and whether that cache has refreshed in the same frame the tag was set is not
            // something to fail a zone over. The facts it depends on -- an enabled camera carrying
            // the MainCamera tag -- are checked directly instead, so a real failure is still caught.
            var tagged = camera != null && camera.CompareTag(MainCameraTag);
            var complete = rig != null && controller != null && cameraEnabled && tagged;
            if (complete && enabledCameras == 1)
            {
                Debug.Log(FurnishLogPrefix + report);
                return true;
            }

            if (_log != null)
            {
                _log.Warn(LogCode.FurnishIncomplete, report);
            }

            // Debug.LogError as well as the structured warning: an unplayable zone is not a
            // degradation to note in a counter, it is the run being over.
            Debug.LogError(FurnishLogPrefix + "ZONE IS NOT PLAYABLE — " + report);
            return false;
        }

        private static string Describe(bool present)
        {
            return present ? "yes" : "NO";
        }

        /// <summary>Renderers actually present in the zone. Zero means nothing was built to look at.</summary>
        /// <remarks>
        /// Reported next to the colour space on purpose: those two numbers separate "the zone is
        /// empty" from "the zone is there and too dark to see", and a black screen looks identical
        /// either way. In Gamma the shore renders at 6.6% grey, which is where an afternoon went.
        /// </remarks>
        private static int CountRenderers(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            var count = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                count += roots[i].GetComponentsInChildren<Renderer>(true).Length;
            }

            return count;
        }

        private static int CountEnabledCameras()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            var count = 0;
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].enabled && cameras[i].gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Disables every camera and audio listener except the ones this zone just produced.
        /// </summary>
        /// <remarks>
        /// Two things make duplicates possible: a zone transition leaves the outgoing zone resident
        /// for a moment, and a scene opened directly in the editor may carry a camera of its own.
        /// Disabling rather than destroying is deliberate -- a camera belonging to a scene that is
        /// about to unload will go with it, and a camera belonging to authored content is not this
        /// class's to delete.
        /// </remarks>
        /// <param name="keep">The camera that should remain enabled. Null disables nothing.</param>
        private void SuppressForeignCameras(Camera keep)
        {
            if (keep == null)
            {
                return;
            }

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != keep && cameras[i].enabled)
                {
                    cameras[i].enabled = false;
                }
            }

            var keepListener = keep.GetComponent<AudioListener>();
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
            for (var i = 0; i < listeners.Length; i++)
            {
                // Two enabled listeners make Unity warn and pick one arbitrarily, which is how a
                // zone ends up hearing itself from where the previous zone's camera stood.
                listeners[i].enabled = listeners[i] == keepListener;
            }
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

        private ZoneEntryAnchor CreateAnchor(Scene scene, Vector3 spawn)
        {
            var go = new GameObject("PlayerSpawn (furnished)");
            go.transform.SetParent(FurnishedRoot(scene).transform, false);
            go.transform.position = spawn;
            return go.AddComponent<ZoneEntryAnchor>();
        }

        /// <summary>
        /// Builds the zone's terrain, landmark, props and interactables.
        /// </summary>
        /// <remarks>
        /// Skipped entirely when the scene already has content, so hand-authored zone art displaces
        /// the procedural build with no code change -- the same "authored content wins" rule the
        /// rest of this class follows.
        /// </remarks>
        /// <param name="scene">The zone being furnished.</param>
        /// <returns>Where the player should stand.</returns>
        private Vector3 BuildZoneContent(Scene scene)
        {
            var fallback = new Vector3(0f, AnchorHeight, 0f);

            if (!SceneKeys.IsZone(scene.name))
            {
                return fallback;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != FurnishedRootName)
                {
                    // Authored content present. Leave it alone entirely.
                    return fallback;
                }
            }

            var root = FurnishedRoot(scene).transform;
            return ZoneBuilder.Build(scene.name, root, _interactions);
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
                // The primitive's capsule collider is replaced by the CharacterController's own,
                // which would otherwise fight it and trap the rig on its own geometry.
                Object.Destroy(collider);
            }

            // The camera pivot sits at eye height INSIDE this capsule, so its own mesh is the one
            // thing guaranteed to be in front of the camera at all times. A first-person rig shows
            // the world, not the inside of its own collision shape.
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            if (bodyRenderer != null)
            {
                bodyRenderer.enabled = false;
            }

            var controller = body.AddComponent<CharacterController>();
            controller.height = CapsuleHeight;
            controller.radius = 0.34f;
            controller.center = new Vector3(0f, 0f, 0f);

            // Generous step and slope limits: this is a rocky island built from noise, and a
            // controller that catches on every 20 cm lip reads as broken rather than as terrain.
            controller.stepOffset = 0.45f;
            controller.slopeLimit = 52f;
            controller.skinWidth = 0.04f;

            return body.AddComponent<PlayerRig>();
        }

        /// <summary>
        /// Guarantees the zone has exactly one enabled, correctly tagged camera on the player's pivot.
        /// </summary>
        /// <remarks>
        /// WHAT WENT WRONG BEFORE, and why this reads the way it does now: the previous version
        /// returned early on <c>rig.CameraPivot != null</c>. A pivot is not a camera. Any path that
        /// produced a pivot without one — an authored rig wired in the Inspector, a re-furnish of a
        /// zone whose camera had been disabled or destroyed — left the zone with no camera at all,
        /// and nothing anywhere said so. The symptom is Unity's "Display 1 — No cameras rendering",
        /// which names neither the scene nor the cause.
        /// <para>
        /// It also adopted any camera it found in the scene without checking whether that camera was
        /// enabled or its object active, so a disabled camera could silently become the zone's view.
        /// </para>
        /// <para>
        /// The tag matters independently: an untagged camera renders perfectly well but leaves
        /// <c>Camera.main</c> null, so anything that resolves the camera that way — Unity's own
        /// helpers included — finds nothing.
        /// </para>
        /// </remarks>
        /// <param name="scene">The zone being furnished.</param>
        /// <param name="rig">The rig the camera must follow.</param>
        /// <returns>The camera that will render this zone, or null when there is no rig to carry it.</returns>
        private Camera EnsureCamera(Scene scene, PlayerRig rig)
        {
            if (rig == null)
            {
                return null;
            }

            // A pivot that already carries a camera is finished; one that does not still needs it.
            var pivot = rig.CameraPivot;
            if (pivot != null)
            {
                var onPivot = pivot.GetComponentInChildren<Camera>(true);
                if (onPivot != null)
                {
                    return Commission(onPivot);
                }

                return Commission(BuildCamera(pivot));
            }

            var existing = FindCameraIn(scene);
            if (existing != null)
            {
                rig.AttachCameraPivot(existing.transform);
                return Commission(existing);
            }

            var created = new GameObject("Camera Pivot (furnished)");
            created.transform.SetParent(rig.transform, false);
            created.transform.localPosition = new Vector3(0f, EyeHeight - CapsuleHeight * 0.5f, 0f);

            var camera = BuildCamera(created.transform);
            rig.AttachCameraPivot(created.transform);
            return Commission(camera);
        }

        private static Camera BuildCamera(Transform pivot)
        {
            // Tagged BEFORE the component is added, so the camera is registered already tagged.
            // Camera.main is a cached tag lookup; tagging afterwards is correct but leaves the
            // result of Camera.main within the same frame dependent on when that cache refreshes.
            pivot.gameObject.tag = MainCameraTag;

            var camera = pivot.gameObject.AddComponent<Camera>();

            // 62° vertical, locked, per the Mobile UX Plan's comfort commitment. Never animated.
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.backgroundColor = new Color(0.04f, 0.06f, 0.05f);

            if (pivot.gameObject.GetComponent<AudioListener>() == null)
            {
                pivot.gameObject.AddComponent<AudioListener>();
            }

            return camera;
        }

        /// <summary>Puts a camera into service: active, enabled, and resolvable as Camera.main.</summary>
        /// <remarks>
        /// ⚠ VERIFY: under URP a runtime-added Camera also needs its
        /// <c>UniversalAdditionalCameraData</c>. URP is documented as adding that component on demand
        /// when it renders the camera, so this does not add it explicitly — doing so would make
        /// <c>ForgottenIsle.Game</c> reference the URP assembly for one component. If a furnished
        /// zone renders black in the editor, this is the first thing to check.
        /// </remarks>
        private static Camera Commission(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            if (!camera.gameObject.activeSelf)
            {
                camera.gameObject.SetActive(true);
            }

            camera.enabled = true;

            // "MainCamera" is one of Unity's built-in tags, so this cannot fail on a fresh project
            // the way a project-defined tag would.
            camera.gameObject.tag = MainCameraTag;
            return camera;
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
