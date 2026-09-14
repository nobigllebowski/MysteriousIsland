using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Diagnostics
{
    /// <summary>
    /// Dumps everything about a furnished zone that decides whether it can be seen, in one log call.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS. A zone that renders nothing and a zone that was never built look identical
    /// on screen, and the earlier furnish report could not tell them apart: it said "camera yes,
    /// player yes" and stopped, which is true of both. Four separate diagnoses were made from
    /// screenshots and three of them were wrong, because a screenshot of a black screen contains no
    /// information about why it is black.
    /// <para>
    /// So this reports the facts that actually discriminate, in one place: how many renderers exist,
    /// how many are enabled, what shader they use, where they are, where the camera is, which way it
    /// points, what it can cull, and whether any renderer is inside its view at all. Whichever of
    /// those is wrong, the answer is in the dump rather than in the next guess.
    /// </para>
    /// <para>
    /// It is not development-only. It runs once per zone entry, costs one string, and the situation
    /// it exists for is precisely the one where a development build is not what is in front of you.
    /// </para>
    /// </remarks>
    public static class ZoneDiagnostics
    {
        private const string Prefix = "[Vardholm] ZONE DUMP";

        /// <summary>Name of the file the report is also written to, under persistentDataPath.</summary>
        public const string DumpFileName = "vardholm-zone-dump.txt";

        /// <summary>Renderers further than this from the camera are reported separately as "far".</summary>
        private const float NearRadius = 60f;

        /// <summary>
        /// Writes the full structural report for <paramref name="scene"/>.
        /// </summary>
        /// <param name="scene">The furnished zone.</param>
        /// <param name="camera">The camera meant to render it. Null is reported, not thrown on.</param>
        /// <param name="playerBody">The player's transform, for distance figures. Null tolerated.</param>
        public static void Dump(Scene scene, Camera camera, Transform playerBody)
        {
            var text = Build(scene, camera, playerBody);

            // ALSO WRITTEN TO A FILE, because relaying a console block by hand has failed several
            // times running: the console list shows one line, the detail pane is easy to miss, and
            // a screenshot of it loses exactly the digits that matter. The file is one attachment.
            WriteToFile(text);

            // One call, and an error rather than a log when the zone cannot be seen: a report nobody
            // reads is the same as no report, and the console only makes noise about errors.
            if (RendersNothing(scene, camera))
            {
                Debug.LogError(text);
                return;
            }

            Debug.Log(text);
        }

        /// <summary>Writes the report where it can be attached to a message in one action.</summary>
        /// <remarks>
        /// <c>Application.persistentDataPath</c> because it is writable on every platform this game
        /// targets, including a device build, which is where this will eventually matter most. The
        /// path is logged so nobody has to know what that resolves to.
        /// </remarks>
        private static void WriteToFile(string text)
        {
            try
            {
                var path = System.IO.Path.Combine(Application.persistentDataPath, DumpFileName);
                System.IO.File.WriteAllText(path, text);
                Debug.Log("[Vardholm] zone dump written to: " + path);
            }
            catch (System.Exception exception)
            {
                // A diagnostic that throws is worse than a diagnostic that is missing.
                Debug.LogWarning("[Vardholm] could not write the zone dump: " + exception.Message);
            }
        }

        /// <summary>True when nothing in this zone can reach the screen.</summary>
        public static bool RendersNothing(Scene scene, Camera camera)
        {
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
            {
                return true;
            }

            return CountEnabledRenderers(scene, camera.cullingMask) == 0;
        }

        /// <summary>Enabled renderers in <paramref name="scene"/> that the given mask can see.</summary>
        public static int CountEnabledRenderers(Scene scene, int cullingMask)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            var count = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    var renderer = renderers[r];
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    // The layer has to be in the mask or the camera will never draw it, however
                    // correct everything else about the object is.
                    if ((cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    {
                        continue;
                    }

                    count++;
                }
            }

            return count;
        }

        private static string Build(Scene scene, Camera camera, Transform playerBody)
        {
            var b = new StringBuilder(2048);

            b.Append(Prefix).Append(" — '").Append(scene.name).Append("'\n");

            // --- the environment the whole thing renders in -------------------------------------
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            b.Append("  pipeline ").Append(pipeline != null ? pipeline.name : "Built-in")
             .Append(" · colour space ").Append(QualitySettings.activeColorSpace)
             .Append(" · active scene '").Append(SceneManager.GetActiveScene().name).Append('\'')
             .Append(" · scene valid ").Append(scene.IsValid())
             .Append(" · loaded ").Append(scene.isLoaded)
             .Append('\n');

            b.Append("  ambient ").Append(Fmt(RenderSettings.ambientLight))
             .Append(" · fog ").Append(RenderSettings.fog)
             .Append(" density ").Append(RenderSettings.fogDensity.ToString("F3", CultureInfo.InvariantCulture))
             .Append(" colour ").Append(Fmt(RenderSettings.fogColor))
             .Append('\n');

            // --- the camera ---------------------------------------------------------------------
            if (camera == null)
            {
                b.Append("  CAMERA: none. Nothing can be drawn.\n");
            }
            else
            {
                var t = camera.transform;
                b.Append("  camera '").Append(camera.name).Append("' at ").Append(t.position.ToString("F2"))
                 .Append(" · euler ").Append(t.eulerAngles.ToString("F1"))
                 .Append(" · forward ").Append(t.forward.ToString("F2"))
                 .Append('\n');

                b.Append("    enabled ").Append(camera.enabled)
                 .Append(" · active ").Append(camera.gameObject.activeInHierarchy)
                 .Append(" · near ").Append(camera.nearClipPlane.ToString("F3", CultureInfo.InvariantCulture))
                 .Append(" · far ").Append(camera.farClipPlane.ToString("F0", CultureInfo.InvariantCulture))
                 .Append(" · fov ").Append(camera.fieldOfView.ToString("F0", CultureInfo.InvariantCulture))
                 .Append(" · clear ").Append(camera.clearFlags)
                 .Append(" · bg ").Append(Fmt(camera.backgroundColor))
                 .Append(" · mask 0x").Append(camera.cullingMask.ToString("X8"))
                 .Append(" · depth ").Append(camera.depth.ToString("F0", CultureInfo.InvariantCulture))
                 .Append(" · scene '").Append(camera.gameObject.scene.name).Append('\'')
                 .Append('\n');
            }

            // Above or below the ground is the difference between a view of an island and a view of
            // the inside of it. From below, a single-sided terrain is entirely culled and the screen
            // is black except for whatever pokes through from underneath.
            if (camera != null)
            {
                Physics.SyncTransforms();

                // Named for this block, not 'eye': the renderer loop further down declares its own
                // 'eye' in the enclosing method scope, and C# forbids the two (CS0136) even though
                // the blocks never overlap.
                var probeOrigin = camera.transform.position;
                RaycastHit hit;
                if (Physics.Raycast(
                    new Vector3(probeOrigin.x, probeOrigin.y + 250f, probeOrigin.z),
                    Vector3.down, out hit, 500f))
                {
                    b.Append("  ground under the camera: y=")
                     .Append(hit.point.y.ToString("F2", CultureInfo.InvariantCulture))
                     .Append(" on '").Append(hit.collider.name).Append("' · eye y=")
                     .Append(probeOrigin.y.ToString("F2", CultureInfo.InvariantCulture))
                     .Append(probeOrigin.y < hit.point.y
                         ? "  ← CAMERA IS UNDER THE TERRAIN. Everything above it is back-facing and culled."
                         : "  (above ground)")
                     .Append('\n');
                }
                else
                {
                    b.Append("  ground under the camera: NO COLLIDER HIT — nothing to stand on here.\n");
                }
            }

            if (playerBody != null)
            {
                b.Append("  player at ").Append(playerBody.position.ToString("F2"))
                 .Append(" · scene '").Append(playerBody.gameObject.scene.name).Append('\'')
                 .Append('\n');
            }

            // --- what is actually in the zone ----------------------------------------------------
            if (!scene.IsValid() || !scene.isLoaded)
            {
                b.Append("  SCENE IS NOT LOADED. There is nothing to look at.\n");
                return b.ToString();
            }

            var roots = scene.GetRootGameObjects();
            b.Append("  root objects: ").Append(roots.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');

            var totalRenderers = 0;
            var enabledRenderers = 0;
            var visibleRenderers = 0;
            var inMask = 0;
            var meshFilters = 0;
            var emptyFilters = 0;
            var colliders = 0;
            var nullShaders = 0;
            var nearest = float.MaxValue;
            var nearestName = "none";
            var shaderNames = new StringBuilder();

            var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            var eye = camera != null ? camera.transform.position : Vector3.zero;

            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                var rootRenderers = root.GetComponentsInChildren<Renderer>(true);

                b.Append("    • '").Append(root.name).Append("' at ").Append(root.transform.position.ToString("F1"))
                 .Append(" · active ").Append(root.activeInHierarchy)
                 .Append(" · renderers ").Append(rootRenderers.Length.ToString(CultureInfo.InvariantCulture))
                 .Append(" · lights ").Append(root.GetComponentsInChildren<Light>(true).Length.ToString(CultureInfo.InvariantCulture))
                 .Append('\n');

                colliders += root.GetComponentsInChildren<Collider>(true).Length;

                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                meshFilters += filters.Length;
                for (var f = 0; f < filters.Length; f++)
                {
                    var mesh = filters[f].sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        emptyFilters++;
                    }
                }

                for (var r = 0; r < rootRenderers.Length; r++)
                {
                    var renderer = rootRenderers[r];
                    totalRenderers++;

                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    enabledRenderers++;

                    if (camera != null && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                    {
                        inMask++;
                    }

                    var material = renderer.sharedMaterial;

                    // The check that matters most: a material whose shader is null draws absolutely
                    // nothing, with no error and no magenta. It is indistinguishable from an empty
                    // world, which is exactly why it has to be counted rather than assumed.
                    if (material == null || material.shader == null)
                    {
                        nullShaders++;
                    }
                    else if (shaderNames.Length < 200 && shaderNames.ToString().IndexOf(material.shader.name) < 0)
                    {
                        if (shaderNames.Length > 0)
                        {
                            shaderNames.Append(", ");
                        }

                        shaderNames.Append(material.shader.name);
                    }

                    var distance = Vector3.Distance(eye, renderer.bounds.center);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        nearestName = renderer.name;
                    }

                    if (planes != null && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    {
                        visibleRenderers++;
                    }
                }
            }

            b.Append("  TOTALS: renderers ").Append(totalRenderers.ToString(CultureInfo.InvariantCulture))
             .Append(" · enabled ").Append(enabledRenderers.ToString(CultureInfo.InvariantCulture))
             .Append(" · in culling mask ").Append(inMask.ToString(CultureInfo.InvariantCulture))
             .Append(" · inside frustum ").Append(visibleRenderers.ToString(CultureInfo.InvariantCulture))
             .Append('\n');

            b.Append("    meshFilters ").Append(meshFilters.ToString(CultureInfo.InvariantCulture))
             .Append(" (empty ").Append(emptyFilters.ToString(CultureInfo.InvariantCulture)).Append(')')
             .Append(" · colliders ").Append(colliders.ToString(CultureInfo.InvariantCulture))
             .Append(" · NULL SHADERS ").Append(nullShaders.ToString(CultureInfo.InvariantCulture))
             .Append(" · shaders [").Append(shaderNames.Length > 0 ? shaderNames.ToString() : "none").Append(']')
             .Append('\n');

            b.Append("    nearest renderer ")
             .Append(nearest < float.MaxValue
                 ? "'" + nearestName + "' at " + nearest.ToString("F1", CultureInfo.InvariantCulture)
                   + " m from the camera"
                   + (nearest < 1.5f ? "  ← POINT BLANK: this object is over the lens" : string.Empty)
                 : "none")
             .Append(" · within ").Append(NearRadius.ToString("F0", CultureInfo.InvariantCulture)).Append(" m: ")
             .Append(nearest <= NearRadius ? "yes" : "NO")
             .Append('\n');

            // --- the three objects that decide whether the zone reads at all ---------------------
            AppendKeyObject(b, scene, camera, "ground", "Ground");
            AppendKeyObject(b, scene, camera, "standing stone", "Marker");
            AppendKeyObject(b, scene, camera, "landmark", "Rib");

            // --- lighting, and the number the whole failure came down to -------------------------
            var sun = FindBrightestDirectionalLight(scene);
            b.Append("  light ")
             .Append(sun != null
                 ? "'" + sun.name + "' intensity " + sun.intensity.ToString("F2", CultureInfo.InvariantCulture)
                   + " elevation " + sun.transform.eulerAngles.x.ToString("F0", CultureInfo.InvariantCulture) + "°"
                   + " colour " + Fmt(sun.color) + " enabled " + sun.enabled
                 : "NONE — nothing is lit")
             .Append('\n');

            var luminance = EstimateGroundLuminance(scene, sun);
            b.Append("  ESTIMATED GROUND LUMINANCE ")
             .Append(luminance >= 0f ? luminance.ToString("F3", CultureInfo.InvariantCulture) : "n/a")
             .Append(luminance >= 0f && luminance < ReadableLuminance
                 ? "  ← BELOW " + ReadableLuminance.ToString("F2", CultureInfo.InvariantCulture)
                   + ": the geometry renders and is shaded to near-black."
                 : string.Empty)
             .Append('\n');

            AppendVisibilityCategories(b, scene, camera);

            b.Append("  VERDICT: ").Append(Verdict(
                totalRenderers, enabledRenderers, inMask, visibleRenderers, nullShaders, camera, luminance));

            return b.ToString();
        }

        /// <summary>
        /// Sorts every renderer into the one category that explains why it is or is not on screen.
        /// </summary>
        /// <remarks>
        /// The counts are exclusive and checked in the order the pipeline applies them, so exactly
        /// one number is non-zero when something is systematically wrong and the block names it
        /// without further reading: everything behind the camera is an aim or placement problem,
        /// everything culled by layer is a mask problem, everything with zero or non-finite bounds
        /// is a mesh problem, and everything visible with a black screen is a shading problem.
        /// </remarks>
        private static void AppendVisibilityCategories(StringBuilder b, Scene scene, Camera camera)
        {
            int total = 0, enabled = 0, visible = 0, behind = 0, outsideClip = 0;
            int culledByLayer = 0, zeroBounds = 0, nonFinite = 0, withTriangles = 0;

            var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            var eye = camera != null ? camera.transform.position : Vector3.zero;
            var forward = camera != null ? camera.transform.forward : Vector3.forward;
            var near = camera != null ? camera.nearClipPlane : 0f;
            var far = camera != null ? camera.farClipPlane : float.MaxValue;

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    var renderer = renderers[r];
                    total++;

                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    enabled++;

                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh != null && mesh.triangles.Length >= 3)
                    {
                        withTriangles++;
                    }

                    var bounds = renderer.bounds;
                    if (!IsFinite(bounds.center) || !IsFinite(bounds.size))
                    {
                        nonFinite++;
                        continue;
                    }

                    if (bounds.size.sqrMagnitude < 1e-8f)
                    {
                        zeroBounds++;
                        continue;
                    }

                    if (camera != null && (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    {
                        culledByLayer++;
                        continue;
                    }

                    var toObject = bounds.center - eye;
                    if (Vector3.Dot(toObject, forward) < 0f)
                    {
                        behind++;
                        continue;
                    }

                    var distance = toObject.magnitude;
                    if (distance + bounds.extents.magnitude < near || distance - bounds.extents.magnitude > far)
                    {
                        outsideClip++;
                        continue;
                    }

                    if (planes != null && GeometryUtility.TestPlanesAABB(planes, bounds))
                    {
                        visible++;
                    }
                }
            }

            b.Append("  WORLD VISIBILITY: renderers ").Append(total.ToString(CultureInfo.InvariantCulture))
             .Append(" · enabled ").Append(enabled.ToString(CultureInfo.InvariantCulture))
             .Append(" · withTriangles ").Append(withTriangles.ToString(CultureInfo.InvariantCulture))
             .Append(" · visibleInFrustum ").Append(visible.ToString(CultureInfo.InvariantCulture))
             .Append(" · behindCamera ").Append(behind.ToString(CultureInfo.InvariantCulture))
             .Append(" · outsideClip ").Append(outsideClip.ToString(CultureInfo.InvariantCulture))
             .Append(" · culledByLayer ").Append(culledByLayer.ToString(CultureInfo.InvariantCulture))
             .Append(" · zeroBounds ").Append(zeroBounds.ToString(CultureInfo.InvariantCulture))
             .Append(" · nonFiniteBounds ").Append(nonFinite.ToString(CultureInfo.InvariantCulture))
             .Append('\n');
        }

        /// <summary>True when no component is NaN or infinite. Bounds built from a bad mesh are both.</summary>
        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                   && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        /// <summary>
        /// Reports one named object in full: mesh, material, bounds, distance, and whether it is
        /// actually in view.
        /// </summary>
        /// <remarks>
        /// THE DISTINCTION THIS EXISTS FOR: a proximity prompt reading "Standing Stone" proves that
        /// a component registered itself with the interaction system at a distance. It proves
        /// nothing about whether the object has a mesh, whether that mesh has triangles, or whether
        /// it is in front of the camera rather than behind the player's head — and the stone was
        /// behind the player's head, 2.45 m away at 128° from forward, which is why the prompt was
        /// up from the first frame and the stone was never once on screen.
        /// <para>
        /// The angle to camera forward is the line that separates those two facts, so it is printed
        /// for every object here.
        /// </para>
        /// </remarks>
        private static void AppendKeyObject(
            StringBuilder b, Scene scene, Camera camera, string label, string nameFragment)
        {
            var renderer = FindRendererByName(scene, nameFragment);
            b.Append("  ").Append(label).Append(": ");

            if (renderer == null)
            {
                b.Append("NOT FOUND (searched for a renderer whose name contains '")
                 .Append(nameFragment).Append("')\n");
                return;
            }

            var t = renderer.transform;
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            var material = renderer.sharedMaterial;

            b.Append('\'').Append(renderer.name).Append('\'')
             .Append(" pos ").Append(t.position.ToString("F2"))
             .Append(" scale ").Append(t.lossyScale.ToString("F2"))
             .Append(" activeInHierarchy ").Append(renderer.gameObject.activeInHierarchy)
             .Append(" enabled ").Append(renderer.enabled)
             .Append(" isVisible ").Append(renderer.isVisible)
             .Append('\n');

            b.Append("      mesh ")
             .Append(mesh != null
                 ? "verts " + mesh.vertexCount.ToString(CultureInfo.InvariantCulture)
                   + " tris " + (mesh.triangles.Length / 3).ToString(CultureInfo.InvariantCulture)
                   + " localBounds c" + mesh.bounds.center.ToString("F1") + " s" + mesh.bounds.size.ToString("F1")
                 : "NONE")
             .Append(" · worldBounds c").Append(renderer.bounds.center.ToString("F1"))
             .Append(" s").Append(renderer.bounds.size.ToString("F1"))
             .Append('\n');

            b.Append("      material ")
             .Append(material != null ? material.name : "NONE")
             .Append(" · shader ").Append(material != null && material.shader != null ? material.shader.name : "NULL")
             .Append(" · colour ").Append(material != null && material.HasProperty("_Color")
                 ? Fmt(material.GetColor("_Color"))
                 : "n/a")
             .Append(" · queue ").Append(material != null
                 ? material.renderQueue.ToString(CultureInfo.InvariantCulture)
                 : "n/a")
             .Append('\n');

            if (camera == null)
            {
                return;
            }

            var toObject = renderer.bounds.center - camera.transform.position;
            var distance = toObject.magnitude;
            var angle = distance > 0.001f
                ? Vector3.Angle(camera.transform.forward, toObject)
                : 0f;

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var inFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);

            b.Append("      distance ").Append(distance.ToString("F1", CultureInfo.InvariantCulture))
             .Append(" m · angle off camera forward ").Append(angle.ToString("F0", CultureInfo.InvariantCulture))
             .Append("° (").Append(angle > 90f ? "BEHIND THE CAMERA" : "in front")
             .Append(") · in frustum ").Append(inFrustum ? "YES" : "NO")
             .Append('\n');
        }

        private static Renderer FindRendererByName(Scene scene, string fragment)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r].name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return renderers[r];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Screen luminance below which a surface reads as black rather than as dark.
        /// </summary>
        /// <remarks>
        /// The Ribcage shipped at <b>0.077</b>. Anything under this is not "moody", it is invisible.
        /// </remarks>
        private const float ReadableLuminance = 0.18f;

        /// <summary>The brightest enabled directional light in the zone, or null.</summary>
        private static Light FindBrightestDirectionalLight(Scene scene)
        {
            Light best = null;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var lights = roots[i].GetComponentsInChildren<Light>(true);
                for (var l = 0; l < lights.Length; l++)
                {
                    var light = lights[l];
                    if (light.type != LightType.Directional || !light.enabled)
                    {
                        continue;
                    }

                    if (best == null || light.intensity > best.intensity)
                    {
                        best = light;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// What the ground's lit colour will look like on screen, 0 (black) to 1 (white).
        /// </summary>
        /// <remarks>
        /// THIS IS THE NUMBER THAT WOULD HAVE ANSWERED IT IN ONE LINE, and none of the earlier
        /// checks computed anything like it: every one of them asked whether an object existed, and
        /// the object always did. A surface that renders perfectly and resolves to seven per cent
        /// grey is indistinguishable, on screen and in every structural test, from no surface.
        /// <para>
        /// The model is deliberately the simplest one that predicts the failure: Lambert on a flat
        /// upward-facing surface, plus flat ambient, with the sRGB→linear conversion that Linear
        /// colour space applies to a material's colour. That conversion is the trap — an albedo of
        /// 0.13 is 0.014 once converted, so values that look reasonable in the inspector arrive a
        /// fifth as bright. Specular, shadows and fog are ignored; all three only subtract, so this
        /// is an upper bound, and an upper bound that is already too dark settles the question.
        /// </para>
        /// </remarks>
        /// <param name="scene">The zone whose ground material to read.</param>
        /// <param name="sun">The directional light shading it. Null yields ambient only.</param>
        /// <returns>Luminance in 0..1, or -1 when no ground material could be found.</returns>
        public static float EstimateGroundLuminance(Scene scene, Light sun)
        {
            var ground = FindGroundMaterial(scene);
            if (ground == null)
            {
                return -1f;
            }

            var albedo = ground.HasProperty("_BaseColor")
                ? ground.GetColor("_BaseColor")
                : ground.HasProperty("_Color") ? ground.GetColor("_Color") : Color.grey;

            var linearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var a = linearSpace ? albedo.linear : albedo;
            var ambient = linearSpace ? RenderSettings.ambientLight.linear : RenderSettings.ambientLight;

            // Elevation from the light's own direction rather than its euler angles, which are only
            // the same thing while the light has no parent rotation.
            var ndotl = sun != null ? Mathf.Max(0f, Vector3.Dot(-sun.transform.forward, Vector3.up)) : 0f;
            var intensity = sun != null ? sun.intensity : 0f;
            var sunColor = sun != null ? (linearSpace ? sun.color.linear : sun.color) : Color.black;

            var r = a.r * intensity * ndotl * sunColor.r + a.r * ambient.r;
            var g = a.g * intensity * ndotl * sunColor.g + a.g * ambient.g;
            var bl = a.b * intensity * ndotl * sunColor.b + a.b * ambient.b;

            var lit = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(bl));
            var shown = linearSpace ? lit.gamma : lit;

            return 0.2126f * shown.r + 0.7152f * shown.g + 0.0722f * shown.b;
        }

        private static Material FindGroundMaterial(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var renderers = roots[i].GetComponentsInChildren<MeshRenderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r].name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return renderers[r].sharedMaterial;
                    }
                }
            }

            return null;
        }

        /// <summary>Names the first thing in the chain that is wrong, in the order it breaks.</summary>
        private static string Verdict(
            int total, int enabled, int inMask, int visible, int nullShaders, Camera camera, float luminance)
        {
            if (camera == null)
            {
                return "no camera. Nothing renders.";
            }

            if (total == 0)
            {
                return "the zone contains NO renderers. ZoneBuilder did not run, or built nothing. "
                       + "This is a construction problem, not a rendering one.";
            }

            if (enabled == 0)
            {
                return "renderers exist but every one is disabled or on an inactive object.";
            }

            if (inMask == 0)
            {
                return "renderers are enabled but none is on a layer this camera's culling mask includes.";
            }

            if (nullShaders == enabled)
            {
                return "every renderer has a null shader. The material chain resolved nothing, so the "
                       + "geometry is drawn with no shader at all — invisible, with no error.";
            }

            if (visible == 0)
            {
                return "renderers are enabled and drawable but NONE is inside the camera frustum. "
                       + "The camera is pointed away from the world, or is somewhere the world is not.";
            }

            if (nullShaders > 0)
            {
                return visible + " renderer(s) in view, but " + nullShaders + " have a null shader.";
            }

            if (luminance >= 0f && luminance < ReadableLuminance)
            {
                return visible + " renderer(s) in view and drawable, but the ground resolves to "
                       + luminance.ToString("F3", CultureInfo.InvariantCulture) + " luminance. Nothing is "
                       + "culled and nothing is missing — the world is being SHADED TO BLACK. Raise the "
                       + "albedo, the sun intensity or its elevation in ZoneBuilder's recipe.";
            }

            return visible + " renderer(s) in view and drawable, ground luminance "
                   + (luminance >= 0f ? luminance.ToString("F3", CultureInfo.InvariantCulture) : "n/a")
                   + ". This zone should be visible.";
        }

        private static string Fmt(Color c)
        {
            return "(" + c.r.ToString("F2", CultureInfo.InvariantCulture)
                   + "," + c.g.ToString("F2", CultureInfo.InvariantCulture)
                   + "," + c.b.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }
    }
}
