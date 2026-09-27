using TheDeep.Creatures;
using TheDeep.Data;
using TheDeep.Submarine;
using UnityEditor;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// The dive site: one enormous vertical shaft. The sub hangs high up in open water with rock walls
    /// on every side, and divers swim DOWN from it. Ledges and caves stick out of the walls at
    /// different depths. The sub can be lowered to four stations, one above the other:
    ///   1 UPPER SHAFT (1280 m)      - ledges 15-50 m under the sub.
    ///   2 THE WALL (1580 m)         - ledges and caves below, something circling in the dark underneath.
    ///   3 ABYSSAL TERRACE (1980 m)  - a huge rock shelf 30 m down: a whale fall, black smokers, worms.
    ///   4 SHAFT FLOOR (2410 m)      - the bottom, 70 m down (needs a longer rope). Dive Team 7 is here.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        static readonly Vector3 Station2 = new(0f, -300f, 0f);
        static readonly Vector3 Station3 = new(10f, -700f, 0f);
        static readonly Vector3 Station4 = new(8f, -1130f, 0f);
        const float ShaftTop = 45f;
        const float ShaftBottom = -1215f;
        const float WallX = 26f, WallZ = 22f;
        const float TerraceY = -730f;
        const float FloorY = -1200f;

        static Transform[] stationPoints;
        static Light downwellingLight;
        static Vector3[] lostDiverSpots;
        static Material rockWall, silt2, deepSilt, leviathanSkin, bioLight, beaconYellow, boulderRock;
        // Centre lines of the caves that have things inside them (set by BuildShaftWalls).
        static Vector3[] eastCavePath, southCavePath, northTerraceCavePath;
        static Mesh glowBlob;

        static void BuildWorld(Transform t, Material rockMat, Material siltMat, Light downwelling)
        {
            InitPoiMaterials();
            glowBlob = GlowBlobMesh();
            downwellingLight = downwelling;
            boulderRock = rockMat;
            rockWall = Mat("Env_RockWall", ProceduralTextures.Rock(), new Color(1.6f, 1.6f, 1.65f), smoothness: 0.2f); // texture is dark; brighten so lit rock reads
            silt2 = siltMat;
            deepSilt = Mat("Env_DeepSilt", ProceduralTextures.Silt(), new Color(0.6f, 0.6f, 0.62f), smoothness: 0.05f);
            leviathanSkin = Mat("Creature_Leviathan", null, new Color(0.03f, 0.035f, 0.04f), smoothness: 0.6f);
            bioLight = Mat("Creature_Biolight", null, new Color(0.4f, 0.9f, 1f), emission: new Color(0.5f, 1.6f, 2f));
            beaconYellow = Mat("POI_BeaconYellow", null, new Color(0.8f, 0.6f, 0.08f), metallic: 0.3f, smoothness: 0.4f);

            BuildShaftWalls(Group("Shaft", t));
            BuildUpperShaft(Group("Zone1_UpperShaft", t));
            BuildWallZone(Group("Zone2_Wall", t));
            BuildTerrace(Group("Zone3_Terrace", t));
            BuildShaftFloor(Group("Zone4_Floor", t));
            BuildWallGlows(Group("WallGlows", t));
            BuildBellyLight();

            var stations = Group("DiveStations", t);
            stationPoints = new[]
            {
                StationPoint(stations, "Station1_UpperShaft", Vector3.zero),
                StationPoint(stations, "Station2_Wall", Station2),
                StationPoint(stations, "Station3_Terrace", Station3),
                StationPoint(stations, "Station4_Floor", Station4),
            };
        }

        static Transform StationPoint(Transform parent, string name, Vector3 pos)
        {
            var p = Group(name, parent);
            p.position = pos;
            return p;
        }

        // ------------------------------------------------------------------ the shaft

        /// <summary>Four huge rock walls facing inward, from above the sub all the way to the floor, with caves at several depths.</summary>
        static void BuildShaftWalls(Transform t)
        {
            float height = ShaftTop - ShaftBottom;
            float Up(float y) => y - ShaftBottom; // world height -> distance up the wall

            // West wall (x = -38) facing +x.
            var west = new Vector3(-WallX, ShaftBottom, -45f);
            CliffWall("Shaft_West", t, west, Vector3.forward, Vector3.right, 90f, height, 4f, 5f, 31, rockWall,
                new CaveMouth { U = 40f, V = Up(-35f), Radius = 3.6f }, new CaveMouth { U = 58f, V = Up(FloorY + 12f), Radius = 4f });
            Tunnel("Cave_WestUpper", t, WallPoint(west, Vector3.forward, 40f, Up(-35f)), Vector3.left, 3.6f, 24f, 32);
            Tunnel("Cave_WestFloor", t, WallPoint(west, Vector3.forward, 58f, Up(FloorY + 12f)), Vector3.left, 4f, 26f, 33);

            // East wall (x = +38) facing -x.
            var east = new Vector3(WallX, ShaftBottom, 45f);
            CliffWall("Shaft_East", t, east, Vector3.back, Vector3.left, 90f, height, 4f, 5f, 34, rockWall,
                new CaveMouth { U = 52f, V = Up(Station2.y - 30f), Radius = 4.2f });
            eastCavePath = Tunnel("Cave_EastWall", t, WallPoint(east, Vector3.back, 52f, Up(Station2.y - 30f)), Vector3.right, 4.2f, 26f, 35);

            // North wall (z = +34) facing -z.
            var north = new Vector3(45f, ShaftBottom, WallZ);
            CliffWall("Shaft_North", t, north, Vector3.left, Vector3.back, 90f, height, 4f, 5f, 36, rockWall,
                new CaveMouth { U = 30f, V = Up(-14f), Radius = 3.4f }, new CaveMouth { U = 62f, V = Up(TerraceY + 6f), Radius = 4.5f });
            Tunnel("Cave_NorthUpper", t, WallPoint(north, Vector3.left, 30f, Up(-14f)), Vector3.forward, 3.4f, 22f, 37);
            northTerraceCavePath = Tunnel("Cave_NorthTerrace", t, WallPoint(north, Vector3.left, 62f, Up(TerraceY + 6f)), Vector3.forward, 4.5f, 26f, 38);

            // South wall (z = -34) facing +z.
            var south = new Vector3(-45f, ShaftBottom, -WallZ);
            CliffWall("Shaft_South", t, south, Vector3.right, Vector3.forward, 90f, height, 4f, 5f, 39, rockWall,
                new CaveMouth { U = 55f, V = Up(Station2.y - 40f), Radius = 3.8f });
            southCavePath = Tunnel("Cave_SouthWall", t, WallPoint(south, Vector3.right, 55f, Up(Station2.y - 40f)), Vector3.back, 3.8f, 24f, 40);
        }

        /// <summary>
        /// A thick rock shelf sticking out of the wall. Named Terrain_ so things can be placed on top of it.
        /// <paramref name="outward"/> points from the wall into the shaft: the back half is buried 8 m into the
        /// rock so the shelf always meets the wall, while the shaft-side edge stays where <paramref name="center"/>
        /// and <paramref name="size"/> put it. Vector3.zero leaves the shelf as given.
        /// </summary>
        static void Slab(string name, Transform t, Vector3 center, Vector3 size, Vector3 outward)
        {
            center -= outward * 4f;
            size += new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z)) * 8f;
            var slab = Primitive(PrimitiveType.Sphere, "Terrain_" + name, t, center, size, rockWall, collider: false);
            var mesh = SlabUVMesh(name, slab.GetComponent<MeshFilter>().sharedMesh, center, size);
            slab.GetComponent<MeshFilter>().sharedMesh = mesh;
            slab.AddComponent<MeshCollider>().sharedMesh = mesh;
            slab.isStatic = true;
        }

        /// <summary>
        /// Copy of the sphere mesh with world-space UVs (seen from above, 4 m per tile like the walls), so the
        /// rock texture isn't stretched once around the whole shelf. Saved as an asset; the scale stays on the transform.
        /// </summary>
        static Mesh SlabUVMesh(string name, Mesh sphere, Vector3 center, Vector3 size)
        {
            var mesh = Object.Instantiate(sphere);
            mesh.name = "Slab_" + name;
            var verts = mesh.vertices;
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                uv[i] = new Vector2(center.x + verts[i].x * size.x, center.z + verts[i].z * size.z) * 0.25f;
            mesh.uv = uv;
            mesh.RecalculateTangents();

            System.IO.Directory.CreateDirectory(TerrainFolder);
            string path = $"{TerrainFolder}/Slab_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ zone 1: upper shaft

        /// <summary>
        /// Ledges under the sub at station 1 (center, size, direction out of the wall). The first points of
        /// interest sit on these; the rock itself also runs back into the wall (see <see cref="Slab"/>).
        /// </summary>
        static readonly (Vector3 center, Vector3 size, Vector3 outward)[] UpperLedges =
        {
            (new Vector3(-19f, -12f, 3f), new Vector3(14f, 3.5f, 16f), Vector3.right),    // 0 west
            (new Vector3(5f, -22f, 16f), new Vector3(20f, 3.5f, 12f), Vector3.back),      // 1 north
            (new Vector3(19f, -30f, -5f), new Vector3(14f, 3.5f, 16f), Vector3.left),     // 2 east
            (new Vector3(-3f, -38f, -16f), new Vector3(18f, 3.5f, 12f), Vector3.forward), // 3 south
        };

        /// <summary>A point on top of upper ledge <paramref name="index"/>, offset from its middle.</summary>
        static Vector3 OnLedge(int index, float dx, float dz, float lift = 0f)
        {
            var c = UpperLedges[index].center;
            return OnGround(c.x + dx, c.z + dz, c.y, lift);
        }

        static void BuildUpperShaft(Transform t)
        {
            for (int i = 0; i < UpperLedges.Length; i++) Slab("Ledge" + i, t, UpperLedges[i].center, UpperLedges[i].size, UpperLedges[i].outward);

            // A few boulders on the ledges.
            var rng = new System.Random(5);
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            for (int i = 0; i < 12; i++)
            {
                var scale = new Vector3(R(1f, 2.6f), R(0.6f, 1.6f), R(1f, 2.6f));
                var boulder = Primitive(PrimitiveType.Sphere, "Boulder", t, OnLedge(i % UpperLedges.Length, R(-4f, 4f), R(-4f, 4f), scale.y * 0.15f), scale, boulderRock, collider: true);
                boulder.transform.localRotation = Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15));
            }

            // Something that shouldn't be here: an old dive helmet and a snapped tether on the deepest ledge.
            var helmetMat = Mat("Env_OldBrass", null, new Color(0.3f, 0.26f, 0.15f), metallic: 0.6f, smoothness: 0.2f);
            Sphere("OldDiveHelmet", t, OnLedge(3, 3f, 1f, 0.15f), 0.4f, helmetMat);
            OldRope(t, OnLedge(3, 3.4f, 1.2f, 0.04f), 6, 55);
        }

        /// <summary>
        /// A cave running from a wall's mouth into the rock along <paramref name="into"/>, wandering a bit.
        /// Returns its centre line (5 points, mouth first) so things can be placed inside.
        /// </summary>
        static Vector3[] Tunnel(string name, Transform t, Vector3 mouth, Vector3 into, float radius, float length, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 side = Vector3.Cross(into, Vector3.up).normalized;
            var path = new Vector3[5];
            path[0] = mouth - into * 1.5f; // start just outside so the mouth overlaps the wall
            for (int i = 1; i < path.Length; i++)
                path[i] = mouth + into * (length * i / (path.Length - 1)) + side * R(-3f, 3f) + Vector3.up * R(-1f, 1f);
            CaveTunnel(name, t, path, radius, seed, rockWall);
            return path;
        }

        /// <summary>A point on a cave's floor, below the middle of its first stretch (falls back to <see cref="OnGround"/>).</summary>
        static Vector3 CaveFloorPoint(Vector3[] path, string caveName)
        {
            var p = Vector3.Lerp(path[1], path[2], 0.5f);
            Physics.SyncTransforms();
            return NearestTerrainHit(p, Vector3.down, 8f, out var floor, "Terrain_" + caveName) ? floor.point : OnGround(p.x, p.z, p.y);
        }

        /// <summary>
        /// <paramref name="local"/> (in <paramref name="root"/>'s space) moved up or down onto the terrain under it,
        /// plus <paramref name="lift"/>. Keeps the height if there's no ground there.
        /// </summary>
        static Vector3 GroundLocal(Transform root, Vector3 local, float lift = 0f)
        {
            var w = root.TransformPoint(local);
            return root.InverseTransformPoint(new Vector3(w.x, GroundAt(w.x, w.z, w.y) + lift, w.z));
        }

        /// <summary>
        /// An old tether lying on the ground from <paramref name="start"/>, wandering towards +x. It drapes over the
        /// rock under it; where the rock ends it runs to the lip, hangs over it and stops.
        /// </summary>
        static void OldRope(Transform t, Vector3 start, int segments, int seed)
        {
            var rng = new System.Random(seed);
            Physics.SyncTransforms();
            Vector3 p = start;
            for (int i = 0; i < segments; i++)
            {
                Vector3 next = p + new Vector3(0.6f + (float)rng.NextDouble() * 0.5f, 0f, (float)rng.NextDouble() * 1.2f - 0.6f);
                if (RopeGround(next, p.y, out float ground))
                {
                    next.y = ground + 0.04f;
                    RopeSegment(t, p, next);
                    p = next;
                    continue;
                }
                // Off the edge: follow the rock out to its last bit of ground...
                Vector3 lip = p;
                for (int k = 1; k <= 8 && RopeGround(Vector3.Lerp(p, next, k / 8f), p.y, out ground); k++)
                {
                    lip = Vector3.Lerp(p, next, k / 8f);
                    lip.y = ground + 0.04f;
                }
                if (lip != p) RopeSegment(t, p, lip);
                // ...then hang down over the lip, leaning out just far enough to clear the rock face.
                var flat = new Vector3(next.x - p.x, 0f, next.z - p.z).normalized;
                Vector3 hang = (flat * 1.2f + Vector3.down).normalized;
                foreach (float lean in new[] { 0.25f, 0.5f, 0.8f })
                {
                    var dir = (flat * lean + Vector3.down).normalized;
                    if (!NearestTerrainHit(lip, dir, 1.1f, out _)) { hang = dir; break; }
                }
                RopeSegment(t, lip, lip + hang * 1.1f);
                return;
            }
        }

        /// <summary>
        /// Height of the walkable rock under <paramref name="q"/> within a couple of metres of <paramref name="nearY"/>
        /// (the ground a rope lies on, not another ledge far below). Call Physics.SyncTransforms() first.
        /// </summary>
        static bool RopeGround(Vector3 q, float nearY, out float y)
        {
            y = float.MinValue;
            foreach (var hit in Physics.RaycastAll(new Vector3(q.x, nearY + 1.5f, q.z), Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.name.StartsWith("Terrain_") && hit.normal.y > 0.3f && hit.point.y > y) y = hit.point.y; // skip rock faces
            return y > float.MinValue;
        }

        static void RopeSegment(Transform t, Vector3 from, Vector3 to)
        {
            var seg = Cylinder("OldTether", t, (from + to) * 0.5f, new Vector3(0.05f, (to - from).magnitude * 0.5f, 0.05f), rope);
            seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }

        // ------------------------------------------------------------------ zone 2: the wall

        static void BuildWallZone(Transform t)
        {
            // Ledges below the station, and things living on them.
            float s = Station2.y;
            Slab("Ledge_Coral", t, new Vector3(-19f, s - 22f, -6f), new Vector3(14f, 3f, 14f), Vector3.right);
            Slab("Ledge_Beacon", t, new Vector3(8f, s - 34f, 16f), new Vector3(18f, 3f, 12f), Vector3.back);
            Slab("Ledge_Deep", t, new Vector3(18f, s - 46f, 6f), new Vector3(14f, 3f, 14f), Vector3.left);
            CoralLedge(t, 11, OnGround(-19f, -6f, s - 22f));
            SurveyBeacon(t, 14, OnGround(8f, 16f, s - 34f));

            // Inside the caves: crystals on the east one's floor, glow-worms on the south one's ceiling.
            CrystalVein(t, 12, CaveFloorPoint(eastCavePath, "Cave_EastWall"), 90f);
            GlowGrotto(t, 13, southCavePath);

            // Something enormous circling in the dark below the sub.
            Leviathan(t, 15, new Vector3(0f, s - 90f, 0f));
        }

        static void CoralLedge(Transform t, int id, Vector3 pos)
        {
            var root = Group("DeepCoral", t);
            root.position = pos;
            var coral = Mat("POI_Coral", null, new Color(0.9f, 0.45f, 0.3f), smoothness: 0.3f, emission: new Color(0.08f, 0.02f, 0.01f));
            var rng = new System.Random(id);
            for (int i = 0; i < 16; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() * 3f - 1.5f, 0f, (float)rng.NextDouble() * 3f - 1.5f);
                float h = 0.4f + (float)rng.NextDouble() * 1.1f;
                var branch = Cylinder("Branch", root, offset + Vector3.up * h * 0.5f, new Vector3(0.07f, h * 0.5f, 0.07f), coral);
                branch.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 50f - 25f, 0f, (float)rng.NextDouble() * 50f - 25f);
            }
            MakeScannable(root.gameObject, id, "DEEP-WATER CORAL", DataCategory.Creature, 110, 4f, 2f);
        }

        static void SurveyBeacon(Transform t, int id, Vector3 pos)
        {
            var root = Group("SurveyBeacon", t);
            root.position = pos;
            root.rotation = Quaternion.Euler(0f, 0f, 20f);
            Cylinder("Body", root, new Vector3(0f, 0.6f, 0f), new Vector3(0.3f, 0.6f, 0.3f), beaconYellow, collider: true);
            var led = Sphere("Blink", root, new Vector3(0f, 1.25f, 0f), 0.1f, lampRed);
            led.AddComponent<BlinkingIndicator>();
            Stencil("TEAM 7", root, new Vector3(0f, 0.6f, -0.16f), Quaternion.identity, 0.05f, Color.black);
            PointLight("BeaconGlow", root, new Vector3(0f, 1.3f, 0f), new Color(1f, 0.15f, 0.1f), 1.2f, 4f, false);
            MakeScannable(root.gameObject, id, "SURVEY BEACON: TEAM 7", DataCategory.Evidence, 150, 4f, 1.5f);
        }

        /// <summary>Glow-worms on the ceiling and upper walls of the back of a cave (<paramref name="path"/> = its centre line).</summary>
        static void GlowGrotto(Transform t, int id, Vector3[] path)
        {
            var root = Group("GlowWormGrotto", t);
            // In the chamber at the back, so the scan zone and the light are inside the cave.
            root.position = Vector3.Lerp(path[3], path[4], 0.4f);
            var threadMat = Mat("Creature_GlowThread", null, new Color(0.3f, 0.6f, 0.7f), emission: new Color(0.1f, 0.32f, 0.4f));
            var rng = new System.Random(id);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var dots = new System.Collections.Generic.List<Renderer>();
            Physics.SyncTransforms();
            for (int attempt = 0; attempt < 200 && dots.Count < 70; attempt++)
            {
                // From somewhere along the back half of the centre line, up at the ceiling or up to 70 degrees to the side.
                int k = rng.Next(2, 4);
                var from = Vector3.Lerp(path[k], path[k + 1], R(0f, 1f));
                var side = Vector3.Cross(path[k + 1] - path[k], Vector3.up).normalized;
                float a = R(-70f, 70f) * Mathf.Deg2Rad;
                if (!NearestTerrainHit(from, Vector3.up * Mathf.Cos(a) + side * Mathf.Sin(a), 12f, out var h, "Terrain_Cave_SouthWall")) continue;
                var dot = GlowBlob("Glow", root, h.point + h.normal * 0.04f, Quaternion.identity, Vector3.one * 0.06f, bioLight);
                dots.Add(dot.GetComponent<Renderer>());
                // Real glow-worms hang sticky threads from the ceiling.
                if (h.normal.y < -0.5f)
                {
                    float length = R(0.2f, 0.7f);
                    var thread = Cylinder("Thread", root, root.InverseTransformPoint(h.point + Vector3.down * (length * 0.5f)),
                        new Vector3(0.02f, length * 0.5f, 0.02f), threadMat);
                    thread.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            var glow = PointLight("GrottoGlow", root, Vector3.zero, new Color(0.3f, 0.9f, 1f), 3f, 8f, false);
            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", dots.ToArray());
            Assign(pulse, "lamp", glow);
            Assign(pulse, "seconds", 5f);
            Assign(pulse, "phaseSpread", 2.399f); // golden angle: every dot on its own beat, so the ceiling twinkles
            MakeScannable(root.gameObject, id, "GLOW-WORM GROTTO", DataCategory.Creature, 130, 5f, 3f);
        }

        /// <summary>A ~35 m shape that circles the middle of the shaft below the sub, dotted with faint lights.</summary>
        static void Leviathan(Transform t, int id, Vector3 center)
        {
            var root = Group("UnknownLargeOrganism", t);
            root.position = center;
            var body = Primitive(PrimitiveType.Capsule, "Body", root, Vector3.zero, new Vector3(6f, 17f, 5f), leviathanSkin, collider: false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tail = Primitive(PrimitiveType.Capsule, "Tail", root, new Vector3(0f, 0f, -19f), new Vector3(2f, 7f, 1.2f), leviathanSkin, collider: false);
            tail.transform.localRotation = Quaternion.Euler(80f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Fin", root, new Vector3(0f, 0f, -26f), new Vector3(9f, 0.4f, 3f), leviathanSkin, collider: false);
            var rng = new System.Random(id);
            for (int i = 0; i < 22; i++)
            {
                float z = -14f + i * 1.3f;
                float side = i % 2 == 0 ? 1f : -1f;
                Sphere("Photophore", root, new Vector3(side * 2.9f, -0.5f + (float)rng.NextDouble(), z), 0.35f, bioLight);
            }
            var drift = root.gameObject.AddComponent<Drifter>();
            Assign(drift, "radius", 8f);
            Assign(drift, "secondsPerLap", 150f);
            Assign(drift, "bobHeight", 10f);
            Assign(drift, "bobSeconds", 40f);
            MakeScannable(root.gameObject, id, "UNKNOWN LARGE ORGANISM", DataCategory.Creature, 300, 8f, 12f);
        }

        // ------------------------------------------------------------------ zone 3: the abyssal terrace

        static void BuildTerrace(Transform t)
        {
            // A huge shelf of rock sticking out of the west side of the shaft, 30 m under the station. The silt
            // top runs into the walls and dips near them (the north cave's floor is just above it there); a
            // rock face hangs from its edge and closes underneath, so it's solid from every side. The rock mass
            // stays inside that face (x -31..-3; the east face runs from -2 at the top to -0.5 at the bottom) and
            // only bulges out of the underside.
            var topCenter = new Vector3(-17f, TerraceY, 0f);
            var top = Heightfield("Terrace_Top", t, topCenter, 30f, 52f, 2f, 1.2f, 0.06f, 11, deepSilt,
                (x, z) => -1.5f * Mathf.InverseLerp(20f, 22f, Mathf.Abs(z)));
            TerraceSkirt("Terrace_Skirt", t, top, topCenter, 30f, 52f, 2f, 14f, 12, rockWall);
            Slab("Terrace_Rock", t, new Vector3(-17f, TerraceY - 8.5f, 0f), new Vector3(28f, 14f, 50f), Vector3.zero);

            float g = TerraceY;
            WhaleFall(t, 16, OnGround(-15f, -9f, g));
            ThermalVent(t, 17, OnGround(-18f, 14f, g), "BLACK SMOKER FIELD", 140);
            ThermalVent(t, 0, OnGround(-12f, 17f, g));
            ThermalVent(t, 0, OnGround(-21f, 18f, g));
            TubeWormForest(t, 18, OnGround(-15f, 16f, g));
            Isopod(t, 19, OnGround(-8f, 4f, g, 0.2f), OnGround(-6f, -6f, g, 0.2f));
            // Crystals on the floor of the north cave at terrace level.
            CrystalVein(t, 20, CaveFloorPoint(northTerraceCavePath, "Cave_NorthTerrace"), 200f);
        }

        static void WhaleFall(Transform t, int id, Vector3 pos)
        {
            var root = Group("WhaleFall", t);
            root.position = pos;
            root.rotation = Quaternion.Euler(0f, 25f, 0f);
            for (int i = 0; i < 34; i++)
            {
                float z = -13f + i * 0.8f;
                float size = Mathf.Lerp(0.9f, 0.3f, Mathf.Abs(i - 10) / 24f);
                // Each bone rests on the silt under it; the ribs follow their vertebra.
                var vertebra = GroundLocal(root, new Vector3(0f, 0f, z), size * 0.4f);
                Box("Vertebra", root, vertebra, new Vector3(size, size * 0.8f, 0.5f), bone, collider: false, worldUV: false);
                if (i >= 4 && i < 16 && i % 2 == 0)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var rib = Cylinder("Rib", root, new Vector3(side * 1.6f, vertebra.y + 1.1f, z), new Vector3(0.18f, 2.2f, 0.18f), bone);
                        rib.transform.localRotation = Quaternion.Euler(0f, 0f, side * 35f);
                    }
                }
            }
            Primitive(PrimitiveType.Sphere, "Skull", root, GroundLocal(root, new Vector3(0f, 0f, 15f), 0.8f), new Vector3(3.2f, 1.8f, 4.5f), bone, collider: true);
            MakeScannable(root.gameObject, id, "WHALE FALL", DataCategory.Creature, 160, 6f, 8f);
        }

        static void TubeWormForest(Transform t, int id, Vector3 pos)
        {
            var root = Group("TubeWormForest", t);
            root.position = pos;
            var red = Mat("POI_WormPlume", null, new Color(0.8f, 0.08f, 0.06f), emission: new Color(0.15f, 0.0f, 0.0f));
            var rng = new System.Random(id);
            Physics.SyncTransforms();
            for (int placed = 0, attempt = 0; placed < 60 && attempt < 150; attempt++)
            {
                var offset = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 7f;
                float h = 0.8f + (float)rng.NextDouble() * 1.8f;
                var tilt = Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, 0, (float)rng.NextDouble() * 16f - 8f);
                // Rooted in the silt, and never inside a vent chimney (anything solid that isn't terrain).
                offset = GroundLocal(root, offset);
                bool blocked = false;
                foreach (var c in Physics.OverlapSphere(root.TransformPoint(offset) + Vector3.up * 0.6f, 0.15f, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.name.StartsWith("Terrain_")) blocked = true;
                if (blocked) continue;
                var tube = Cylinder("Tube", root, offset + Vector3.up * h * 0.5f, new Vector3(0.1f, h * 0.5f, 0.1f), bone);
                tube.transform.localRotation = tilt;
                Sphere("Plume", root, offset + Vector3.up * (h + 0.05f), 0.2f, red);
                placed++;
            }
            MakeScannable(root.gameObject, id, "TUBE WORM FOREST", DataCategory.Creature, 120, 4f, 4f);
        }

        // ------------------------------------------------------------------ zone 4: the shaft floor

        static void BuildShaftFloor(Transform t)
        {
            // Runs well under the walls so no gap opens where they bulge out. The rubble stays low enough to
            // pass under the west floor cave (whose floor is at about -1191).
            Heightfield("Shaft_Floor", t, new Vector3(0f, FloorY, 0f), 76f, 68f, 2f, 1.2f, 0.06f, 21, deepSilt, (x, z) =>
            {
                // Rubble piled up against the walls.
                float nearX = Mathf.Max(0f, Mathf.Abs(x) - 20f), nearZ = Mathf.Max(0f, Mathf.Abs(z) - 16f);
                return Mathf.Min((nearX * nearX + nearZ * nearZ) * 0.08f, 4f);
            });

            float g = FloorY;
            DivingBellWreck(t, 21, OnGround(-12f, 6f, g));
            StructureCluster(t, 22, OnGround(12f, -4f, g));
            // Dive Team 7. Their bodies (and camera chips) are added by the footage step.
            lostDiverSpots = new[] { OnGround(-16f, -4f, g), OnGround(-4f, 12f, g), OnGround(6f, -12f, g), OnGround(17f, 6f, g) };
            OldRope(t, OnGround(-15f, 5f, g, 0.05f), 12, 71);
            OldRope(t, OnGround(0f, -12f, g, 0.05f), 9, 72);
        }

        static void DivingBellWreck(Transform t, int id, Vector3 pos)
        {
            var root = Group("DivingBellWreck", t);
            root.position = pos;
            root.rotation = Quaternion.Euler(12f, 40f, 25f);
            var shell = Mat("POI_BellShell", ProceduralTextures.HullExterior(), new Color(0.55f, 0.45f, 0.4f), metallic: 0.3f);
            var bellCenter = new Vector3(0f, 1.3f, 0f);
            var bellRadii = new Vector3(1.3f, 1.4f, 1.3f);
            Primitive(PrimitiveType.Sphere, "Bell", root, bellCenter, bellRadii * 2f, shell, collider: true);
            Cylinder("Window", root, new Vector3(0f, 1.5f, -1.28f), new Vector3(0.6f, 0.03f, 0.6f), glassDark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                var leg = Cylinder("Frame", root, new Vector3(Mathf.Cos(a) * 1.4f, 0.4f, Mathf.Sin(a) * 1.4f), new Vector3(0.12f, 0.9f, 0.12f), rust);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, i == 2 ? 70f : 10f); // one bent flat
            }
            // Rust nameplate on the hull above the window, lying flat on the surface (touching it in the middle,
            // on two brackets at the ends where the hull curves away), with the name painted on it.
            float dy = 2f - bellCenter.y;
            var onHull = bellCenter + new Vector3(0f, dy, -bellRadii.z * Mathf.Sqrt(1f - dy * dy / (bellRadii.y * bellRadii.y)));
            var d = onHull - bellCenter;
            var normal = new Vector3(d.x / (bellRadii.x * bellRadii.x), d.y / (bellRadii.y * bellRadii.y), d.z / (bellRadii.z * bellRadii.z)).normalized;
            var plateRot = Quaternion.LookRotation(-normal, Vector3.up); // reads when looking at the hull
            var plate = Group("Nameplate", root);
            plate.localPosition = onHull + normal * 0.02f;
            plate.localRotation = plateRot;
            Box("Plate", plate, Vector3.zero, new Vector3(1.3f, 0.2f, 0.04f), rust, collider: false, worldUV: false);
            foreach (float x in new[] { -0.55f, 0.55f })
                Box("Bracket", plate, new Vector3(x, 0f, 0.1f), new Vector3(0.06f, 0.1f, 0.16f), rust, collider: false, worldUV: false);
            Stencil("TEAM 7 - BELL 2", root, onHull + normal * (0.02f + 0.025f), plateRot, 0.1f, new Color(0.9f, 0.85f, 0.7f));
            MakeScannable(root.gameObject, id, "TEAM 7 DIVING BELL (WRECKED)", DataCategory.Evidence, 220, 5f, 3f);
        }

        static void StructureCluster(Transform t, int id, Vector3 pos)
        {
            var root = Group("StructureCluster", t);
            root.position = pos;
            var glyphs = new System.Collections.Generic.List<Renderer>();
            var rng = new System.Random(id);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var stone = Group("Monolith", root);
                stone.localPosition = new Vector3(Mathf.Cos(a) * 6f, 0f, Mathf.Sin(a) * 4f);
                stone.localRotation = Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, -a * Mathf.Rad2Deg + 90f, (float)rng.NextDouble() * 10f - 5f);
                float h = 5f + (float)rng.NextDouble() * 5f;
                Box("Stone", stone, new Vector3(0f, h * 0.5f, 0f), new Vector3(1.4f, h, 0.9f), rock);
                for (int k = 0; k < 6; k++)
                {
                    var glyph = Box("Glyph", stone, new Vector3(0f, 1f + k * h / 7f, -0.46f), new Vector3(0.6f, 0.05f, 0.01f), monolithGlyph, collider: false, worldUV: false);
                    glyphs.Add(glyph.GetComponent<Renderer>());
                }
            }
            var glow = PointLight("ClusterGlow", root, new Vector3(0f, 3f, 0f), new Color(0.1f, 0.9f, 0.8f), 2f, 12f, false);
            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", glyphs.ToArray());
            Assign(pulse, "lamp", glow);
            Assign(pulse, "seconds", 9f);
            MakeScannable(root.gameObject, id, "STRUCTURE CLUSTER (ORIGIN UNKNOWN)", DataCategory.Structure, 300, 8f, 8f);
        }

        // ------------------------------------------------------------------ depth cues

        /// <summary>
        /// Patches of glowing creatures on the walls all the way down, so the drop reads as a drop:
        /// looking down you see them fade away into the dark.
        /// </summary>
        static void BuildWallGlows(Transform t)
        {
            var green = Mat("Creature_WallGlowGreen", null, new Color(0.3f, 1f, 0.6f), emission: new Color(0.4f, 2.6f, 1.3f));
            var blue = Mat("Creature_WallGlowBlue", null, new Color(0.4f, 0.8f, 1f), emission: new Color(0.5f, 1.5f, 2.8f));
            Physics.SyncTransforms();
            var rng = new System.Random(77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var dirs = new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            for (float y = -4f; y > FloorY + 4f; y -= 7f)
            {
                var dir = dirs[rng.Next(dirs.Length)];
                var side = Vector3.Cross(dir, Vector3.up);
                var from = new Vector3(0f, y + R(-2f, 2f), 0f) + side * R(-14f, 14f);
                if (!RaycastWall(from, dir, out var hit)) continue;
                // Skip patches hidden behind a ledge (or the floor rubble): the nearest rock must be the wall itself.
                if (!NearestTerrainHit(from, dir, 60f, out hit) || !IsShaftWallName(hit.collider.name)) continue;
                var mat = rng.NextDouble() < 0.6 ? green : blue;
                int count = rng.Next(4, 9), placed = 0;
                for (int i = 0; i < count; i++)
                {
                    // Each dot finds the rock under it (the wall is far from flat), then lies flat on it like lichen.
                    var guess = hit.point + side * R(-1.6f, 1.6f) + Vector3.up * R(-1.2f, 1.2f);
                    float size = R(0.15f, 0.35f);
                    if (!NearestTerrainHit(guess - dir * 6f, dir, 12f, out var h) || !IsShaftWallName(h.collider.name)) continue;
                    var dot = GlowBlob("Glow", t, h.point + h.normal * size * 0.2f, Quaternion.LookRotation(h.normal),
                        new Vector3(size, size, size * 0.35f), mat);
                    dot.isStatic = true;
                    placed++;
                }
                // Near the stations the patches light up the rock around them too.
                if (placed > 0 && NearStation(hit.point.y))
                    PointLight("GlowLight", t, hit.point + hit.normal * 1.2f, mat == green ? new Color(0.3f, 1f, 0.6f) : new Color(0.4f, 0.8f, 1f), 2.5f, 9f, false);
            }
        }

        /// <summary>A glowing speck: the shared low-poly <see cref="GlowBlobMesh"/> (unit size, like Sphere), casting no shadow.</summary>
        static GameObject GlowBlob(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = glowBlob;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// An octahedron 1 m across (6 vertices, 8 triangles) for the ~1000 glow specks, instead of 768-triangle
        /// spheres. Saved as an asset (rebuilt every build).
        /// </summary>
        static Mesh GlowBlobMesh()
        {
            const string folder = "Assets/_Project/Models/Generated";
            const string path = folder + "/GlowBlob.asset";
            var verts = new[]
            {
                Vector3.right * 0.5f, Vector3.left * 0.5f, Vector3.up * 0.5f, Vector3.down * 0.5f, Vector3.forward * 0.5f, Vector3.back * 0.5f,
            };
            var tris = new System.Collections.Generic.List<int>();
            foreach (int sx in new[] { 1, -1 })
            foreach (int sy in new[] { 1, -1 })
            foreach (int sz in new[] { 1, -1 })
            {
                int x = sx > 0 ? 0 : 1, y = sy > 0 ? 2 : 3, z = sz > 0 ? 4 : 5;
                // (x, y, z) faces outward when an even number of the signs are negative; otherwise swap two.
                if (sx * sy * sz > 0) tris.AddRange(new[] { x, y, z });
                else tris.AddRange(new[] { x, z, y });
            }
            var mesh = new Mesh { name = "GlowBlob" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static bool NearStation(float y)
        {
            foreach (float station in new[] { 0f, Station2.y, Station3.y, Station4.y })
                if (y < station + 10f && y > station - 75f) return true;
            return false;
        }

        static bool RaycastWall(Vector3 from, Vector3 dir, out RaycastHit wall)
        {
            foreach (var hit in Physics.RaycastAll(from, dir, 60f))
                if (hit.collider.name.StartsWith("Terrain_Shaft_")) { wall = hit; return true; }
            wall = default;
            return false;
        }

        /// <summary>
        /// The nearest terrain hit along a ray (triggers ignored): any Terrain_ collider, or only the one named
        /// <paramref name="only"/>. Call Physics.SyncTransforms() first.
        /// </summary>
        static bool NearestTerrainHit(Vector3 from, Vector3 dir, float distance, out RaycastHit nearest, string only = null)
        {
            nearest = default;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(from, dir, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                string name = hit.collider.name;
                if (only != null ? name != only : !name.StartsWith("Terrain_")) continue;
                if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
            }
            return found;
        }

        /// <summary>One of the four shaft walls (not the floor, which shares the Terrain_Shaft_ prefix).</summary>
        static bool IsShaftWallName(string name) => name.StartsWith("Terrain_Shaft_") && name != "Terrain_Shaft_Floor";

        /// <summary>A big floodlight under the sub pointing straight down, lighting up whatever is below.</summary>
        static void BuildBellyLight()
        {
            var sub = GameObject.Find("Submarine").transform;
            var housing = Mat("Sub_LampHousing", null, new Color(0.15f, 0.15f, 0.16f), metallic: 0.6f, smoothness: 0.4f);
            var lens = Mat("Sub_LampLens", null, new Color(1f, 0.95f, 0.8f), emission: new Color(3f, 2.8f, 2.3f));
            Cylinder("BellyLightHousing", sub, new Vector3(-1f, -1.15f, 0f), new Vector3(0.7f, 0.12f, 0.7f), housing);
            Cylinder("BellyLightLens", sub, new Vector3(-1f, -1.28f, 0f), new Vector3(0.55f, 0.02f, 0.55f), lens);
            var go = new GameObject("BellyFloodlight", typeof(Light));
            go.transform.SetParent(sub, false);
            go.transform.localPosition = new Vector3(-1f, -1.4f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var light = go.GetComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.spotAngle = 115f;
            light.innerSpotAngle = 60f;
            light.range = 75f;
            light.intensity = 900f;
            light.shadows = LightShadows.Hard;
        }

        // ------------------------------------------------------------------ navigation

        static void BuildNavigation(GameObject expeditionObject)
        {
            var nav = expeditionObject.AddComponent<SubNavigation>();
            var so = new SerializedObject(nav);
            so.FindProperty("submarine").objectReferenceValue = GameObject.Find("Submarine").transform;
            so.FindProperty("downwelling").objectReferenceValue = downwellingLight;
            var list = so.FindProperty("stations");
            (string name, int level, float light, Color fog, float density)[] data =
            {
                ("Upper Shaft", 0, 0.85f, new Color(0.012f, 0.04f, 0.05f), 0.028f),
                ("The Wall", 1, 0.4f, new Color(0.007f, 0.025f, 0.035f), 0.03f),
                ("Abyssal Terrace", 2, 0.14f, new Color(0.004f, 0.013f, 0.022f), 0.034f),
                ("Shaft Floor", 3, 0.04f, new Color(0.002f, 0.005f, 0.009f), 0.038f),
            };
            list.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = data[i].name;
                e.FindPropertyRelative("point").objectReferenceValue = stationPoints[i];
                e.FindPropertyRelative("requiredDepthLevel").intValue = data[i].level;
                e.FindPropertyRelative("downwelling").floatValue = data[i].light;
                e.FindPropertyRelative("fogColor").colorValue = data[i].fog;
                e.FindPropertyRelative("fogDensity").floatValue = data[i].density;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
