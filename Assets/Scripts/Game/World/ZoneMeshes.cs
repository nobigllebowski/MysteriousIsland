using UnityEngine;

namespace ForgottenIsle.Game.World
{
    /// <summary>
    /// Procedural meshes for the greybox island: undulating ground, rock shards, rib arches.
    /// </summary>
    /// <remarks>
    /// The whole zone is built from three generators rather than imported art, for one reason:
    /// the project must run from a clone with no asset downloads and no manual import step. Cubes
    /// and spheres would satisfy that too — but a flat plane with primitives on it reads as a test
    /// scene, and the brief asks for a place.
    /// <para>
    /// Everything here is deterministic given a seed, so the same zone rebuilds identically every
    /// time it is entered. That matters more than it sounds: zones are furnished from scratch on
    /// every visit, and a player who walks back to the shore must find the same shore.
    /// </para>
    /// <para>
    /// Mesh budgets are deliberately small — a 32×32 ground grid is ~2k triangles, a rock is 1
    /// sphere-derived hull. Mobile overdraw is controlled by keeping geometry opaque and low, not
    /// by drawing fewer, larger things.
    /// </para>
    /// </remarks>
    public static class ZoneMeshes
    {
        /// <summary>Vertices per side of the ground grid. 97 gives 96 quads and ~18k triangles.</summary>
        /// <remarks>
        /// Raised from 33. At 33 the quads were five metres across, which is wider than the player
        /// is tall: every hillside was a visible staircase of flat facets, and no shader can hide a
        /// silhouette that coarse. 97 puts a vertex every 1.8 m, which is the scale at which ground
        /// stops reading as a tessellation and starts reading as a slope.
        /// </remarks>
        private const int GroundResolution = 97;

        /// <summary>
        /// Metres across. The island, its coastline and the sea around it are all sized from this.
        /// </summary>
        /// <remarks>
        /// Public and owned here because the coastline is computed from it: a second copy of this
        /// number in the zone builder would be a second definition of where the island ends.
        /// </remarks>
        public const float GroundSize = 170f;

        /// <summary>Y of the waterline. The spawn apron sits this far above it.</summary>
        public const float SeaLevel = -1.2f;

        /// <summary>How far the seabed drops below the waterline at the outer edge of the mesh.</summary>
        private const float SeaDepth = 8f;

        /// <summary>Fraction of the half-extent where the land begins falling toward the sea.</summary>
        private const float CoastStart = 0.56f;

        /// <summary>Fraction of the half-extent where the land is fully submerged.</summary>
        private const float CoastEnd = 0.93f;

        // The high ground, deliberately off centre. A hill centred on the spawn would put the
        // player on the summit looking down at everything, which is the one viewpoint from which an
        // island has no silhouette at all. Pushed to one quarter, the player arrives on the low
        // shore with high ground visible across the zone -- somewhere to walk towards.
        private const float RidgeX = -0.21f;
        private const float RidgeZ = 0.29f;
        private const float RidgeRadius = 0.44f;
        private const float RidgeHeight = 2.6f;

        /// <summary>
        /// Builds an island: rolling land inside a coastline, falling away to a seabed outside it.
        /// </summary>
        /// <remarks>
        /// IT WAS NOT AN ISLAND BEFORE. The mesh was a square of noise that ended at a hard edge
        /// with nothing past it, and no amount of shading fixes that -- the player was standing on
        /// a tile. The shape is now three things multiplied together: noise for the surface, a
        /// ridge for the skyline, and a noisy radial falloff for the coast, so the outline has bays
        /// and headlands rather than being a circle.
        /// <para>
        /// The apron still matters and still works the same way: the player spawns at the origin,
        /// and terrain noise under the spawn point is how a character controller ends up embedded
        /// in a hill on the first frame.
        /// </para>
        /// </remarks>
        /// <param name="amplitude">Peak height of the surface undulation.</param>
        /// <param name="seed">Chooses the noise offsets; the same seed gives the same island.</param>
        /// <param name="flatRadius">Radius around the origin kept level for the spawn.</param>
        /// <returns>A new mesh. The caller owns it.</returns>
        public static Mesh BuildGround(float amplitude, int seed, float flatRadius)
        {
            var mesh = new Mesh { name = "ZoneGround" };

            // 16-bit indices cap at 65535 vertices; 97x97 = 9409, so the default format is still
            // fine and we avoid the memory cost of a 32-bit index buffer on mobile.
            var vertexCount = GroundResolution * GroundResolution;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];

