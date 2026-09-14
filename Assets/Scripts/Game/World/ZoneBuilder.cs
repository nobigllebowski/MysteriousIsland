using ForgottenIsle.Core.Items;
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

        // The Ribcage: open dark-sand shore under a low grey sky. Sparse, wide, cold-lit, so the
        // rib arch reads from a distance and the player has an obvious direction to walk.
        //
        // THE VALUES ARE PHYSICAL, NOT ARTISTIC PREFERENCE, and the first set was wrong by a factor
        // of five. In Linear colour space a material colour is converted from sRGB before shading,
        // so an albedo of 0.13 is 0.014 linear -- near coal. With a sun at 24° elevation
        // (N·L = 0.41) and intensity 0.85 the shore reached the screen at luminance 0.077: seven
        // per cent grey, which is black on any display. The geometry was rendering correctly the
        // whole time and being shaded to nothing.
        //
        // These land the ground at ~0.41 and the stone at ~0.48 screen luminance — still cold,
        // desaturated and bleak, but readable. Changing any of them changes that number; the
        // arithmetic is in ZoneDiagnostics.EstimateGroundLuminance, which reports it every entry.
        private static readonly Recipe RibcageRecipe = new Recipe(
            seed: 20260914,
            amplitude: 3.2f,
            ground: new Color(0.40f, 0.43f, 0.41f),
            stone: new Color(0.50f, 0.50f, 0.47f),
            fog: new Color(0.44f, 0.49f, 0.52f),
            fogDensity: 0.012f,
            sun: new Color(0.82f, 0.86f, 0.90f),
            sunIntensity: 1.30f,
            sunAngles: new Vector3(38f, 35f, 0f),
            ambient: new Color(0.55f, 0.60f, 0.64f),
            rockCount: 26,
            floraCount: 10);

        // Fernmaw: a sunken green channel. Higher relief and much denser fog to make it feel narrow
        // and enclosed, which is the whole point of the contrast with the shore.
        //
        // Deliberately darker than the Ribcage — ground ~0.31 against the shore's ~0.41 — because
        // the contrast between open shore and sunken channel is the zone's entire character. Darker
        // than the shore, not darker than visible.
        private static readonly Recipe FernmawRecipe = new Recipe(
            seed: 71104,
            amplitude: 6.4f,
            ground: new Color(0.26f, 0.34f, 0.27f),
            stone: new Color(0.36f, 0.41f, 0.33f),
            fog: new Color(0.20f, 0.28f, 0.22f),
            fogDensity: 0.045f,
            sun: new Color(0.72f, 0.86f, 0.68f),
            sunIntensity: 1.05f,
            sunAngles: new Vector3(55f, 200f, 0f),
            ambient: new Color(0.54f, 0.64f, 0.56f),
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
            Dress(go.AddComponent<MeshRenderer>(), material);

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
            RenderSettings.ambientIntensity = 1f;

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

            // Shadow bias, set rather than left at the default. A 120 m ground mesh shaded by one
            // directional light is the case the defaults are worst at: too little bias and the
            // terrain shadows itself in moving bands, which is the other half of the flicker.
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.12f;
            sun.shadowNearPlane = 0.2f;

            // Cascades spread over the default 150 m put almost no resolution where the player
            // actually is. The fog has already taken the world to 3% visibility by 150 m, so
            // shadows past 80 m are invisible anyway and the cascade split is pure waste — and a
            // coarse cascade near the camera is what makes shadow edges crawl as you walk.
            QualitySettings.shadowDistance = 80f;
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
                Dress(go.AddComponent<MeshRenderer>(), material);
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
                    Dress(quad.GetComponent<MeshRenderer>(), material);
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
            Object.Destroy(go.GetComponent<Collider>());
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
            Object.Destroy(go.GetComponent<Collider>());
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
            Object.Destroy(go.GetComponent<Collider>());
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
            Object.Destroy(housing.GetComponent<Collider>());
            housing.transform.SetParent(root, false);
            housing.transform.position = position + new Vector3(0f, 0.9f, 0f);
            housing.transform.localScale = new Vector3(1.3f, 1.8f, 0.9f);
            Dress(housing.GetComponent<MeshRenderer>(), stone);

            var moving = GameObject.CreatePrimitive(PrimitiveType.Cube);
            moving.name = "Moving";
            Object.Destroy(moving.GetComponent<Collider>());
            moving.transform.SetParent(housing.transform, false);
            moving.transform.localPosition = new Vector3(0.62f, 0.15f, 0f);
            moving.transform.localScale = new Vector3(0.9f, 0.16f, 0.22f);
            Dress(moving.GetComponent<MeshRenderer>(), CreateMaterial(tint, "MechanismPart"));

            var mechanism = housing.AddComponent<Mechanism>();
            mechanism.Configure(
                contentId,
                nameKey,
                "narration." + contentId + ".idle",
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
                Object.Destroy(pillar.GetComponent<Collider>());
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
