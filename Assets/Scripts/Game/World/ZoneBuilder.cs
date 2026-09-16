using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Radio;
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
        /// <summary>Radius kept level around the spawn so the player never starts inside a hill.</summary>
        private const float SpawnApron = 7f;

        /// <summary>
        /// How far the sea reaches.
        /// </summary>
        /// <remarks>
        /// Sized to the camera's far plane, not to the horizon. Water drawn past 260 m would be
        /// clipped away and leave a visible arc where the sea simply stops; at 240 m the fog has
        /// taken it to about five per cent visibility (exp(-(0.0072 · 240)²) = 0.05), so the edge
        /// is close to gone before the far plane could cut it. The water shader keeps the disc
        /// centred on the camera, so this radius is always the distance to the rim -- from the
        /// shore as much as from the middle of the island. Reaching further would mean pushing the far plane out, and the
        /// near/far ratio is exactly what caused the depth-buffer flicker that took a day to find.
        /// </remarks>
        private const float SeaRadius = 240f;

        /// <summary>
        /// Per-zone look and content.
        /// </summary>
        /// <remarks>
        /// A class with named fields rather than the positional struct this used to be. The recipe
        /// grew from twelve values to twenty-four when the island got a sky, a sea and a shoreline,
        /// and a twenty-four-argument constructor call is a row of unlabelled colours where one
        /// transposed pair is a bug nobody can see by reading.
        /// </remarks>
        private sealed class Recipe
        {
            public int Seed;
            public float Amplitude;

            // Ground
            public Color Ground;
            public Color Rock;
            public Color Sand;
            public Color WetSand;
            public Color Stone;

            // Sky
            public Color SkyZenith;
            public Color SkyHorizon;
            public Color SkyGround;
            public Color SunDisc;
            public float CloudCover;

            // Sea
            public Color WaterDeep;
            public Color WaterShallow;

            // Light
            public Color Fog;
            public float FogDensity;
            public Color Sun;
            public float SunIntensity;
            public Vector3 SunAngles;
            public Color AmbientSky;
            public Color AmbientEquator;
            public Color AmbientGround;

            // Content
            public int RockCount;
            public int FloraCount;
            public Color FoliageBase;
            public Color FoliageTip;
            public float Broadleaf;
        }

        // The Ribcage: an open shore under a high overcast, seen across water. Sparse, wide and
        // cold-lit, so the rib arch reads from a distance and the player has an obvious direction
        // to walk.
        //
        // THE VALUES ARE PHYSICAL, NOT ARTISTIC PREFERENCE, and the first set was wrong by a factor
        // of five. In Linear colour space a material colour is converted from sRGB before shading,
        // so an albedo of 0.13 is 0.014 linear -- near coal. With a sun at 24° elevation
        // (N·L = 0.41) and intensity 0.85 the shore reached the screen at luminance 0.077: seven
        // per cent grey, which is black on any display. The geometry was rendering correctly the
        // whole time and being shaded to nothing.
        //
        // The ground colour is now a TINT applied over the mesh's vertex colours, which sit around
        // 0.9, so the product is what ZoneDiagnostics.EstimateGroundLuminance reports every entry.
        private static readonly Recipe RibcageRecipe = new Recipe
        {
            Seed = 20260914,
            Amplitude = 4.2f,

            Ground = new Color(0.46f, 0.47f, 0.42f),
            Rock = new Color(0.44f, 0.43f, 0.40f),
            Sand = new Color(0.66f, 0.61f, 0.50f),
            WetSand = new Color(0.36f, 0.33f, 0.28f),
            Stone = new Color(0.52f, 0.52f, 0.49f),

            SkyZenith = new Color(0.34f, 0.46f, 0.60f),
            SkyHorizon = new Color(0.76f, 0.79f, 0.80f),
            SkyGround = new Color(0.66f, 0.70f, 0.72f),
            SunDisc = new Color(1.00f, 0.97f, 0.90f),
            CloudCover = 0.52f,

            WaterDeep = new Color(0.05f, 0.12f, 0.17f),
            WaterShallow = new Color(0.17f, 0.32f, 0.35f),

            // Thinner than it was. Fog dense enough to hide a missing horizon is not needed once
            // there is a horizon, and the island only reads as an island if its far shore is
            // visible from the near one.
            Fog = new Color(0.70f, 0.74f, 0.76f),
            FogDensity = 0.0072f,
            Sun = new Color(0.96f, 0.93f, 0.86f),
            SunIntensity = 1.25f,
            SunAngles = new Vector3(38f, 35f, 0f),
            AmbientSky = new Color(0.52f, 0.60f, 0.70f),
            AmbientEquator = new Color(0.48f, 0.50f, 0.50f),
            AmbientGround = new Color(0.26f, 0.25f, 0.22f),

            RockCount = 44,
            FloraCount = 150,
            FoliageBase = new Color(0.16f, 0.19f, 0.12f),
            FoliageTip = new Color(0.44f, 0.46f, 0.28f),
            Broadleaf = 0f
        };

        // Fernmaw: a sunken green channel. Higher relief and much denser air to make it feel narrow
        // and enclosed, which is the whole point of the contrast with the shore.
        //
        // Deliberately darker than the Ribcage because the contrast between open shore and sunken
        // channel is the zone's entire character. Darker than the shore, not darker than visible.
        private static readonly Recipe FernmawRecipe = new Recipe
        {
            Seed = 71104,
            Amplitude = 7.4f,

            Ground = new Color(0.30f, 0.37f, 0.27f),
            Rock = new Color(0.31f, 0.33f, 0.28f),
            Sand = new Color(0.44f, 0.43f, 0.34f),
            WetSand = new Color(0.22f, 0.24f, 0.19f),
            Stone = new Color(0.38f, 0.42f, 0.34f),

            SkyZenith = new Color(0.30f, 0.40f, 0.36f),
            SkyHorizon = new Color(0.58f, 0.66f, 0.54f),
            SkyGround = new Color(0.40f, 0.48f, 0.38f),
            SunDisc = new Color(0.92f, 0.96f, 0.80f),
            CloudCover = 0.72f,

            WaterDeep = new Color(0.04f, 0.10f, 0.08f),
            WaterShallow = new Color(0.14f, 0.26f, 0.19f),

            Fog = new Color(0.44f, 0.52f, 0.42f),
            FogDensity = 0.021f,
            Sun = new Color(0.82f, 0.92f, 0.74f),
            SunIntensity = 1.05f,
            SunAngles = new Vector3(55f, 200f, 0f),
            AmbientSky = new Color(0.46f, 0.58f, 0.46f),
            AmbientEquator = new Color(0.40f, 0.48f, 0.38f),
            AmbientGround = new Color(0.18f, 0.22f, 0.16f),

            RockCount = 30,
            FloraCount = 260,
            FoliageBase = new Color(0.08f, 0.16f, 0.09f),
            FoliageTip = new Color(0.26f, 0.40f, 0.20f),
            Broadleaf = 1f
        };

        /// <summary>
        /// Builds a zone's content under <paramref name="root"/> and registers its interactables.
        /// </summary>
        /// <param name="zoneKey">Scene key naming which zone to build.</param>
        /// <param name="root">Parent for everything created.</param>
        /// <param name="interactions">System the zone's interactables register with. Null tolerated.</param>
        /// <param name="radio">The radio the Ribcage's set reports into. Null yields no set.</param>
        /// <returns>The world position the player should spawn at.</returns>
        public static Vector3 Build(string zoneKey, Transform root, InteractionSystem interactions, RadioService radio = null)
        {
            var fernmaw = zoneKey == ContentIds.ZoneFernmaw;
            var recipe = fernmaw ? FernmawRecipe : RibcageRecipe;

            var groundMaterial = CreateTerrainMaterial(recipe);
            var stoneMaterial = CreatePropMaterial(recipe.Stone, "ZoneStone");

            BuildGround(root, recipe, groundMaterial);
            ApplyAtmosphere(root, recipe);
            BuildSea(root, recipe);
            ScatterRocks(root, recipe);
            ScatterFlora(root, recipe);

            if (fernmaw)
            {
                BuildFernmawContent(root, recipe, stoneMaterial, interactions);
            }
            else
            {
                BuildRibcageContent(root, recipe, stoneMaterial, interactions);
                BuildTrawlerHull(root, recipe, stoneMaterial, interactions, radio);
                BuildHullLine(root, recipe, interactions);
            }

            return new Vector3(0f, Height(0f, 0f, recipe) + 1.2f, 0f);
        }

        // --- Shared construction -------------------------------------------------------------

        private static void BuildGround(Transform root, Recipe recipe, Material material)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);

            var mesh = ZoneMeshes.BuildGround(recipe.Amplitude, recipe.Seed, SpawnApron);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Dress(go.AddComponent<MeshRenderer>(), material);

            // A MeshCollider on an 18k-triangle mesh, and the same mesh the eye sees rather than a
            // coarse stand-in. A separate collision mesh is the usual advice and is wrong here: any
            // simplification puts the surface the player walks on somewhere other than the surface
            // they can see, and on a 1.8 m grid that gap is visible. There is exactly one of these
            // per zone and it never moves, so Unity bakes it once. Rocks and flora get no colliders
            // at all -- walking through a fern is a better failure than 200 colliders.
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

            // Trilight rather than Flat. Flat ambient lights the underside of every rock exactly
            // as brightly as its top, which is the single most reliable way to make a lit scene
            // look like an untextured mock-up: real outdoor ambient comes mostly from the sky, so
            // upward faces get sky colour, downward faces get bounced ground colour, and the
            // difference between them is most of what reads as "outdoors".
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = recipe.AmbientSky;
            RenderSettings.ambientEquatorColor = recipe.AmbientEquator;
            RenderSettings.ambientGroundColor = recipe.AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            // The sky itself. Without one the camera cleared to the fog colour and the world had no
            // horizon at all -- and a world with no horizon is a diorama, whatever is standing in
            // it. Set before UpdateEnvironment so the environment rebuild sees it.
            RenderSettings.skybox = CreateSkyMaterial(recipe);

            // Ambient set at runtime does not reach the shaders until the environment is rebuilt.
            // Without this the zone is lit by whatever the scene asset was saved with, which for a
            // scene created empty by the editor script is nothing at all.
            DynamicGI.UpdateEnvironment();

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

            // Shadow bias, set rather than left at the default. A 170 m ground mesh shaded by one
            // directional light is the case the defaults are worst at: too little bias and the
            // terrain shadows itself in moving bands, which is the other half of the flicker.
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.12f;
            sun.shadowNearPlane = 0.2f;

            // Cascades spread over the default 150 m put almost no resolution where the player
            // actually is. At this fog density the world is at ~31% visibility by 150 m and a
            // shadow's contrast is well below what reads at that distance, so shadows past 80 m
            // are not worth their cascade -- and a
            // coarse cascade near the camera is what makes shadow edges crawl as you walk.
            QualitySettings.shadowDistance = 80f;
            QualitySettings.shadowCascades = 2;

            // The sun disc in the sky has to be in the same place as the light, or the scene is lit
            // from one direction and the glare comes from another. The shader takes a direction
            // pointing AT the sun, which is the reverse of the direction the light shines in.
            var skybox = RenderSettings.skybox;
            if (skybox != null && skybox.HasProperty("_SunDirection"))
            {
                var toSun = -sunGo.transform.forward;
                skybox.SetVector("_SunDirection", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            }
        }

        /// <summary>
        /// Lays the sea around the island and sinks it to the waterline.
        /// </summary>
        /// <remarks>
        /// One mesh, one material, no collider — the shore is walkable ground that happens to go
        /// under water, and a swimming system is not a thing this game has or wants. A player who
        /// wades out finds the ground keeps going down; the water does not stop them, the slope
        /// does, and the character controller already refuses gradients that steep.
        /// </remarks>
        private static void BuildSea(Transform root, Recipe recipe)
        {
            var go = new GameObject("Sea");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, ZoneMeshes.SeaLevel, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = ZoneMeshes.BuildWater(SeaRadius, 64, 48);

            var renderer = go.AddComponent<MeshRenderer>();
            Dress(renderer, CreateWaterMaterial(recipe));

            // Water neither casts nor receives shadows. A shadow falling on a transparent sheet is
            // wrong twice over: the sheet is not a surface light stops at, and the receive pass on
            // a 900 m plane is the most expensive shadow in the zone for no visible return.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Strews rock over the island, clustered rather than sprinkled.
        /// </summary>
        /// <remarks>
        /// Even scatter is the tell of a generator. Stone in the real world arrives in groups —
        /// what fell off one cliff, what one glacier dropped — so most rocks here are placed as a
        /// small companion to another rock, and only every third one starts a new cluster. The
        /// difference costs nothing and is the first thing that stops a landscape looking sown.
        /// </remarks>
        private static void ScatterRocks(Transform root, Recipe recipe)
        {
            var parent = new GameObject("Rocks").transform;
            parent.SetParent(root, false);

            var random = new System.Random(recipe.Seed + 17);

            // Two materials so a boulder and the shingle around it are not the same stone, and six
            // meshes rather than four now that there are more rocks than meshes by a wide margin.
            var boulder = CreatePropMaterial(recipe.Stone, "ZoneStone");
            var shingle = CreatePropMaterial(recipe.Rock, "ZoneShingle");

            var meshes = new Mesh[6];
            for (var i = 0; i < meshes.Length; i++)
            {
                meshes[i] = ZoneMeshes.BuildRock(recipe.Seed + i * 31);
            }

            var clusterX = 0f;
            var clusterZ = 0f;
            var inCluster = 0;

            for (var i = 0; i < recipe.RockCount; i++)
            {
                float x, z;
                if (inCluster <= 0)
                {
                    var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    var radius = 11f + (float)random.NextDouble() * (ZoneMeshes.GroundSize * 0.34f);
                    clusterX = Mathf.Cos(angle) * radius;
                    clusterZ = Mathf.Sin(angle) * radius;
                    inCluster = 1 + (int)(random.NextDouble() * 3.0);
                    x = clusterX;
                    z = clusterZ;
                }
                else
                {
                    x = clusterX + (float)(random.NextDouble() - 0.5) * 9f;
                    z = clusterZ + (float)(random.NextDouble() - 0.5) * 9f;
                }

                inCluster--;

                var ground = Height(x, z, recipe);

                // Nothing is placed on the seabed. A boulder standing under the water with its top
                // poking through is a thing the player will walk to and find is not there.
                if (ground < ZoneMeshes.SeaLevel + 0.3f)
                {
                    continue;
                }

                var scale = 0.7f + (float)random.NextDouble() * 2.8f;

                var go = new GameObject("Rock");
                go.transform.SetParent(parent, false);

                // Sunk by a third rather than a quarter: a rock resting exactly on the surface
                // reads as placed on the ground, and a rock bedded into it reads as part of it.
                go.transform.position = new Vector3(x, ground - scale * 0.34f, z);
                go.transform.rotation = Quaternion.Euler(
                    (float)random.NextDouble() * 24f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 24f);

                // Non-uniform scale. Identical proportions at six different sizes still reads as
                // six copies of one rock; squashing each one differently does not.
                go.transform.localScale = new Vector3(
                    scale * (0.8f + (float)random.NextDouble() * 0.5f),
                    scale * (0.6f + (float)random.NextDouble() * 0.6f),
                    scale * (0.8f + (float)random.NextDouble() * 0.5f));

                go.AddComponent<MeshFilter>().sharedMesh = meshes[i % meshes.Length];
                Dress(go.AddComponent<MeshRenderer>(), scale > 1.8f ? boulder : shingle);
            }
        }

        /// <summary>
        /// Plants the zone's vegetation, thick where it would be thick and absent where it would be.
        /// </summary>
        /// <remarks>
        /// Density is driven by the ground rather than uniform: nothing grows below the waterline,
        /// little grows on the exposed high ground, and the belt between them is where it gathers.
        /// The shader does the rest — the quads are cut into blades and moved by the wind there, so
        /// what this method places is position, scale and colour, not shape.
        /// </remarks>
        private static void ScatterFlora(Transform root, Recipe recipe)
        {
            var parent = new GameObject("Flora").transform;
            parent.SetParent(root, false);

            var random = new System.Random(recipe.Seed + 91);
            var material = CreateFoliageMaterial(recipe);

            // Quads, not meshes: flora is silhouette and density, and crossed quads read as
            // vegetation at a fraction of the cost of any actual plant model.
            for (var i = 0; i < recipe.FloraCount; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = 6f + (float)random.NextDouble() * (ZoneMeshes.GroundSize * 0.36f);
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                var ground = Height(x, z, recipe);

                // The shoreline. Plants stop at the tideline, and the bare band of sand that leaves
                // is what makes the water look like it belongs to the land it touches.
                if (ground < ZoneMeshes.SeaLevel + 0.9f)
                {
                    continue;
                }

                // Thinning with altitude: the exposed top of the island is wind-scoured, and a
                // ridge as green as the sheltered ground below it looks painted on.
                var exposure = Mathf.InverseLerp(recipe.Amplitude * 1.2f, recipe.Amplitude * 3.0f, ground);
                if (random.NextDouble() < exposure * 0.75)
                {
                    continue;
                }

                var height = recipe.Broadleaf > 0.5f
                    ? 1.1f + (float)random.NextDouble() * 2.6f
                    : 0.45f + (float)random.NextDouble() * 1.15f;

                var plant = new GameObject("Flora");
                plant.transform.SetParent(parent, false);

                // Set a little into the ground so no plant floats on a slope: the quad's pivot is
                // its base, and a base exactly on the surface hangs in the air on any gradient.
                plant.transform.position = new Vector3(x, ground - 0.12f, z);
                plant.transform.rotation = Quaternion.Euler(
                    (float)(random.NextDouble() - 0.5) * 10f,
                    (float)random.NextDouble() * 360f,
                    (float)(random.NextDouble() - 0.5) * 10f);

                // Three quads rather than two. The third breaks the X that two crossed quads draw
                // when the player looks straight down at them, which is the angle a first-person
                // camera spends most of its time at with something growing at its feet.
                for (var blade = 0; blade < 3; blade++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Blade";

                    // The primitive's collider would make a forest of triggers the player bumps
                    // into. Vegetation is scenery; it is not solid.
                    StripCollider(quad);

                    quad.transform.SetParent(plant.transform, false);
                    quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                    quad.transform.localRotation = Quaternion.Euler(0f, blade * 60f, 0f);
                    quad.transform.localScale = new Vector3(height * 0.7f, height, 1f);

                    var quadRenderer = quad.GetComponent<MeshRenderer>();
                    Dress(quadRenderer, material);

                    // Grass does not cast a shadow worth the draw call, and 150 plants × 3 quads
                    // in the shadow pass is the most expensive nothing in the zone.
                    quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
                // AHEAD OF THE SPAWN, not around it. The ribs used to span z = -16 to +16 with the
                // player appearing at the origin, which put them INSIDE the arch: the nearest rib
                // hung directly over the camera and filled a third of the screen with a grey slab,
                // and the landmark that is supposed to be seen from a distance could not be seen at
                // all. Starting at +6 reads as a whole arch ahead, which is what the comment
                // above has always described, and leaves a walk through it toward the gate at +30.
                var z = 6f + i * 6.4f;
                var lean = i % 2 == 0 ? 1f : -1f;

                var rib = new GameObject("Rib");
                rib.transform.SetParent(ribs, false);
                rib.transform.position = new Vector3(lean * 6.5f, Height(lean * 6.5f, z, recipe) - 0.4f, z);
                // Yaw 180 for the +X row, not for the -X one. BuildRib arcs toward +X from its
                // origin, so a rib at x = +6.5 with no yaw arcs AWAY from the centre: both rows
                // curved outward and the "arch" opened outward like a flower. Six ribs that lean
                // apart are not a ribcage. Yawing the +X row turns it to face the centre, which is
                // what the comment above has always claimed this builds.
                rib.transform.rotation = Quaternion.Euler(0f, lean > 0f ? 180f : 0f, lean > 0f ? 8f : -8f);
                rib.AddComponent<MeshFilter>().sharedMesh = ribMesh;
                Dress(rib.AddComponent<MeshRenderer>(), boneMaterial);
            }

            // THE MARKER. IT WAS BEHIND THE PLAYER, and that is the whole reason the stone was
            // never seen. The rig spawns at the origin facing +Z with no yaw, and the marker sat at
            // z = -1.5 — 2.45 m away at 128° from the camera's forward vector, which is to say
            // squarely behind the head. Proximity does not care about facing, so INSPECT appeared
            // the instant the run began while the stone itself was never once on screen. A comment
            // reading "the reason to walk into it" described something the player could not walk
            // toward because they were never shown it.
            //
            // Now placed ahead and slightly right, far enough to be approached rather than
            // auto-prompted, and inside the arch's z range so it reads against the ribs.
            var markerPos = new Vector3(2.5f, 0f, 7.5f);
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

            // THE TOOL. A spindle off the net drum, lying where the drum stands — which is the
            // point: it is not hidden, it is where a person working the drum would have left it.
            // The player will not know what it is for until they find tape that will not play.
            var spindlePos = new Vector3(-6.5f, 0f, 3f);
            spindlePos.y = Height(spindlePos.x, spindlePos.z, recipe) + 0.22f;
            CreateItem(
                root, interactions,
                ItemIds.DrySpindle,
                "item.dry_spindle",
                spindlePos,
                new Color(0.46f, 0.38f, 0.26f),
                new Vector3(0.16f, 0.16f, 0.52f));

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



        // --- The six hulls ---------------------------------------------------------------------

        /// <summary>
        /// Six wrecked hulls along the Ribcage, on one straight line, and the sightline that finds it.
        /// </summary>
        /// <remarks>
        /// <c>design/04-first-30-minutes.md</c> §2:40. Half-buried, at different angles, different
        /// eras -- the composition reads as chaos until the player stands at the bow of the nearest
        /// and looks down the beach, and then it does not. Wrecks do not queue up. Somebody parked
        /// them. The hulls are shells of slabs at six angles; the line they share is exact.
        /// <para>
        /// The stand point is a few metres off the first hull's bow along the axis, and the axis
        /// runs through every hull's centre, so a player who lines up the near bow with the far
        /// ones is looking down it. Tolerance is the design's ±4°.
        /// </para>
        /// </remarks>
        private static void BuildHullLine(Transform root, Recipe recipe, InteractionSystem interactions)
        {
            var group = new GameObject("The Six Hulls").transform;
            group.SetParent(root, false);

            // The near hull is the NEAREST hull to the spawn -- the design says "the bow of the
            // nearest hull", and a player who follows that has to arrive at the stand point. The
            // line runs away from the spawn along the shore, so the far end vanishes into the fog.
            var start = new Vector3(-9f, 0f, -14f);
            var axis = new Vector3(-1f, 0f, -0.14f).normalized;
            const float Spacing = 13.5f;
            const int Count = 6;

            var rust = CreatePropMaterial(new Color(0.36f, 0.24f, 0.18f), "WreckRust");
            var timber = CreatePropMaterial(new Color(0.30f, 0.25f, 0.18f), "WreckTimber");
            var glass = CreatePropMaterial(new Color(0.58f, 0.58f, 0.52f), "WreckFibreglass");

            var random = new System.Random(recipe.Seed + 613);
            var centres = new Vector3[Count];
            var lengths = new float[Count];
            for (var i = 0; i < Count; i++)
            {
                var centre = start + axis * (Spacing * i);
                centre.y = Mathf.Max(Height(centre.x, centre.z, recipe), ZoneMeshes.SeaLevel + 0.2f);
                centres[i] = centre;

                var hull = new GameObject("Hull " + (i + 1)).transform;
                hull.SetParent(group, false);
                hull.position = centre;

                // Each hull leans its own way: heel, yaw off the line, and a different build. The
                // near hull is the exception: its bow points back along the line toward the stand
                // point, so "stand at the bow and look down the beach" is literally what happens.
                var yaw = (float)(random.NextDouble() - 0.5) * 70f;
                var heel = (float)(random.NextDouble() - 0.5) * 40f;
                var length = 7f + (float)random.NextDouble() * 5f;
                var beam = 2.2f + (float)random.NextDouble() * 1.2f;
                if (i == 0)
                {
                    // Local +X (the stem) along -axis: the yaw that turns (1,0,0) onto -axis.
                    yaw = SightlineMath.YawOf(-axis.x, -axis.z) - 90f;
                    heel = 8f;
                    length = 9f;
                }

                hull.rotation = Quaternion.Euler(0f, yaw, heel);
                lengths[i] = length;

                var material = i % 3 == 0 ? rust : i % 3 == 1 ? timber : glass;

                // A keel slab, two flank slabs leaning in, and a stem: enough to read as a boat
                // on its side from twenty metres, which is the distance this is seen from.
                Slab(hull, material, "Keel", new Vector3(0f, 0.3f, 0f), new Vector3(length, 0.5f, beam * 0.6f), Vector3.zero, solid: true);
                Slab(hull, material, "Flank port", new Vector3(0f, 1.2f, beam * 0.45f), new Vector3(length * 0.9f, 2.2f, 0.18f), new Vector3(-22f, 0f, 0f), solid: true);
                Slab(hull, material, "Flank starboard", new Vector3(0f, 1.0f, -beam * 0.45f), new Vector3(length * 0.8f, 1.8f, 0.18f), new Vector3(24f, 0f, 0f), solid: true);
                Slab(hull, material, "Stem", new Vector3(length * 0.5f, 1.3f, 0f), new Vector3(0.3f, 2.6f, beam * 0.5f), new Vector3(0f, 0f, -12f));
            }

            // THE SIGHTLINE. Stand past the near hull's bow -- half its length plus a stride
            // beyond its centre, clear of the solid keel -- and look along the axis.
            var standAt = start - axis * (9f * 0.5f + 3f);
            standAt.y = Height(standAt.x, standAt.z, recipe);

            var sightline = new GameObject("Sightline " + ContentIds.MarkerHullLine);
            sightline.transform.SetParent(group, false);
            sightline.transform.position = standAt;

            // The chalk line: a thought, drawn as a thin white stroke through all six, hovering at
            // eye height so it reads against the hulls rather than the sand.
            var chalkMaterial = CreateMaterial(new Color(0.96f, 0.96f, 0.92f), "Chalk");
            AddChalk(sightline.transform, start - axis * 2f, start + axis * (Spacing * (Count - 1) + 6f), standAt.y + 1.5f, chalkMaterial);

            var observer = sightline.AddComponent<Sightline>();
            observer.Configure(
                ContentIds.MarkerHullLine,
                "interactable.hull_line",
                standAt,
                axis,
                standRadius: 4.5f,
                toleranceDegrees: 4f);

            if (interactions != null)
            {
                interactions.Register(observer);
            }

            // THE WRONG HULLS. Stand at the bow of the second, third or fourth and look the same
            // way: a chalk snaps in through that hull and the two beyond it -- three, not six --
            // and Nadia says so. The design's reward for the attempt (§2:40 FAILURE). The last two
            // hulls have too little beyond them to make a line of three, and the near hull is the
            // right one. Stand radii do not overlap: the stances are 13.5 m apart and 4.5 m wide.
            for (var i = 1; i + 2 < Count; i++)
            {
                var wrongStand = centres[i] - axis * (lengths[i] * 0.5f + 3f);
                wrongStand.y = Height(wrongStand.x, wrongStand.z, recipe);

                var partial = new GameObject("Sightline " + ContentIds.RemarkHullLinePartial + " " + (i + 1));
                partial.transform.SetParent(group, false);
                partial.transform.position = wrongStand;

                AddChalk(partial.transform, centres[i] - axis * 2f, centres[i + 2] + axis * 2f, wrongStand.y + 1.5f, chalkMaterial);

                var hint = partial.AddComponent<Sightline>();
                hint.ConfigureAsHint(
                    ContentIds.RemarkHullLinePartial,
                    "interactable.hull_line_partial",
                    ContentIds.MarkerHullLine,
                    wrongStand,
                    axis,
                    standRadius: 4.5f,
                    toleranceDegrees: 4f);

                if (interactions != null)
                {
                    interactions.Register(hint);
                }
            }
        }

        /// <summary>A chalk stroke between two points at one height, disabled until a sightline enables it.</summary>
        private static void AddChalk(Transform parent, Vector3 from, Vector3 to, float y, Material material)
        {
            var chalk = new GameObject("Chalk");
            chalk.transform.SetParent(parent, false);
            var line = chalk.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            from.y = y;
            to.y = y;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.enabled = false;
        }

        // --- The trawler hull ------------------------------------------------------------------

        /// <summary>
        /// The only sheltered structure on the route: a hole cut in a trawler's flank, a swept
        /// floor, a crate for a table, and the radio on it.
        /// </summary>
        /// <remarks>
        /// From <c>design/04-first-30-minutes.md</c> §15:00. The hull is a shell of flat slabs
        /// rather than a modelled boat because the brief is a place to stand in and a doorway to
        /// look through, and that is what a shell provides. The cut door faces the spawn so the
        /// player sees a dark opening in a wall of rust before they see anything inside it.
        /// <para>
        /// Beside the set, the nail row: the dead torch hangs there, which is where a person keeps
        /// a torch. It is the answer to two of the radio's three faults and the player finds that
        /// out by taking it apart.
        /// </para>
        /// </remarks>
        private static void BuildTrawlerHull(
            Transform root, Recipe recipe, Material stone, InteractionSystem interactions, RadioService radio)
        {
            var hull = new GameObject("Trawler Hull").transform;
            hull.SetParent(root, false);

            // Off the critical path to the gate, far enough that it is found by looking. On the
            // rise toward the ridge so the doorway reads against the sky from the shore.
            var at = new Vector3(15f, 0f, 17f);
            at.y = Height(at.x, at.z, recipe);
            hull.position = at;
            hull.rotation = Quaternion.Euler(0f, -28f, 0f);

            var rust = CreatePropMaterial(new Color(0.34f, 0.22f, 0.16f), "HullRust");
            var deck = CreatePropMaterial(new Color(0.26f, 0.24f, 0.21f), "HullDeck");

            // Three walls and a roof, the fourth side open: a section of flank three metres
            // across, leaning the way a beached hull leans. The floor is the island's own sand,
            // swept — the ground mesh is the floor.
            Slab(hull, rust, "Flank", new Vector3(0f, 1.7f, 1.6f), new Vector3(4.2f, 3.4f, 0.22f), new Vector3(-8f, 0f, 0f), solid: true);
            Slab(hull, rust, "Bulkhead port", new Vector3(-2.1f, 1.7f, 0f), new Vector3(0.22f, 3.4f, 3.2f), Vector3.zero, solid: true);
            Slab(hull, rust, "Bulkhead starboard", new Vector3(2.1f, 1.7f, 0f), new Vector3(0.22f, 3.4f, 3.2f), Vector3.zero, solid: true);
            Slab(hull, deck, "Deck over", new Vector3(0f, 3.35f, 0.1f), new Vector3(4.4f, 0.24f, 3.6f), new Vector3(-6f, 0f, 0f), solid: true);

            // The cut: the open side is the door, and two short lips either side of it are the
            // edges of the hole — cut, not torn, the slag beads still sharp.
            Slab(hull, rust, "Cut edge left", new Vector3(-1.6f, 1.7f, -1.55f), new Vector3(1.0f, 3.4f, 0.2f), Vector3.zero, solid: true);
            Slab(hull, rust, "Cut edge right", new Vector3(1.6f, 1.7f, -1.55f), new Vector3(1.0f, 3.4f, 0.2f), Vector3.zero, solid: true);

            // The crate, upside down, used as a table. Against the bulkhead.
            var crate = Slab(hull, CreatePropMaterial(new Color(0.16f, 0.24f, 0.30f), "Crate"), "Crate",
                new Vector3(0.9f, 0.32f, 0.9f), new Vector3(0.7f, 0.64f, 0.5f), Vector3.zero, solid: true);

            // The set. A cream body, a black panel, a perspex window, a handle: four boxes, and
            // enough that a player who has seen a 1970s marine set recognises one.
            var setRoot = new GameObject("Radio " + ContentIds.RadioSet);
            setRoot.transform.SetParent(hull, false);
            setRoot.transform.localPosition = crate.localPosition + new Vector3(0f, 0.32f + 0.14f, 0f);
            setRoot.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);

            var cream = CreatePropMaterial(new Color(0.78f, 0.74f, 0.62f), "RadioCream");
            var panel = CreatePropMaterial(new Color(0.09f, 0.09f, 0.09f), "RadioPanel");
            Slab(setRoot.transform, cream, "Body", new Vector3(0f, 0f, 0f), new Vector3(0.46f, 0.28f, 0.30f), Vector3.zero);
            Slab(setRoot.transform, panel, "Face", new Vector3(0f, 0.02f, -0.16f), new Vector3(0.40f, 0.20f, 0.02f), Vector3.zero);
            Slab(setRoot.transform, CreateMaterial(new Color(0.80f, 0.60f, 0.22f), "DialWindow"), "Dial",
                new Vector3(0f, 0.05f, -0.175f), new Vector3(0.26f, 0.06f, 0.01f), Vector3.zero);
            Slab(setRoot.transform, panel, "Handle", new Vector3(0f, 0.19f, 0f), new Vector3(0.30f, 0.03f, 0.03f), Vector3.zero);

            var set = setRoot.AddComponent<RadioSet>();
            set.Configure(radio, "interactable.radio_set");
            if (interactions != null && radio != null)
            {
                interactions.Register(set);
            }

            // The nail row: eleven tags on eleven nails, hung like keys, and the torch among them.
            var tagBrass = CreateMaterial(new Color(0.68f, 0.56f, 0.24f), "Tag");
            for (var i = 0; i < 11; i++)
            {
                Slab(hull, tagBrass, "Tag " + (i + 1),
                    new Vector3(-1.95f, 1.2f + (i % 2) * 0.12f, -1.1f + i * 0.2f),
                    new Vector3(0.02f, 0.06f, 0.04f), Vector3.zero);
            }

            // Not once it has been taken apart. The pickup is rebuilt with the zone like everything
            // else, and a torch that was opened for its cells does not grow back on the nail.
            if (radio != null && radio.Repair.TorchTakenApart)
            {
                return;
            }

            var torchAt = hull.TransformPoint(new Vector3(-1.9f, 1.55f, 0.6f));
            CreateItem(
                root, interactions,
                ItemIds.DeadTorch,
                "item.dead_torch",
                torchAt,
                new Color(0.12f, 0.12f, 0.13f),
                new Vector3(0.06f, 0.22f, 0.06f));
        }

        /// <summary>
        /// Takes a primitive's collider out of the physics scene now, not at the end of the frame.
        /// </summary>
        /// <remarks>
        /// <c>Object.Destroy</c> is deferred, and everything in Furnish -- the ground probe, the
        /// bounds sweep, the first CharacterController.Move -- runs in the frame the primitive was
        /// created, with the doomed collider still live. A continue saved inside a mechanism's
        /// footprint was being lifted onto its roof by a probe that hit a collider that would not
        /// exist a frame later. Disabling first is what makes "no collider" true when it is read.
        /// </remarks>
        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            collider.enabled = false;
            Object.Destroy(collider);
        }

        /// <summary>One flat box, parented, positioned and dressed. The hull is made of these.</summary>
        /// <param name="solid">Keep the collider: a wall the player must not walk through.</param>
        private static Transform Slab(
            Transform parent, Material material, string name, Vector3 localPosition, Vector3 scale, Vector3 tilt,
            bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!solid)
            {
                StripCollider(go);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(tilt);
            go.transform.localScale = scale;
            Dress(go.GetComponent<MeshRenderer>(), material);
            return go.transform;
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
                    StripCollider(block);
                    block.transform.SetParent(wall, false);
                    block.transform.position = new Vector3(
                        7.5f + course * 0.35f,
                        Height(7.5f, z, recipe) + 0.6f + course * 1.15f,
                        z);
                    block.transform.localScale = new Vector3(1.6f, 1.1f, 2.7f);
                    Dress(block.GetComponent<MeshRenderer>(), blockMaterial);
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

            // THE KEY, still in its bracket beside the housing. Left, not hidden: whoever serviced
            // this expected to come back.
            var keyPos = new Vector3(6.2f, 0f, -6.5f);
            keyPos.y = Height(keyPos.x, keyPos.z, recipe) + 0.2f;
            CreateItem(
                root, interactions,
                ItemIds.SluiceKey,
                "item.sluice_key",
                keyPos,
                new Color(0.52f, 0.50f, 0.46f),
                new Vector3(0.1f, 0.34f, 0.1f));

            // THE SEIZED SLUICE. The channel is dry because this is shut, and it is shut because
            // nobody has turned it in a long time. Opening it is the act the zone is built around.
            var sluicePos = new Vector3(6.4f, 0f, 0f);
            sluicePos.y = Height(sluicePos.x, sluicePos.z, recipe);
            CreateMechanism(
                root, stone, interactions,
                ContentIds.MechanismSluice,
                "interactable.channel_sluice",
                ItemIds.SluiceKey,
                sluicePos,
                new Color(0.44f, 0.30f, 0.20f));

            // THE DECK, inside the housing, still wired to the island's mains. It will not read a
            // reel that has been in water, which is the puzzle the spindle on the shore answers.
            var deckPos = new Vector3(4.4f, 0f, -2.6f);
            deckPos.y = Height(deckPos.x, deckPos.z, recipe);
            CreateMechanism(
                root, stone, interactions,
                ContentIds.MechanismTapeDeck,
                "interactable.tape_deck",
                ItemIds.ReboundReel,
                deckPos,
                new Color(0.22f, 0.24f, 0.26f));

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
            StripCollider(go);
            go.transform.SetParent(root, false);
            go.transform.position = position + new Vector3(0f, 1.1f, 0f);
            go.transform.localScale = new Vector3(0.7f, 2.2f, 0.45f);
            go.transform.rotation = Quaternion.Euler(0f, 18f, 3f);
            Dress(go.GetComponent<MeshRenderer>(), stone);

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
            StripCollider(go);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.34f, 0.24f, 0.06f);
            go.transform.rotation = Quaternion.Euler(72f, 24f, 0f);

            // Emissive-ish flat colour: the one warm thing in a cold zone, so the eye finds it.
            Dress(go.GetComponent<MeshRenderer>(), CreateMaterial(tint, "Discovery"));

            var pickup = go.AddComponent<DiscoveryPickup>();
            pickup.Configure(contentId, nameKey, "narration." + contentId);

            if (interactions != null)
            {
                interactions.Register(pickup);
            }
        }

        /// <summary>A carryable object lying where a person would have set it down.</summary>
        private static void CreateItem(
            Transform root, InteractionSystem interactions,
            string itemId, string nameKey, Vector3 position, Color tint, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Item " + itemId;
            StripCollider(go);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.transform.rotation = Quaternion.Euler(0f, itemId.Length * 23f % 360f, 6f);
            Dress(go.GetComponent<MeshRenderer>(), CreateMaterial(tint, "Item"));

            var pickup = go.AddComponent<ItemPickup>();
            pickup.Configure(itemId, nameKey);

            if (interactions != null)
            {
                interactions.Register(pickup);
            }
        }

        /// <summary>
        /// A built thing that does not work, with one moving part that swings clear when it does.
        /// </summary>
        /// <remarks>
        /// The moving part is a child named "Moving" rather than a serialized reference, because
        /// this is built at runtime and has no Inspector. <c>Mechanism</c> looks it up by that name;
        /// the convention is the contract, and it is written down in both places.
        /// </remarks>
        private static void CreateMechanism(
            Transform root, Material stone, InteractionSystem interactions,
            string contentId, string nameKey, string requiredItemId, Vector3 position, Color tint)
        {
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = "Mechanism " + contentId;
            StripCollider(housing);
            housing.transform.SetParent(root, false);
            housing.transform.position = position + new Vector3(0f, 0.9f, 0f);
            housing.transform.localScale = new Vector3(1.3f, 1.8f, 0.9f);
            Dress(housing.GetComponent<MeshRenderer>(), stone);

            var moving = GameObject.CreatePrimitive(PrimitiveType.Cube);
            moving.name = "Moving";
            StripCollider(moving);
            moving.transform.SetParent(housing.transform, false);
            moving.transform.localPosition = new Vector3(0.62f, 0.15f, 0f);
            moving.transform.localScale = new Vector3(0.9f, 0.16f, 0.22f);
            Dress(moving.GetComponent<MeshRenderer>(), CreateMaterial(tint, "MechanismPart"));

            var mechanism = housing.AddComponent<Mechanism>();
            // Examining a mechanism empty-handed is an InspectCommand, and the handler narrates
            // "narration." + contentId; that row is the idle line, by convention, not by parameter.
            mechanism.Configure(
                contentId,
                nameKey,
                "narration." + contentId + ".solved",
                requiredItemId,
                consumesItem: false);

            if (interactions != null)
            {
                interactions.Register(mechanism);
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
                StripCollider(pillar);
                pillar.transform.SetParent(parent, false);
                pillar.transform.localPosition = new Vector3(side * 1.9f, 1.9f, 0f);
                pillar.transform.localScale = new Vector3(0.75f, 3.8f, 0.75f);
                pillar.transform.localRotation = Quaternion.Euler(0f, side * 6f, side * -2f);
                Dress(pillar.GetComponent<MeshRenderer>(), stone);
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

        /// <summary>
        /// Assigns a material and detaches the renderer from everything this project never bakes.
        /// </summary>
        /// <remarks>
        /// A renderer created at runtime, in a scene created at runtime, has no baked lighting data
        /// of any kind — no lightmaps, no light probes, no reflection probes. Unity's defaults
        /// assume it does: <c>lightProbeUsage</c> is <c>BlendProbes</c> and
        /// <c>reflectionProbeUsage</c> is <c>BlendProbes</c> out of the box, so every object here
        /// was asking for probe data that does not exist and can never exist for this world.
        /// <para>
        /// Setting both to <c>Off</c> makes the shading depend on exactly two things this code does
        /// control — the ambient colour and the directional light — instead of on a baking step the
        /// project does not have and, for a procedurally generated world, cannot have.
        /// </para>
        /// </remarks>
        private static void Dress(MeshRenderer renderer, Material material)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Loads one of this project's own shaders, or null if it is not in the build.
        /// </summary>
        /// <remarks>
        /// Resources.Load first and Shader.Find second, and the order is the whole point.
        /// Shader.Find resolves anything in the project while running in the editor and resolves
        /// only what the build actually included once the game is on a phone — so a shader found
        /// this way in the editor can be missing at runtime, which is the classic "it worked in
        /// Play mode" failure. Everything here lives under Assets/Resources/Shaders, which is a
        /// guarantee of inclusion rather than a hope.
        /// <para>
        /// Null is a supported answer. Every caller falls back to the stock lit shader, so a
        /// missing custom shader costs the island its looks and not its playability.
        /// </para>
        /// </remarks>
        /// <param name="resourcePath">Path under Resources, without the extension.</param>
        /// <param name="shaderName">The shader's declared name, for the editor-side lookup.</param>
        /// <returns>The shader, or null.</returns>
        private static Shader LoadShader(string resourcePath, string shaderName)
        {
            var shader = Resources.Load<Shader>(resourcePath);

            // `== null` and not `is null`, for the same reason documented on FindShader: Unity
            // overloads the operator and a destroyed or unloadable asset is only null through it.
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find(shaderName);
            return shader != null ? shader : null;
        }

        /// <summary>The ground material: vertex colours, slope rock and a shoreline.</summary>
        private static Material CreateTerrainMaterial(Recipe recipe)
        {
            var shader = LoadShader("Shaders/VardholmTerrain", "Vardholm/Terrain");
            if (shader == null)
            {
                // The stock shader ignores vertex colours, so the fallback is flat — but visible,
                // and at the right brightness, which is the property that actually matters.
                return CreateMaterial(recipe.Ground, "ZoneGround");
            }

            var material = new Material(shader) { name = "ZoneGround" };
            material.SetColor("_Color", recipe.Ground);
            material.SetColor("_RockColor", recipe.Rock);
            material.SetColor("_SandColor", recipe.Sand);
            material.SetColor("_WetColor", recipe.WetSand);
            material.SetFloat("_SeaLevel", ZoneMeshes.SeaLevel);
            return material;
        }

        /// <summary>A solid prop material: one colour, mottled by where the object stands.</summary>
        private static Material CreatePropMaterial(Color color, string name)
        {
            var shader = LoadShader("Shaders/VardholmProp", "Vardholm/Prop");
            if (shader == null)
            {
                return CreateMaterial(color, name);
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);

            // The crevice colour is derived rather than authored. Every caller would otherwise
            // have to pass a second colour that is always the first one darkened, and the pair
            // would drift apart the first time somebody edited only one of them.
            material.SetColor("_DarkColor", new Color(color.r * 0.42f, color.g * 0.44f, color.b * 0.44f, 1f));
            return material;
        }

        /// <summary>The vegetation material: cut-out blades that move in the wind.</summary>
        private static Material CreateFoliageMaterial(Recipe recipe)
        {
            var shader = LoadShader("Shaders/VardholmFoliage", "Vardholm/Foliage");
            if (shader == null)
            {
                return CreateMaterial(recipe.FoliageBase, "ZoneFlora");
            }

            var material = new Material(shader) { name = "ZoneFlora" };
            material.SetColor("_BaseColor", recipe.FoliageBase);
            material.SetColor("_TipColor", recipe.FoliageTip);
            material.SetFloat("_Broadleaf", recipe.Broadleaf);

            // Ferns are broad and few; grass is narrow and many. One number, two plants.
            material.SetFloat("_Blades", recipe.Broadleaf > 0.5f ? 2f : 4f);
            material.SetFloat("_Width", recipe.Broadleaf > 0.5f ? 0.62f : 0.34f);
            return material;
        }

        /// <summary>The sea.</summary>
        private static Material CreateWaterMaterial(Recipe recipe)
        {
            var shader = LoadShader("Shaders/VardholmWater", "Vardholm/Water");
            if (shader == null)
            {
                // Opaque and flat, but still a sheet of water-coloured something at the right
                // height, which is what makes the island read as an island.
                return CreateMaterial(recipe.WaterDeep, "Sea");
            }

            var material = new Material(shader) { name = "Sea" };
            material.SetColor("_DeepColor", recipe.WaterDeep);
            material.SetColor("_ShallowColor", recipe.WaterShallow);

            // The water reflects the sky it is under, so the reflection colour comes from the sky
            // recipe rather than being a colour of its own that somebody has to keep in step.
            material.SetColor("_SkyColor", recipe.SkyHorizon);
            return material;
        }

        /// <summary>The sky: gradient, cloud deck and a sun that agrees with the light.</summary>
        private static Material CreateSkyMaterial(Recipe recipe)
        {
            var shader = LoadShader("Shaders/VardholmSky", "Vardholm/Sky");
            if (shader == null)
            {
                // No sky rather than a wrong one. RenderSettings.skybox = null leaves the camera
                // clearing to the fog colour, which is exactly what this zone did before and is a
                // defensible flat horizon rather than a magenta dome.
                return null;
            }

            var material = new Material(shader) { name = "ZoneSky" };
            material.SetColor("_ZenithColor", recipe.SkyZenith);
            material.SetColor("_HorizonColor", recipe.SkyHorizon);
            material.SetColor("_GroundColor", recipe.SkyGround);
            material.SetColor("_SunColor", recipe.SunDisc);
            material.SetColor("_CloudColor", recipe.SkyHorizon);
            material.SetFloat("_CloudCover", recipe.CloudCover);
            return material;
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
