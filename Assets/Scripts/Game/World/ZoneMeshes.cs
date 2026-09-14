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
        /// <summary>Vertices per side of the ground grid. 33 gives 32 quads and ~2k triangles.</summary>
        private const int GroundResolution = 33;

        /// <summary>
        /// Builds a rolling ground mesh with a flat-ish apron near the origin.
        /// </summary>
        /// <remarks>
        /// The apron matters: the player spawns at the origin, and terrain noise under the spawn
        /// point is how a character controller ends up embedded in a hill on the first frame. Height
        /// is faded to zero within <paramref name="flatRadius"/> so the entry area is always safe.
        /// </remarks>
        /// <param name="size">Width and depth in metres.</param>
        /// <param name="amplitude">Peak height of the undulation.</param>
        /// <param name="seed">Chooses the noise offsets; the same seed gives the same ground.</param>
        /// <param name="flatRadius">Radius around the origin kept level for the spawn.</param>
        /// <returns>A new mesh. The caller owns it.</returns>
        public static Mesh BuildGround(float size, float amplitude, int seed, float flatRadius)
        {
            var mesh = new Mesh { name = "ZoneGround" };

            // 16-bit indices cap at 65535 vertices; 33x33 = 1089, so the default format is fine and
            // we avoid the memory cost of a 32-bit index buffer on mobile.
            var vertexCount = GroundResolution * GroundResolution;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var colors = new Color[vertexCount];

            var random = new System.Random(seed);
            var offsetA = (float)random.NextDouble() * 100f;
            var offsetB = (float)random.NextDouble() * 100f;
            var half = size * 0.5f;
            var step = size / (GroundResolution - 1);

            for (var z = 0; z < GroundResolution; z++)
            {
                for (var x = 0; x < GroundResolution; x++)
                {
                    var index = z * GroundResolution + x;
                    var worldX = -half + x * step;
                    var worldZ = -half + z * step;

                    // Two octaves is enough to stop the ground reading as a repeating pattern while
                    // staying cheap to evaluate at build time.
                    var height =
                        Mathf.PerlinNoise(offsetA + worldX * 0.035f, offsetA + worldZ * 0.035f) * amplitude +
                        Mathf.PerlinNoise(offsetB + worldX * 0.11f, offsetB + worldZ * 0.11f) * amplitude * 0.35f;

                    var distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
                    if (distance < flatRadius)
                    {
                        height *= Mathf.SmoothStep(0f, 1f, distance / flatRadius);
                    }

                    vertices[index] = new Vector3(worldX, height, worldZ);
                    uvs[index] = new Vector2(x / (float)(GroundResolution - 1), z / (float)(GroundResolution - 1));

                    // Vertex colour carries the height gradient so one unlit material can shade the
                    // whole zone without a texture, a second material, or a custom shader.
                    var shade = Mathf.InverseLerp(0f, amplitude * 1.35f, height);
                    colors[index] = Color.Lerp(new Color(0.10f, 0.14f, 0.12f), new Color(0.24f, 0.28f, 0.22f), shade);
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
        /// </remarks>
        /// <param name="x">World X.</param>
        /// <param name="z">World Z.</param>
        /// <param name="amplitude">Same amplitude passed to <see cref="BuildGround"/>.</param>
        /// <param name="seed">Same seed passed to <see cref="BuildGround"/>.</param>
        /// <param name="flatRadius">Same flat radius passed to <see cref="BuildGround"/>.</param>
        /// <returns>Ground height at that point.</returns>
        public static float SampleHeight(float x, float z, float amplitude, int seed, float flatRadius)
        {
            var random = new System.Random(seed);
            var offsetA = (float)random.NextDouble() * 100f;
            var offsetB = (float)random.NextDouble() * 100f;

            var height =
                Mathf.PerlinNoise(offsetA + x * 0.035f, offsetA + z * 0.035f) * amplitude +
                Mathf.PerlinNoise(offsetB + x * 0.11f, offsetB + z * 0.11f) * amplitude * 0.35f;

            var distance = Mathf.Sqrt(x * x + z * z);
            if (distance < flatRadius)
            {
                height *= Mathf.SmoothStep(0f, 1f, distance / flatRadius);
            }

            return height;
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