            float offsetA, offsetB, offsetC;
            Offsets(seed, out offsetA, out offsetB, out offsetC);

            var half = GroundSize * 0.5f;
            var step = GroundSize / (GroundResolution - 1);

            for (var z = 0; z < GroundResolution; z++)
            {
                for (var x = 0; x < GroundResolution; x++)
                {
                    var index = z * GroundResolution + x;
                    var worldX = -half + x * step;
                    var worldZ = -half + z * step;
                    var height = HeightAt(worldX, worldZ, amplitude, offsetA, offsetB, offsetC, flatRadius);

                    vertices[index] = new Vector3(worldX, height, worldZ);
                    uvs[index] = new Vector2(x / (float)(GroundResolution - 1), z / (float)(GroundResolution - 1));

                    // Vertex colour carries the wet-to-dry gradient, and the terrain shader
                    // multiplies the zone's ground colour through it. It is a MULTIPLIER, so it
                    // sits around 1 rather than around the colour it used to be: the old values
                    // were absolute dark greens, and multiplying those by a dark ground colour is
                    // how this world rendered black for most of a week.
                    var band = Mathf.InverseLerp(SeaLevel, amplitude * 2.4f, height);
                    var mottle = Mathf.PerlinNoise(offsetC + worldX * 0.14f, offsetC + worldZ * 0.14f);

                    var damp = new Color(0.62f, 0.66f, 0.63f);
                    var mid = new Color(0.90f, 0.92f, 0.86f);
                    var dry = new Color(1.00f, 0.97f, 0.88f);

                    var shade = band < 0.5f
                        ? Color.Lerp(damp, mid, band * 2f)
                        : Color.Lerp(mid, dry, (band - 0.5f) * 2f);

                    var tint = Mathf.Lerp(0.88f, 1.06f, mottle);
                    colors[index] = new Color(
                        Mathf.Clamp01(shade.r * tint),
                        Mathf.Clamp01(shade.g * tint),
                        Mathf.Clamp01(shade.b * tint),
                        1f);
                }
            }

