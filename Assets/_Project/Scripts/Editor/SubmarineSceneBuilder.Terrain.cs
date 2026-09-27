using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// Procedural rock: seafloor heightfields, displaced cliff walls (with holes for cave mouths),
    /// and cave tunnels (winding tubes seen from the inside). Meshes are saved as assets.
    /// All vertices are in world space; objects sit at the origin.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        const string TerrainFolder = "Assets/_Project/Models/Generated/Terrain";

        public struct CaveMouth
        {
            public float U, V, Radius; // position on the wall surface, metres from its origin
        }

        static float Fbm(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0f, amplitude = 1f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Mathf.PerlinNoise(x * frequency + seed * 31.7f, y * frequency + seed * 17.3f) - 0.5f) * 2f * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Seafloor patch. <paramref name="extra"/> adds a shape on top of the noise (ridges, slopes).</summary>
        static GameObject Heightfield(string name, Transform parent, Vector3 center, float sizeX, float sizeZ, float cell,
            float amplitude, float noiseScale, int seed, Material mat, Func<float, float, float> extra = null)
        {
            int nx = Mathf.CeilToInt(sizeX / cell), nz = Mathf.CeilToInt(sizeZ / cell);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[verts.Length];
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                float wx = center.x - sizeX / 2f + x * sizeX / nx;
                float wz = center.z - sizeZ / 2f + z * sizeZ / nz;
                float h = Fbm(wx * noiseScale, wz * noiseScale, seed) * amplitude + (extra?.Invoke(wx, wz) ?? 0f);
                verts[z * (nx + 1) + x] = new Vector3(wx, center.y + h, wz);
                uv[z * (nx + 1) + x] = new Vector2(wx, wz) * 0.3f;
            }
            var tris = new List<int>();
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
            return MeshObject(name, parent, verts, uv, tris, Vector3.up, mat);
        }

        /// <summary>
        /// A rock face: a grid spanning <paramref name="width"/> along <paramref name="right"/> and
        /// <paramref name="height"/> along up, bulging in and out along <paramref name="facing"/>.
        /// Cave mouths are left open as holes.
        /// </summary>
        static GameObject CliffWall(string name, Transform parent, Vector3 origin, Vector3 right, Vector3 facing,
            float width, float height, float cell, float roughness, int seed, Material mat, params CaveMouth[] caves)
        {
            int nu = Mathf.CeilToInt(width / cell), nv = Mathf.CeilToInt(height / cell);
            var verts = new Vector3[(nu + 1) * (nv + 1)];
            var uv = new Vector2[verts.Length];
            for (int v = 0; v <= nv; v++)
            for (int u = 0; u <= nu; u++)
            {
                float mu = u * width / nu, mv = v * height / nv;
                // Big slow bulges plus sharper detail; pushed back a little around cave mouths so they read as openings.
                float bulge = Fbm(mu * 0.02f, mv * 0.02f, seed, 3) * roughness * 2.5f + Fbm(mu * 0.12f, mv * 0.12f, seed + 5) * roughness;
                foreach (var cave in caves)
                {
                    float d = Vector2.Distance(new Vector2(mu, mv), new Vector2(cave.U, cave.V));
                    if (d < cave.Radius * 2.5f) bulge -= (1f - d / (cave.Radius * 2.5f)) * cave.Radius * 0.8f;
                }
                verts[v * (nu + 1) + u] = origin + right * mu + Vector3.up * mv + facing * bulge;
                uv[v * (nu + 1) + u] = new Vector2(mu, mv) * 0.25f;
            }
            var tris = new List<int>();
            for (int v = 0; v < nv; v++)
            for (int u = 0; u < nu; u++)
            {
                var center = new Vector2((u + 0.5f) * width / nu, (v + 0.5f) * height / nv);
                bool hole = false;
                foreach (var cave in caves)
                    if (Vector2.Distance(center, new Vector2(cave.U, cave.V)) < cave.Radius * 0.9f) hole = true;
                if (hole) continue;
                int a = v * (nu + 1) + u, b = a + 1, c = a + nu + 1, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
            return MeshObject(name, parent, verts, uv, tris, facing, mat);
        }

        /// <summary>World position of a point on a wall built with <see cref="CliffWall"/> (before bulging).</summary>
        static Vector3 WallPoint(Vector3 origin, Vector3 right, float u, float v) => origin + right * u + Vector3.up * v;

        /// <summary>
        /// A cave: a noisy tube through <paramref name="path"/>, seen from the inside, swelling into a
        /// chamber near the end and closing off at the back.
        /// </summary>
        static GameObject CaveTunnel(string name, Transform parent, Vector3[] path, float radius, int seed, Material mat)
        {
            const int around = 18;
            const float step = 1.2f;
            // Resample the path evenly.
            var centers = new List<Vector3>();
            for (int i = 0; i < path.Length - 1; i++)
            {
                float len = Vector3.Distance(path[i], path[i + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (int k = 0; k < n; k++) centers.Add(Vector3.Lerp(path[i], path[i + 1], k / (float)n));
            }
            centers.Add(path[^1]);

            int rings = centers.Count;
            var verts = new Vector3[rings * (around + 1)];
            var uv = new Vector2[verts.Length];
            float travelled = 0f;
            for (int i = 0; i < rings; i++)
            {
                Vector3 tangent = (centers[Mathf.Min(i + 1, rings - 1)] - centers[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                Vector3 up = Vector3.Cross(side, tangent).normalized;
                float t = i / (float)(rings - 1);
                // Mouth at full size, a chamber around 70% of the way in, closing at the very end.
                float profile = (1f + 0.8f * Mathf.Exp(-Mathf.Pow((t - 0.7f) / 0.15f, 2f))) * Mathf.Clamp01((1f - t) * 8f + 0.08f);
                if (i > 0) travelled += Vector3.Distance(centers[i], centers[i - 1]);
                for (int k = 0; k <= around; k++)
                {
                    float a = k / (float)around * Mathf.PI * 2f;
                    float noise = 1f + 0.3f * Fbm(travelled * 0.25f, k * 0.35f, seed);
                    // Flatter floor so it's easier to swim along.
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a) * (Mathf.Sin(a) < 0f ? 0.7f : 1f);
                    verts[i * (around + 1) + k] = centers[i] + dir * radius * profile * noise;
                    uv[i * (around + 1) + k] = new Vector2(k / (float)around * 4f, travelled * 0.25f);
                }
            }
            var tris = new List<int>();
            for (int i = 0; i < rings - 1; i++)
            for (int k = 0; k < around; k++)
            {
                int a = i * (around + 1) + k, b = a + 1, c = a + around + 1, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
            // Faces point inward: from the wall of the first ring towards its centre.
            Vector3 inward = centers[0] - verts[0];
            var go = MeshObject(name, parent, verts, uv, tris, inward, mat);
            // Seen from outside every face is a back face, so with normal shadow casting the rock above a
            // cave never blocks the downwelling light and cave floors glow like open water.
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return go;
        }

        /// <summary>
        /// A rock face hanging from the edge of a <see cref="Heightfield"/> patch (same centre, size and cell),
        /// so the patch reads as the top of a solid shelf instead of a sheet: every edge vertex gets a copy
        /// <paramref name="depth"/> m lower (give or take 2 m) and 1.5 m further out, joined by outward-facing quads.
        /// A downward-facing cap closes the bottom, so the shelf is solid from below too.
        /// </summary>
        static GameObject TerraceSkirt(string name, Transform parent, GameObject field, Vector3 center, float sizeX, float sizeZ,
            float cell, float depth, int seed, Material mat)
        {
            int nx = Mathf.CeilToInt(sizeX / cell), nz = Mathf.CeilToInt(sizeZ / cell);
            var top = field.GetComponent<MeshFilter>().sharedMesh.vertices;
            // Walk the edge once around: south (+x), east (+z), north (-x), west (-z).
            var edge = new List<int>();
            for (int x = 0; x < nx; x++) edge.Add(x);
            for (int z = 0; z < nz; z++) edge.Add(z * (nx + 1) + nx);
            for (int x = nx; x > 0; x--) edge.Add(nz * (nx + 1) + x);
            for (int z = nz; z > 0; z--) edge.Add(z * (nx + 1));
            edge.Add(edge[0]); // close the loop with its own column so the texture wraps without a seam

            // Face: a top and a bottom vertex per edge point. Cap: its own copy of the bottom ring (so the rim
            // is a hard edge and the underside gets top-down UVs) plus a centre vertex at the average bottom height.
            int ring = edge.Count * 2, hub = ring + edge.Count;
            var verts = new Vector3[hub + 1];
            var uv = new Vector2[verts.Length];
            float along = 0f, bottomY = 0f;
            for (int i = 0; i < edge.Count; i++)
            {
                Vector3 p = top[edge[i]];
                if (i > 0) along += Vector3.Distance(p, top[edge[i - 1]]);
                Vector3 outward = new Vector3(p.x - center.x, 0f, p.z - center.z).normalized;
                float noiseAt = i == edge.Count - 1 ? 0f : along; // the closing column must meet the first one
                Vector3 bottom = p + outward * 1.5f + Vector3.down * (depth + Fbm(noiseAt * 0.15f, 3.7f, seed) * 2f);
                verts[i * 2] = p;
                verts[i * 2 + 1] = bottom;
                uv[i * 2] = new Vector2(along, p.y) * 0.25f;
                uv[i * 2 + 1] = new Vector2(along, bottom.y) * 0.25f;
                verts[ring + i] = bottom;
                uv[ring + i] = new Vector2(bottom.x, bottom.z) * 0.25f;
                if (i < edge.Count - 1) bottomY += bottom.y / (edge.Count - 1);
            }
            verts[hub] = new Vector3(center.x, bottomY, center.z);
            uv[hub] = new Vector2(center.x, center.z) * 0.25f;

            // Walking the edge anticlockwise from above, (top i, top i+1, bottom i) faces away from the centre
            // and (bottom i, bottom i+1, centre) faces down. MeshObject only looks at the first triangle.
            var tris = new List<int>();
            for (int i = 0; i < edge.Count - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.AddRange(new[] { a, c, b, c, d, b });
            }
            for (int i = 0; i < edge.Count - 1; i++)
                tris.AddRange(new[] { ring + i, ring + i + 1, hub });
            Vector3 firstOut = (verts[0] + verts[2]) * 0.5f - center;
            firstOut.y = 0f;
            return MeshObject(name, parent, verts, uv, tris, firstOut, mat);
        }

        /// <summary>Builds the mesh (flipping triangles so the first one faces <paramref name="frontDirection"/>), saves it, adds a collider.</summary>
        static GameObject MeshObject(string name, Transform parent, Vector3[] verts, Vector2[] uv, List<int> tris, Vector3 frontDirection, Material mat)
        {
            if (tris.Count >= 3)
            {
                Vector3 n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
                if (Vector3.Dot(n, frontDirection) < 0f)
                    for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            System.IO.Directory.CreateDirectory(TerrainFolder);
            string path = $"{TerrainFolder}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject("Terrain_" + name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        /// <summary>
        /// Height of the terrain under (x, z) near the expected height <paramref name="fallback"/> (zones are
        /// stacked, so the search starts just above it). Falls back if nothing is there.
        /// </summary>
        static float GroundAt(float x, float z, float fallback)
        {
            Physics.SyncTransforms();
            float best = float.MinValue;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, fallback + 40f, z), Vector3.down, 120f))
                if (hit.collider.name.StartsWith("Terrain_") && hit.normal.y > 0.3f && hit.point.y > best) best = hit.point.y; // skip wall faces
            return best > float.MinValue ? best : fallback;
        }

        static Vector3 OnGround(float x, float z, float fallback, float lift = 0f) => new(x, GroundAt(x, z, fallback) + lift, z);
    }
}
