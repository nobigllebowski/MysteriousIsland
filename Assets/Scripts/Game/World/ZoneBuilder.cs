using ForgottenIsle.Core.Progress;
using ForgottenIsle.Game.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace ForgottenIsle.Game.World
{
    /// <summary>
    /// Builds the playable content of a zone: terrain, landmark, props, atmosphere, interactables.
    /// </summary>
    /// <remarks>
    /// Two zones, one builder, one recipe each. The recipes are code rather than data because there
    /// are exactly two of them and a data format would be a schema to maintain for no second reader.
    /// When there are nine, this becomes a definition asset (ADR-0005) — not before.
    /// <para>
    /// Determinism is the load-bearing property. Zones are destroyed and rebuilt on every entry, so
    /// a fixed seed per zone is what makes walking back to the shore return you to the same shore
    /// rather than a new one. Every random call here is seeded from the zone id.
    /// </para>
    /// <para>
    /// Everything is parented under one root and shares two materials, so a zone is a handful of
    /// draw calls rather than one per rock.
    /// </para>
    /// </remarks>
    public static class ZoneBuilder
    {
        /// <summary>Metres across. Big enough to walk, small enough to hold in memory.</summary>
        private const float GroundSize = 120f;

        /// <summary>Radius kept level around the spawn so the player never starts inside a hill.</summary>
        private const float SpawnApron = 7f;

        /// <summary>Per-zone look and content.</summary>
        private readonly struct Recipe
        {
            public readonly int Seed;
            public readonly float Amplitude;
            public readonly Color Ground;
            public readonly Color Stone;
            public readonly Color Fog;
            public readonly float FogDensity;
            public readonly Color Sun;
            public readonly float SunIntensity;
            public readonly Vector3 SunAngles;
            public readonly Color Ambient;
            public readonly int RockCount;
            public readonly int FloraCount;

            public Recipe(int seed, float amplitude, Color ground, Color stone, Color fog, float fogDensity,
                Color sun, float sunIntensity, Vector3 sunAngles, Color ambient, int rockCount, int floraCount)
            {
                Seed = seed;
                Amplitude = amplitude;
                Ground = ground;
                Stone = stone;
                Fog = fog;
                FogDensity = fogDensity;
                Sun = sun;
                SunIntensity = sunIntensity;
                SunAngles = sunAngles;
                Ambient = ambient;
                RockCount = rockCount;
                FloraCount = floraCount;
            }
        }

        // The Ribcage: open black-sand shore under a low grey sky. Sparse, wide, cold-lit, so the
        // rib arch reads from a distance and the player has an obvious direction to walk.
        private static readonly Recipe RibcageRecipe = new Recipe(
            seed: 20260914,
            amplitude: 3.2f,
            ground: new Color(0.13f, 0.15f, 0.14f),
            stone: new Color(0.30f, 0.30f, 0.28f),
            fog: new Color(0.16f, 0.20f, 0.21f),
            fogDensity: 0.012f,
            sun: new Color(0.78f, 0.82f, 0.85f),
            sunIntensity: 0.85f,
            sunAngles: new Vector3(24f, 35f, 0f),
            ambient: new Color(0.13f, 0.16f, 0.17f),
            rockCount: 26,
            floraCount: 10);

        // Fernmaw: a sunken green channel. Higher relief and much denser fog to make it feel narrow
        // and enclosed, which is the whole point of the contrast with the shore.
        private static readonly Recipe FernmawRecipe = new Recipe(
            seed: 71104,
            amplitude: 6.4f,
            ground: new Color(0.09f, 0.14f, 0.10f),
            stone: new Color(0.20f, 0.24f, 0.19f),
            fog: new Color(0.07f, 0.11f, 0.09f),
            fogDensity: 0.045f,
            sun: new Color(0.55f, 0.70f, 0.52f),
            sunIntensity: 0.45f,
            sunAngles: new Vector3(62f, 200f, 0f),
            ambient: new Color(0.06f, 0.10f, 0.08f),
            rockCount: 18,
            floraCount: 46);

        /// <summary>
        /// Builds a zone's content under <paramref name="root"/> and registers its interactables.
        /// </summary>
        /// <param name="zoneKey">Scene key naming which zone to build.</param>
        /// <param name="root">Parent for everything created.</param>
        /// <param name="interactions">System the zone's interactables register with. Null tolerated.</param>
        /// <returns>The world position the player should spawn at.</returns>
        public static Vector3 Build(string zoneKey, Transform root, InteractionSystem interactions)
        {
            var fernmaw = zoneKey == ContentIds.ZoneFernmaw;
            var recipe = fernmaw ? FernmawRecipe : RibcageRecipe;

            var groundMaterial = CreateMaterial(recipe.Ground, "ZoneGround");
            var stoneMaterial = CreateMaterial(recipe.Stone, "ZoneStone");

            BuildGround(root, recipe, groundMaterial);
            ApplyAtmosphere(root, recipe);
            ScatterRocks(root, recipe, stoneMaterial);
            ScatterFlora(root, recipe, fernmaw);

            if (fernmaw)
            {
                BuildFernmawContent(root, recipe, stoneMaterial, interactions);
            }
            else
            {
                BuildRibcageContent(root, recipe, stoneMaterial, interactions);
            }

            return new Vector3(0f, Height(0f, 0f, recipe) + 1.2f, 0f);
        }

        // --- Shared construction -------------------------------------------------------------

        private static void BuildGround(Transform root, Recipe recipe, Material material)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);

            var mesh = ZoneMeshes.BuildGround(GroundSize, recipe.Amplitude, recipe.Seed, SpawnApron);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            // A MeshCollider on a 2k-triangle mesh is acceptable here because there is exactly one
            // of them and the character controller needs real ground to follow. Rocks and flora get
            // no colliders at all -- walking through a fern is a better failure than 70 colliders.
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void ApplyAtmosphere(Transform root, Recipe recipe)
        {
            // Exponential-squared fog, set in code so no Lighting-window pass is required and the
            // setting travels with the zone rather than with a scene asset.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = recipe.Fog;
            RenderSettings.fogDensity = recipe.FogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = recipe.Ambient;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(recipe.SunAngles);

            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = recipe.Sun;
            sun.intensity = recipe.SunIntensity;

            // One shadow-casting light, soft, short range. Shadows are most of what makes the rib
            // arch read as a solid object rather than a decal, and one directional light is the
            // cheapest way to get them on mobile.
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
        }

        private static void ScatterRocks(Transform root, Recipe recipe, Material material)
        {
            var parent = new GameObject("Rocks").transform;
            parent.SetParent(root, false);

            var random = new System.Random(recipe.Seed + 17);

            // Four rock meshes reused across every instance: 26 rocks, 4 meshes, 1 material. The
            // variety comes from scale and rotation, which cost nothing.
            var meshes = new Mesh[4];
            for (var i = 0; i < meshes.Length; i++)
            {
                meshes[i] = ZoneMeshes.BuildRock(recipe.Seed + i * 31);
            }

            for (var i = 0; i < recipe.RockCount; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 11f + (float)random.NextDouble() * (GroundSize * 0.42f);
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                var scale = 0.8f + (float)random.NextDouble() * 2.6f;

                var go = new GameObject("Rock");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(x, Height(x, z, recipe) - scale * 0.25f, z);
                go.transform.rotation = Quaternion.Euler(
                    (float)random.NextDouble() * 24f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 24f);
                go.transform.localScale = Vector3.one * scale;

                go.AddComponent<MeshFilter>().sharedMesh = meshes[i % meshes.Length];
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private static void ScatterFlora(Transform root, Recipe recipe, bool fernmaw)
        {
            var parent = new GameObject("Flora").transform;
            parent.SetParent(root, false);

            var random = new System.Random(recipe.Seed + 91);
            var material = CreateMaterial(
                fernmaw ? new Color(0.10f, 0.20f, 0.12f) : new Color(0.16f, 0.17f, 0.13f),
                "ZoneFlora");

            // Quads, not meshes: flora is silhouette and density, and two crossed quads per plant
            // reads as vegetation at a fraction of the cost of any actual plant model.
            for (var i = 0; i < recipe.FloraCount; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 6f + (float)random.NextDouble() * (GroundSize * 0.45f);
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                var height = fernmaw
                    ? 1.4f + (float)random.NextDouble() * 2.4f
                    : 0.6f + (float)random.NextDouble() * 1.0f;

                var plant = new GameObject("Flora");
                plant.transform.SetParent(parent, false);
                plant.transform.position = new Vector3(x, Height(x, z, recipe), z);
                plant.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

                for (var blade = 0; blade < 2; blade++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Blade";

                    // The primitive's collider would make a forest of triggers the player bumps
                    // into. Vegetation is scenery; it is not solid.
                    Object.Destroy(quad.GetComponent<Collider>());

                    quad.transform.SetParent(plant.transform, false);
                    quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                    quad.transform.localRotation = Quaternion.Euler(0f, blade * 90f, 0f);
                    quad.transform.localScale = new Vector3(height * 0.55f, height, 1f);
                    quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        // --- Ribcage ---------------------------------------------------------------------------

        private static void BuildRibcageContent(
            Transform root, Recipe recipe, Material stone, InteractionSystem interactions)
        {
            // THE LANDMARK: six ribs in a line, leaning inward. Visible from the spawn, and the
            // thing the player walks toward without being told to.
            var ribs = new GameObject("The Ribcage").transform;
            ribs.SetParent(root, false);

            var ribMesh = ZoneMeshes.BuildRib(9.5f, 5.5f, 0.55f);
            var boneMaterial = CreateMaterial(new Color(0.42f, 0.41f, 0.37f), "Bone");

            for (var i = 0; i < 6; i++)
            {
                var z = -16f + i * 6.4f;
                var lean = i % 2 == 0 ? 1f : -1f;

                var rib = new GameObject("Rib");
                rib.transform.SetParent(ribs, false);
                rib.transform.position = new Vector3(lean * 6.5f, Height(lean * 6.5f, z, recipe) - 0.4f, z);
                rib.transform.rotation = Quaternion.Euler(0f, lean > 0f ? 0f : 180f, lean > 0f ? -8f : 8f);
                rib.AddComponent<MeshFilter>().sharedMesh = ribMesh;
                rib.AddComponent<MeshRenderer>().sharedMaterial = boneMaterial;
            }

            // THE MARKER, at the centre of the arch: the reason to walk into it.
            var markerPos = new Vector3(1.5f, 0f, -1.5f);
            markerPos.y = Height(markerPos.x, markerPos.z, recipe);
            CreateMarker(
                root, stone, interactions,
                ContentIds.MarkerRibStone,
                "interactable.rib_stone",
                markerPos);

            // THE DISCOVERY, deliberately off to one side so it is found by looking around rather
            // than by walking the critical path.
            var tagPos = new Vector3(-13f, 0f, 9f);
            tagPos.y = Height(tagPos.x, tagPos.z, recipe) + 0.45f;
            CreatePickup(
                root, interactions,
                ContentIds.DiscoveryBrassTag,
                "interactable.brass_tag",
                tagPos,
                new Color(0.68f, 0.56f, 0.24f));

            // THE EXIT, at the far edge, framed by two standing stones so it reads as a way through.
            var gatePos = new Vector3(0f, 0f, 30f);
            gatePos.y = Height(gatePos.x, gatePos.z, recipe);
            CreateGate(
                root, stone, interactions,
                ContentIds.GateRibcageToFernmaw,
                "interactable.gully_mouth",
                ContentIds.ZoneFernmaw,
                gatePos);
        }

        // --- Fernmaw ---------------------------------------------------------------------------

        private static void BuildFernmawContent(
            Transform root, Recipe recipe, Material stone, InteractionSystem interactions)
        {
            // THE LANDMARK: the cut channel wall — a run of dressed blocks too regular to be
            // natural, which is the entire story beat of this zone.
            var wall = new GameObject("Aqueduct Wall").transform;
            wall.SetParent(root, false);

            var blockMaterial = CreateMaterial(new Color(0.24f, 0.26f, 0.22f), "DressedStone");
            for (var i = 0; i < 14; i++)
            {
                var z = -18f + i * 2.9f;
                for (var course = 0; course < 3; course++)
                {
                    var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    block.name = "Block";
                    Object.Destroy(block.GetComponent<Collider>());
                    block.transform.SetParent(wall, false);
                    block.transform.position = new Vector3(
                        7.5f + course * 0.35f,
                        Height(7.5f, z, recipe) + 0.6f + course * 1.15f,
                        z);
                    block.transform.localScale = new Vector3(1.6f, 1.1f, 2.7f);
                    block.GetComponent<MeshRenderer>().sharedMaterial = blockMaterial;
                }
            }

            var cutPos = new Vector3(5.6f, 0f, 2f);
            cutPos.y = Height(cutPos.x, cutPos.z, recipe);
            CreateMarker(
                root, stone, interactions,
                ContentIds.MarkerAqueductCut,
                "interactable.aqueduct_cut",
                cutPos);

            var reelPos = new Vector3(-8f, 0f, -11f);
            reelPos.y = Height(reelPos.x, reelPos.z, recipe) + 0.4f;
            CreatePickup(
                root, interactions,
                ContentIds.DiscoveryWaterloggedReel,
                "interactable.waterlogged_reel",
                reelPos,
                new Color(0.30f, 0.26f, 0.22f));

            var backPos = new Vector3(0f, 0f, -30f);
            backPos.y = Height(backPos.x, backPos.z, recipe);
            CreateGate(
                root, stone, interactions,
                ContentIds.GateFernmawToRibcage,
                "interactable.channel_mouth",
                ContentIds.ZoneRibcage,
                backPos);
        }

        // --- Interactable construction ----------------------------------------------------------

        private static void CreateMarker(
            Transform root, Material stone, InteractionSystem interactions,
            string contentId, string nameKey, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Marker " + contentId;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = position + new Vector3(0f, 1.1f, 0f);
            go.transform.localScale = new Vector3(0.7f, 2.2f, 0.45f);
            go.transform.rotation = Quaternion.Euler(0f, 18f, 3f);
            go.GetComponent<MeshRenderer>().sharedMaterial = stone;

            var marker = go.AddComponent<AncientMarker>();
            marker.Configure(contentId, nameKey, "narration." + contentId);

            if (interactions != null)
            {
                interactions.Register(marker);
            }
        }

        private static void CreatePickup(
            Transform root, InteractionSystem interactions,
            string contentId, string nameKey, Vector3 position, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Discovery " + contentId;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.34f, 0.24f, 0.06f);
            go.transform.rotation = Quaternion.Euler(72f, 24f, 0f);

            // Emissive-ish flat colour: the one warm thing in a cold zone, so the eye finds it.
            go.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial(tint, "Discovery");

            var pickup = go.AddComponent<DiscoveryPickup>();
            pickup.Configure(contentId, nameKey, "narration." + contentId);

            if (interactions != null)
            {
                interactions.Register(pickup);
            }
        }

        private static void CreateGate(
            Transform root, Material stone, InteractionSystem interactions,
            string contentId, string nameKey, string destinationZoneId, Vector3 position)
        {
            var parent = new GameObject("Gate " + contentId).transform;
            parent.SetParent(root, false);
            parent.position = position;

            // Two standing stones flanking a gap. A doorway shape is what tells the player this is
            // a way through rather than more scenery.
            for (var side = -1; side <= 1; side += 2)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "Pillar";
                Object.Destroy(pillar.GetComponent<Collider>());
                pillar.transform.SetParent(parent, false);
                pillar.transform.localPosition = new Vector3(side * 1.9f, 1.9f, 0f);
                pillar.transform.localScale = new Vector3(0.75f, 3.8f, 0.75f);
                pillar.transform.localRotation = Quaternion.Euler(0f, side * 6f, side * -2f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = stone;
            }

            var gate = parent.gameObject.AddComponent<ZoneGate>();
            gate.Configure(contentId, nameKey, destinationZoneId, "narration." + contentId);

            if (interactions != null)
            {
                interactions.Register(gate);
            }
        }

        // --- Helpers ----------------------------------------------------------------------------

        private static float Height(float x, float z, Recipe recipe)
        {
            return ZoneMeshes.SampleHeight(x, z, recipe.Amplitude, recipe.Seed, SpawnApron);
        }

        /// <summary>
        /// A flat-shaded opaque material on whatever pipeline the project is actually using.
        /// </summary>
        /// <remarks>
        /// ⚠ VERIFY: shader availability differs between the built-in pipeline and URP, and this
        /// project currently ships neither a URP asset nor a configured pipeline. The fallback
        /// chain tries URP's lit shader, then the built-in Standard, then an unlit last resort, so
        /// the zone renders as *something* under any of them rather than showing magenta.
        /// </remarks>
        /// <summary>Cached lit shader for the active render pipeline. Resolved once, reported once.</summary>
        private static Shader _zoneShader;
        private static bool _zoneShaderResolved;

        /// <summary>
        /// The lit shader this project can actually render with.
        /// </summary>
        /// <remarks>
        /// THE PREVIOUS VERSION OF THIS USED <c>??</c>, AND THAT IS A BUG, not a style opinion.
        /// <c>Shader</c> is a <c>UnityEngine.Object</c>, and Unity overloads <c>==</c> so a destroyed
        /// or unloadable object compares equal to null while still being a live C# reference. The
        /// null-coalescing operator does NOT use that overload — it tests the raw reference — so a
        /// chain of <c>Shader.Find(a) ?? Shader.Find(b)</c> can hand back an object that every other
        /// line of code agrees is null. A material built on it renders nothing at all, with no error,
        /// which looks exactly like an empty world.
        /// <para>
        /// The pipeline is asked rather than guessed, too. This project has no URP package and no
        /// pipeline asset (CONFLICT-7), so it runs on Built-in; asking for a URP shader first was
        /// searching for something that cannot exist here, and a URP shader under Built-in renders
        /// black in any case.
        /// </para>
        /// </remarks>
        private static Shader ZoneShader
        {
            get
            {
                if (_zoneShaderResolved)
                {
                    return _zoneShader;
                }

                _zoneShaderResolved = true;

                var scriptable = GraphicsSettings.currentRenderPipeline != null;
                _zoneShader = scriptable
                    ? FindShader("Universal Render Pipeline/Lit", "Standard", "Unlit/Color")
                    : FindShader("Standard", "Legacy Shaders/Diffuse", "Unlit/Color");

                Debug.Log(
                    "[Vardholm] world: render pipeline is " +
                    (scriptable ? GraphicsSettings.currentRenderPipeline.name : "Built-in") +
                    " · colour space " + QualitySettings.activeColorSpace +
                    " · zone shader '" + (_zoneShader != null ? _zoneShader.name : "NONE — the world will be invisible") + "'");

                return _zoneShader;
            }
        }

        /// <summary>First of <paramref name="names"/> that resolves, using Unity's null semantics.</summary>
        private static Shader FindShader(params string[] names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                var shader = Shader.Find(names[i]);

                // `== null` and not `is null`: this is the comparison that knows about Unity's
                // lifetime, and it is the whole reason this helper exists instead of a `??` chain.
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static Material CreateMaterial(Color color, string name)
        {
            var material = new Material(ZoneShader) { name = name };

            // Property names differ across those shaders. Setting whichever exists avoids a hard
            // dependency on any one pipeline being installed.
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.08f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.08f);
            }

            return material;
        }
    }
}