            var triangles = new int[(GroundResolution - 1) * (GroundResolution - 1) * 6];
            var t = 0;
            for (var z = 0; z < GroundResolution - 1; z++)
            {
                for (var x = 0; x < GroundResolution - 1; x++)
                {
                    var bottomLeft = z * GroundResolution + x;
                    var topLeft = bottomLeft + GroundResolution;

                    triangles[t++] = bottomLeft;
                    triangles[t++] = topLeft;
                    triangles[t++] = bottomLeft + 1;

                    triangles[t++] = bottomLeft + 1;
                    triangles[t++] = topLeft;
                    triangles[t++] = topLeft + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Samples the same height function the ground mesh used.
        /// </summary>
        /// <remarks>
        /// Callers place rocks and markers on the surface without raycasting, which would need
        /// physics to have ticked at least once — and zone furnishing runs in the frame the scene
        /// finished loading, before that is true.
        /// <para>
        /// It calls the identical private function the mesh builder calls. That is the point: the
        /// previous version was a hand-copied duplicate of the height expression, which is a pair
        /// of formulas that have to be edited together forever and will not be.
        /// </para>
        /// </remarks>
        /// <param name="x">World X.</param>
        /// <param name="z">World Z.</param>
        /// <param name="amplitude">Same amplitude passed to <see cref="BuildGround"/>.</param>
        /// <param name="seed">Same seed passed to <see cref="BuildGround"/>.</param>
        /// <param name="flatRadius">Same flat radius passed to <see cref="BuildGround"/>.</param>
        /// <returns>Ground height at that point.</returns>
        public static float SampleHeight(float x, float z, float amplitude, int seed, float flatRadius)
        {
            float offsetA, offsetB, offsetC;
            Offsets(seed, out offsetA, out offsetB, out offsetC);
            return HeightAt(x, z, amplitude, offsetA, offsetB, offsetC, flatRadius);
        }

        /// <summary>Three deterministic noise offsets for a seed.</summary>
        private static void Offsets(int seed, out float a, out float b, out float c)
        {
            var random = new System.Random(seed);
            a = (float)random.NextDouble() * 100f;
            b = (float)random.NextDouble() * 100f;
            c = (float)random.NextDouble() * 100f;
        }

        /// <summary>The island's height field. The single definition of this world's shape.</summary>
        private static float HeightAt(
            float x, float z, float amplitude, float offsetA, float offsetB, float offsetC, float flatRadius)
        {
            // Three octaves: the broad shape of the land, the hummocks on it, and the roughness
            // that keeps a slope from being a plane. Each is a third of the frequency below it.
            var land =
                Mathf.PerlinNoise(offsetA + x * 0.021f, offsetA + z * 0.021f) * amplitude +
                Mathf.PerlinNoise(offsetB + x * 0.072f, offsetB + z * 0.072f) * amplitude * 0.40f +
                Mathf.PerlinNoise(offsetB + x * 0.185f, offsetB + z * 0.185f) * amplitude * 0.15f;

            var ridgeX = x - RidgeX * GroundSize;
            var ridgeZ = z - RidgeZ * GroundSize;
            var ridgeDistance = Mathf.Sqrt(ridgeX * ridgeX + ridgeZ * ridgeZ) / (GroundSize * RidgeRadius);
            land += amplitude * RidgeHeight * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - ridgeDistance));

            var distance = Mathf.Sqrt(x * x + z * z);
            if (distance < flatRadius)
            {
                // Everything fades together, so the apron is continuous with the land around it
                // rather than a disc punched out of it.
                land *= Mathf.SmoothStep(0f, 1f, distance / flatRadius);
            }

            // The coastline. Perturbing the radius rather than the height is what turns a circular
            // island into one with bays and headlands, and it does it without breaking the
            // guarantee that everything past CoastEnd is under water.
            var half = GroundSize * 0.5f;
            var wobble = (Mathf.PerlinNoise(offsetC + x * 0.0105f, offsetC + z * 0.0105f) - 0.5f) * 2f;
            var shaped = distance + wobble * half * 0.16f;
            var island = 1f - Mathf.SmoothStep(half * CoastStart, half * CoastEnd, shaped);

            return land * island + (SeaLevel - SeaDepth) * (1f - island);
        }

