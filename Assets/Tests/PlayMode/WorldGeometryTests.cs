using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Diagnostics;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Player;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ForgottenIsle.Tests.PlayMode
{
    /// <summary>
    /// UNITY PLAYMODE TIER.
    /// The zone is not merely furnished — it contains geometry that can actually reach the screen.
    /// </summary>
    /// <remarks>
    /// WHY THIS SUITE EXISTS, stated plainly: every existing test passed while the world was
    /// invisible. They asserted that a player, a controller and a camera existed, and all three did.
    /// "The interaction prompt says Standing Stone" is evidence that a component registered itself,
    /// not that anything was drawn — a point this suite takes as its whole premise.
    /// <para>
    /// So nothing here asks whether an object exists. Every assertion is about the chain that ends
    /// in a pixel: a mesh with vertices, a renderer that is enabled, a layer the camera can see, a
    /// material with a real shader, and bounds inside the frustum.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WorldGeometryTests
    {
        private const float BootTimeoutSeconds = 10f;
        private const float LoadTimeoutSeconds = 25f;
        private const int TestSlot = SaveSlotService.SlotCount - 1;

        /// <summary>The Ribcage's recipe is 1 ground + 26 rocks + 10 flora + a 6-rib arch + props.</summary>
        private const int MinimumExpectedRenderers = 30;

        /// <summary>Screen luminance below which a surface reads as black rather than as dark.</summary>
        private const float MinimumReadableLuminance = 0.18f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            RequireScenesInBuild();
            yield return WaitForBoot();
            yield return ReturnToMenu();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return ReturnToMenu();
        }

        /// <summary>The zone contains the geometry its recipe describes.</summary>
        [UnityTest]
        public IEnumerator Ribcage_ContainsTheGeometryItsRecipeDescribes()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var zone = SceneManager.GetSceneByName(ContentIds.ZoneRibcage);
            Assert.That(zone.IsValid() && zone.isLoaded, Is.True, "ZoneRibcage is not loaded.");

            var filters = FindAll<MeshFilter>(zone);
            var renderers = FindAll<MeshRenderer>(zone);

            Assert.That(filters.Count, Is.GreaterThan(0), "The zone has no MeshFilter at all.");
            Assert.That(renderers.Count, Is.GreaterThanOrEqualTo(MinimumExpectedRenderers),
                "The zone has " + renderers.Count + " renderers. The Ribcage recipe builds ground, "
                + "26 rocks, 10 flora and a six-rib arch, so a number far below "
                + MinimumExpectedRenderers + " means ZoneBuilder did not run or built almost nothing.");

            for (var i = 0; i < filters.Count; i++)
            {
                var mesh = filters[i].sharedMesh;
                Assert.That(mesh, Is.Not.Null, "MeshFilter on '" + filters[i].name + "' has no mesh.");
                Assert.That(mesh.vertexCount, Is.GreaterThan(0),
                    "Mesh on '" + filters[i].name + "' has no vertices.");
            }
        }

        /// <summary>Every renderer has a material with a real shader.</summary>
        /// <remarks>
        /// A material whose shader is null draws nothing at all — no error, not even magenta — and
        /// is indistinguishable on screen from a zone that was never built. That is the failure the
        /// old <c>Shader.Find(a) ?? Shader.Find(b)</c> chain could produce, so it gets its own test.
        /// </remarks>
        [UnityTest]
        public IEnumerator EveryRenderer_HasAMaterialWithARealShader()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var zone = SceneManager.GetSceneByName(ContentIds.ZoneRibcage);
            var renderers = FindAll<MeshRenderer>(zone);
            Assert.That(renderers.Count, Is.GreaterThan(0), "Nothing to check: the zone has no renderers.");

            for (var i = 0; i < renderers.Count; i++)
            {
                var material = renderers[i].sharedMaterial;
                Assert.That(material, Is.Not.Null,
                    "Renderer '" + renderers[i].name + "' has no material.");
                Assert.That(material.shader, Is.Not.Null,
                    "Material on '" + renderers[i].name + "' has a NULL shader, so it draws nothing.");
            }
        }

        /// <summary>The camera can see the world: layer, enablement and frustum all line up.</summary>
        [UnityTest]
        public IEnumerator TheCamera_ActuallyHasTheWorldInView()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var zone = SceneManager.GetSceneByName(ContentIds.ZoneRibcage);
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");
            Assert.That(camera.enabled, Is.True, "The camera is disabled.");

            Assert.That(ZoneDiagnostics.CountEnabledRenderers(zone, camera.cullingMask),
                Is.GreaterThan(0),
                "No enabled renderer in the zone is on a layer this camera's culling mask includes.");

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var inView = 0;
            var renderers = FindAll<MeshRenderer>(zone);
            for (var i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].enabled && GeometryUtility.TestPlanesAABB(planes, renderers[i].bounds))
                {
                    inView++;
                }
            }

            Assert.That(inView, Is.GreaterThan(0),
                "The zone has drawable geometry but NONE of it is inside the camera frustum. The "
                + "camera is pointed away from the world, or is somewhere the world is not. Camera at "
                + camera.transform.position.ToString("F1") + " facing " + camera.transform.forward.ToString("F2"));
        }

        /// <summary>The player stands on the ground, inside the built area, not under or beyond it.</summary>
        [UnityTest]
        public IEnumerator ThePlayer_StandsInsideTheBuiltArea()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(rig, Is.Not.Null, "No PlayerRig.");
            Assert.That(rig.GetComponent<CharacterController>(), Is.Not.Null, "No CharacterController.");

            var position = rig.transform.position;

            // The ground mesh is 120 m square centred on the origin (ZoneBuilder.GroundSize), so
            // half of that is the edge of the world. The first version of this test said 60 m
            // because I assumed the size rather than reading it.
            Assert.That(Mathf.Abs(position.x), Is.LessThan(60f), "Player is outside the ground in X.");
            Assert.That(Mathf.Abs(position.z), Is.LessThan(60f), "Player is outside the ground in Z.");
            Assert.That(position.y, Is.GreaterThan(-2f).And.LessThan(40f),
                "Player is under the terrain or far above it: y = " + position.y.ToString("F1"));
        }

        /// <summary>The Standing Stone exists as drawable geometry, not only as a registration.</summary>
        /// <remarks>
        /// The prompt reading "Standing Stone" proves a component registered itself with the
        /// interaction system. It says nothing about whether the object has a mesh. This is the
        /// difference, asserted.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheStandingStone_IsDrawableGeometryAndNotJustARegistration()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var marker = UnityEngine.Object.FindAnyObjectByType<AncientMarker>(FindObjectsInactive.Include);
            Assert.That(marker, Is.Not.Null, "No AncientMarker in the Ribcage.");

            var renderer = marker.GetComponentInChildren<MeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null,
                "The Standing Stone registered itself but has no MeshRenderer: the prompt would read "
                + "correctly while nothing was ever drawn.");

            var filter = marker.GetComponentInChildren<MeshFilter>(true);
            Assert.That(filter, Is.Not.Null, "The Standing Stone has no MeshFilter.");
            Assert.That(filter.sharedMesh, Is.Not.Null, "The Standing Stone's MeshFilter has no mesh.");
            Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0),
                "The Standing Stone's mesh has no vertices.");
        }

        /// <summary>The zone owns the active scene while it is on screen.</summary>
        /// <remarks>
        /// Runtime objects created with no scene specified land in the active scene, and
        /// RenderSettings are per scene. With Bootstrap active, the zone's own fog and ambient were
        /// being written into the boot scene and left there after the zone unloaded.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheLoadedZone_IsTheActiveScene()
        {
            var context = RequireContext();
            yield return StartRun(context);

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(ContentIds.ZoneRibcage),
                "The active scene is '" + SceneManager.GetActiveScene().name + "', not the zone the "
                + "player is standing in.");
        }

        /// <summary>The world is shaded bright enough to actually be seen.</summary>
        /// <remarks>
        /// THIS IS THE TEST THAT WOULD HAVE CAUGHT THE FAILURE, and the reason it is worth having:
        /// every other assertion in this file passed while the screen was black. The zone had 58
        /// renderers, a real Standard shader, a camera looking straight at it and nothing culled —
        /// and resolved to 0.077 luminance, seven per cent grey.
        /// <para>
        /// A structural test cannot tell "dark by design" from "invisible", so this asserts the one
        /// quantity that can: the ground's lit colour as it reaches the screen.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator TheGround_IsShadedBrightEnoughToSee()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var zone = SceneManager.GetSceneByName(ContentIds.ZoneRibcage);
            Assert.That(zone.IsValid() && zone.isLoaded, Is.True, "ZoneRibcage is not loaded.");

            var sun = FindDirectionalLight(zone);
            Assert.That(sun, Is.Not.Null, "The zone has no directional light, so nothing is lit.");
            Assert.That(sun.enabled, Is.True, "The zone's directional light is disabled.");

            var luminance = ZoneDiagnostics.EstimateGroundLuminance(zone, sun);
            Assert.That(luminance, Is.GreaterThanOrEqualTo(0f), "No ground material was found to measure.");

            Assert.That(luminance, Is.GreaterThan(MinimumReadableLuminance),
                "The ground resolves to " + luminance.ToString("F3") + " screen luminance. Nothing is "
                + "culled and nothing is missing -- the world is shaded to black. The shipped Ribcage "
                + "was 0.077, which is what an entirely black screen looks like from a zone that "
                + "renders perfectly.");

            // An upper bound too: this is a bleak northern shore, not a beach at noon. A test that
            // only pushes one way invites the fix of turning everything white.
            Assert.That(luminance, Is.LessThan(0.75f),
                "The ground is washed out at " + luminance.ToString("F3") + " luminance.");
        }

        /// <summary>The camera is pointed at the world, not past it or into the ground.</summary>
        [UnityTest]
        public IEnumerator TheCamera_PointsAcrossTheGroundNotIntoIt()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");

            var forward = camera.transform.forward;

            // Straight down or straight up both fill the screen with one surface or with sky, and
            // both look like a broken world. The rig spawns level, so this should be near zero.
            Assert.That(Mathf.Abs(forward.y), Is.LessThan(0.6f),
                "The camera is pitched "
                + (Mathf.Asin(forward.y) * Mathf.Rad2Deg).ToString("F0")
                + "° from level, so it is looking at the sky or at its own feet.");

            var zone = SceneManager.GetSceneByName(ContentIds.ZoneRibcage);
            var ground = FindGround(zone);
            Assert.That(ground, Is.Not.Null, "No ground renderer in the zone.");
            Assert.That(camera.transform.position.y, Is.GreaterThan(ground.bounds.min.y),
                "The camera is below the ground mesh entirely.");
        }

        /// <summary>The first thing the player is told about is in front of them.</summary>
        /// <remarks>
        /// The Standing Stone sat at z = -1.5 with the rig spawning at the origin facing +Z: 2.45 m
        /// away at 128° from forward, squarely behind the head. Proximity does not care about
        /// facing, so INSPECT was up from the first frame while the stone was never on screen —
        /// which is precisely the difference between a logical object existing and a visible mesh
        /// existing, and the reason this assertion is about an angle rather than about a component.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheStandingStone_IsInFrontOfTheSpawn()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");

            var marker = UnityEngine.Object.FindAnyObjectByType<AncientMarker>(FindObjectsInactive.Include);
            Assert.That(marker, Is.Not.Null, "No AncientMarker in the Ribcage.");

            var renderer = marker.GetComponentInChildren<MeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null, "The Standing Stone has no MeshRenderer.");

            var toStone = renderer.bounds.center - camera.transform.position;
            var angle = Vector3.Angle(camera.transform.forward, toStone);

            Assert.That(angle, Is.LessThan(75f),
                "The Standing Stone is " + angle.ToString("F0") + "° off the camera's forward vector, "
                + "so the player spawns with it behind them. It shipped at 128°.");
        }

        /// <summary>The camera is above the ground, not inside the island looking out.</summary>
        /// <remarks>
        /// The ground is a single-sided mesh: from below it, every face is back-facing and culled,
        /// so the view is black apart from the parts of rocks that <c>ScatterRocks</c> deliberately
        /// buries. That is what a correct world looks like when the camera is under it, and no
        /// structural check can tell it apart from an empty zone.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheCamera_IsAboveTheGroundNotInsideTheIsland()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");

            Physics.SyncTransforms();
            var eye = camera.transform.position;

            RaycastHit hit;
            var found = Physics.Raycast(
                new Vector3(eye.x, eye.y + 250f, eye.z), Vector3.down, out hit, 500f);

            Assert.That(found, Is.True, "Nothing solid under the camera at all.");
            Assert.That(eye.y, Is.GreaterThan(hit.point.y),
                "The camera is UNDER the terrain: eye y=" + eye.y.ToString("F2")
                + ", ground y=" + hit.point.y.ToString("F2")
                + ". From below, the single-sided ground is entirely culled and the screen is black.");
        }

        // --- helpers ---------------------------------------------------------------------------

        private static Light FindDirectionalLight(Scene scene)
        {
            var lights = FindAll<Light>(scene);
            for (var i = 0; i < lights.Count; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }

        private static Renderer FindGround(Scene scene)
        {
            var renderers = FindAll<MeshRenderer>(scene);
            for (var i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].name.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return renderers[i];
                }
            }

            return null;
        }

        private static System.Collections.Generic.List<T> FindAll<T>(Scene scene) where T : Component
        {
            var found = new System.Collections.Generic.List<T>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return found;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                found.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return found;
        }

        private IEnumerator StartRun(GameContext context)
        {
            var started = context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            Assert.That(started.Success, Is.True, "StartNewGameCommand was rejected: " + started.Code);

            yield return WaitFor(
                () => context.States.Current == GameStateId.InGame &&
                      context.Session.ZoneId == ContentIds.ZoneRibcage,
                LoadTimeoutSeconds);

            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Never reached InGame.");

            // Furnishing runs on the scene-loaded callback; one more frame guarantees it finished
            // and that bounds have been computed for the frustum checks above.
            yield return null;
            yield return null;
        }

        private static IEnumerator WaitForBoot()
        {
            yield return WaitFor(() => FindBootstrap() != null, BootTimeoutSeconds);
            var bootstrap = FindBootstrap();
            Assert.That(bootstrap, Is.Not.Null, "AppBootstrap never created its host object.");
            yield return WaitFor(() => bootstrap.Context != null, BootTimeoutSeconds);
        }

        private static IEnumerator ReturnToMenu()
        {
            var bootstrap = FindBootstrap();
            if (bootstrap == null || bootstrap.Context == null)
            {
                yield break;
            }

            var context = bootstrap.Context;
            yield return WaitFor(() => !context.SceneLoader.IsLoading, LoadTimeoutSeconds);

            var current = context.States.Current;
            if (current == GameStateId.InGame || current == GameStateId.Paused)
            {
                context.Commands.Dispatch(new QuitToMenuCommand());
            }
            else if (current == GameStateId.LoadFailed)
            {
                context.States.TryTransition(GameStateId.MainMenu);
                context.Zones.UnloadAll();
            }

            yield return WaitFor(
                () => context.Zones.ResidentCount == 0 && !context.SceneLoader.IsLoading,
                LoadTimeoutSeconds);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static AppBootstrap FindBootstrap()
        {
            return UnityEngine.Object.FindAnyObjectByType<AppBootstrap>(FindObjectsInactive.Include);
        }

        private static GameContext RequireContext()
        {
            var bootstrap = FindBootstrap();
            Assert.That(bootstrap, Is.Not.Null, "No AppBootstrap in the scene; boot never ran.");
            Assert.That(bootstrap.Context, Is.Not.Null, "AppBootstrap exists but built no GameContext.");
            return bootstrap.Context;
        }

        private static void RequireScenesInBuild()
        {
            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                if (!IsSceneInBuild(key))
                {
                    Assert.Ignore(
                        "Scene '" + key + "' is not in Build Settings. "
                        + "Run Vardholm > Setup Project, then re-run these tests.");
                }
            }
        }

        private static bool IsSceneInBuild(string sceneKey)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (path.EndsWith("/" + sceneKey + ".unity", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
