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

            // The ground mesh is 60 m square centred on the origin, so anything beyond half of that
            // is standing off the edge of the world.
            Assert.That(Mathf.Abs(position.x), Is.LessThan(30f), "Player is outside the ground in X.");
            Assert.That(Mathf.Abs(position.z), Is.LessThan(30f), "Player is outside the ground in Z.");
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

        // --- helpers ---------------------------------------------------------------------------

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