        /// <summary>
        /// A radial sheet for the sea: dense near the player, coarse toward the horizon.
        /// </summary>
        /// <remarks>
        /// A uniform grid is the wrong mesh for water. Sized to reach the horizon it has quads
        /// wider than the waves, so the swell under the player's feet is lost; sized to resolve the
        /// swell it stops a hundred metres out and the player can see the edge of the sea.
        /// <para>
        /// So the rings are even out to a little past the coastline — a couple of metres apart,
        /// which comfortably resolves an 11 m swell — and then sprint to the horizon, where all
        /// that is needed is colour. Normals and tangents are written flat and uniform because the shader displaces
        /// the surface itself and derives its own normal from that displacement — the mesh is only
        /// the sampling grid, and the shader's tangent-space assumption is documented there.
        /// </para>
        /// </remarks>
        /// <param name="radius">How far the sheet reaches.</param>
        /// <param name="rings">Concentric divisions. 64 is ample: 44 of them fall on the near water.</param>
        /// <param name="segments">Divisions around. 48 keeps the outer ring from reading polygonal.</param>
        /// <returns>A new mesh. The caller owns it.</returns>
        public static Mesh BuildWater(float radius, int rings, int segments)
        {
            var mesh = new Mesh { name = "Sea" };

            var vertexCount = 1 + rings * segments;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            var uvs = new Vector2[vertexCount];

            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            tangents[0] = new Vector4(1f, 0f, 0f, -1f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            // Even spacing out to here, then a sprint to the horizon. NOT a curve that packs
            // vertices around the origin, which was the first version and was exactly wrong: the
            // origin is the middle of the island, which is dry land. The water the player actually
            // stands next to is at the coast, 50-85 m out, so that is where the resolution goes.
            var nearRadius = Mathf.Min(GroundSize * 0.65f, radius);
            const float NearShare = 0.7f;

            for (var ring = 1; ring <= rings; ring++)
            {
                // `ringT` and not `t`: the triangle cursor below is named `t` in this same method,
                // and C# refuses the pair outright (CS0136) even though they never overlap.
                var ringT = ring / (float)rings;
                var ringRadius = ringT <= NearShare
                    ? nearRadius * (ringT / NearShare)
                    : Mathf.Lerp(nearRadius, radius, Mathf.Pow((ringT - NearShare) / (1f - NearShare), 2f));

                for (var segment = 0; segment < segments; segment++)
                {
                    var index = 1 + (ring - 1) * segments + segment;
                    var angle = segment / (float)segments * Mathf.PI * 2f;
                    var vx = Mathf.Cos(angle) * ringRadius;
                    var vz = Mathf.Sin(angle) * ringRadius;

                    vertices[index] = new Vector3(vx, 0f, vz);
                    normals[index] = Vector3.up;
                    tangents[index] = new Vector4(1f, 0f, 0f, -1f);
                    uvs[index] = new Vector2(vx / radius * 0.5f + 0.5f, vz / radius * 0.5f + 0.5f);
                }
            }

            var triangles = new int[segments * 3 + (rings - 1) * segments * 6];
            var t = 0;

            // `fanSegment` and not `segment`: this loop sits in the method's own scope while the
            // ring loops declare a `segment` inside theirs, and C# refuses that pair outright
            // (CS0136) even though the two can never be in scope at the same time.
            for (var fanSegment = 0; fanSegment < segments; fanSegment++)
            {
                triangles[t++] = 0;
                triangles[t++] = 1 + (fanSegment + 1) % segments;
                triangles[t++] = 1 + fanSegment;
            }

            for (var ring = 1; ring < rings; ring++)
            {
                var inner = 1 + (ring - 1) * segments;
                var outer = 1 + ring * segments;

                for (var segment = 0; segment < segments; segment++)
                {
                    var next = (segment + 1) % segments;

                    triangles[t++] = inner + segment;
                    triangles[t++] = outer + next;
                    triangles[t++] = outer + segment;

                    triangles[t++] = inner + segment;
                    triangles[t++] = inner + next;
                    triangles[t++] = outer + next;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// An angular rock: a low-poly hull with vertices pushed out irregularly.
        /// </summary>
        /// <param name="seed">Chooses the distortion; the same seed gives the same rock.</param>
        /// <returns>A new mesh. The caller owns it.</returns>
        public static Mesh BuildRock(int seed)
        {
            // An octahedron subdivided once: 18 vertices, 32 faces. Cheap, and its flat shading
            // reads as stone far better than a smooth sphere does.
            var mesh = new Mesh { name = "Rock" };
            var random = new System.Random(seed);

            var basePoints = new[]
            {
                Vector3.up, Vector3.down,
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back,
                new Vector3(0.7f, 0.7f, 0f).normalized,
                new Vector3(-0.7f, 0.7f, 0f).normalized,
                new Vector3(0f, 0.7f, 0.7f).normalized,
                new Vector3(0f, 0.7f, -0.7f).normalized,
                new Vector3(0.7f, -0.4f, 0.6f).normalized,
                new Vector3(-0.7f, -0.4f, 0.6f).normalized,
                new Vector3(0.7f, -0.4f, -0.6f).normalized,
                new Vector3(-0.7f, -0.4f, -0.6f).normalized
            };

            var vertices = new Vector3[basePoints.Length];
            for (var i = 0; i < basePoints.Length; i++)
            {
                var jitter = 0.72f + (float)random.NextDouble() * 0.55f;
                vertices[i] = basePoints[i] * jitter;

                // Squash vertically so rocks sit like boulders rather than floating spheres.
                vertices[i].y *= 0.78f;
            }

            mesh.vertices = vertices;
            mesh.triangles = ConvexHullTriangles(vertices);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// One rib of the great arch: a tapered arc sweeping up and over.
        /// </summary>
        /// <remarks>
        /// This is the zone's landmark, so it is the one mesh worth more than a primitive. Built as
        /// a swept quad strip along a quarter-ellipse, tapering toward the tip — the silhouette a
        /// player reads as bone rather than as a bent pipe.
        /// </remarks>
        /// <param name="height">How far up the rib reaches.</param>
        /// <param name="reach">How far across it leans.</param>
        /// <param name="thickness">Half-width at the base.</param>
        /// <returns>A new mesh. The caller owns it.</returns>
        public static Mesh BuildRib(float height, float reach, float thickness)
        {
            const int Segments = 14;
            var mesh = new Mesh { name = "Rib" };

            var vertices = new Vector3[(Segments + 1) * 4];
            var triangles = new int[Segments * 24];
            var v = 0;
            var t = 0;

            for (var i = 0; i <= Segments; i++)
            {
                var u = i / (float)Segments;
                var angle = u * Mathf.PI * 0.5f;

                var centre = new Vector3(Mathf.Sin(angle) * reach, Mathf.Sin(angle * 0.92f) * height, 0f);
                var taper = Mathf.Lerp(1f, 0.28f, u) * thickness;

                vertices[v + 0] = centre + new Vector3(0f, taper, taper);
                vertices[v + 1] = centre + new Vector3(0f, taper, -taper);
                vertices[v + 2] = centre + new Vector3(0f, -taper, -taper);
                vertices[v + 3] = centre + new Vector3(0f, -taper, taper);

                if (i > 0)
                {
                    var prev = v - 4;
                    for (var face = 0; face < 4; face++)
                    {
                        var a = prev + face;
                        var b = prev + (face + 1) % 4;
                        var c = v + face;
                        var d = v + (face + 1) % 4;

                        triangles[t++] = a;
                        triangles[t++] = c;
                        triangles[t++] = b;

                        triangles[t++] = b;
                        triangles[t++] = c;
                        triangles[t++] = d;
                    }
                }

                v += 4;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Faces for a small point cloud that is known to be star-shaped about its centre.
        /// </summary>
        /// <remarks>
        /// Not a general convex hull. These are jittered points on a sphere, so connecting each
        /// triple whose plane faces outward is correct for this input and is a few lines rather
        /// than an implementation of quickhull. Wrong for arbitrary input; never given any.
        /// </remarks>
        private static int[] ConvexHullTriangles(Vector3[] points)
        {
            var triangles = new System.Collections.Generic.List<int>(128);

            for (var i = 0; i < points.Length; i++)
            {
                for (var j = i + 1; j < points.Length; j++)
                {
                    for (var k = j + 1; k < points.Length; k++)
                    {
                        var normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                        if (normal.sqrMagnitude < 1e-6f)
                        {
                            continue;
                        }

                        var outward = true;
                        var inward = true;
                        for (var p = 0; p < points.Length; p++)
                        {
                            if (p == i || p == j || p == k)
                            {
                                continue;
                            }

                            if (Vector3.Dot(normal, points[p] - points[i]) > 1e-4f)
                            {
                                outward = false;
                            }
                            else if (Vector3.Dot(normal, points[p] - points[i]) < -1e-4f)
                            {
                                inward = false;
                            }

                            if (!outward && !inward)
                            {
                                break;
                            }
                        }

                        if (outward)
                        {
                            triangles.Add(i);
                            triangles.Add(j);
                            triangles.Add(k);
                        }
                        else if (inward)
                        {
                            triangles.Add(i);
                            triangles.Add(k);
                            triangles.Add(j);
                        }
                    }
                }
            }

            return triangles.ToArray();
        }
    }
}
