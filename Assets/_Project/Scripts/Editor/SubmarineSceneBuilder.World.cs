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
        const float WallX = 38f, WallZ = 34f;
        const float TerraceY = -730f;
        const float FloorY = -1200f;

        static Transform[] stationPoints;
        static Light downwellingLight;
        static Vector3[] lostDiverSpots;
        static Material rockWall, silt2, deepSilt, leviathanSkin, bioLight, beaconYellow, boulderRock;

        static void BuildWorld(Transform t, Material rockMat, Material siltMat, Light downwelling)
        {
            InitPoiMaterials();
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
            Tunnel("Cave_EastWall", t, WallPoint(east, Vector3.back, 52f, Up(Station2.y - 30f)), Vector3.right, 4.2f, 26f, 35);

            // North wall (z = +34) facing -z.
            var north = new Vector3(45f, ShaftBottom, WallZ);
            CliffWall("Shaft_North", t, north, Vector3.left, Vector3.back, 90f, height, 4f, 5f, 36, rockWall,
                new CaveMouth { U = 30f, V = Up(-14f), Radius = 3.4f }, new CaveMouth { U = 62f, V = Up(TerraceY + 6f), Radius = 4.5f });
            Tunnel("Cave_NorthUpper", t, WallPoint(north, Vector3.left, 30f, Up(-14f)), Vector3.forward, 3.4f, 22f, 37);
            Tunnel("Cave_NorthTerrace", t, WallPoint(north, Vector3.left, 62f, Up(TerraceY + 6f)), Vector3.forward, 4.5f, 26f, 38);

            // South wall (z = -34) facing +z.
            var south = new Vector3(-45f, ShaftBottom, -WallZ);
            CliffWall("Shaft_South", t, south, Vector3.right, Vector3.forward, 90f, height, 4f, 5f, 39, rockWall,
                new CaveMouth { U = 55f, V = Up(Station2.y - 40f), Radius = 3.8f });
            Tunnel("Cave_SouthWall", t, WallPoint(south, Vector3.right, 55f, Up(Station2.y - 40f)), Vector3.back, 3.8f, 24f, 40);
        }

        /// <summary>A thick rock shelf sticking out of the wall. Named Terrain_ so things can be placed on top of it.</summary>
        static void Slab(string name, Transform t, Vector3 center, Vector3 size)
        {
            var slab = Primitive(PrimitiveType.Sphere, "Terrain_" + name, t, center, size, rockWall, collider: false);
            slab.AddComponent<MeshCollider>().sharedMesh = slab.GetComponent<MeshFilter>().sharedMesh;
            slab.isStatic = true;
        }

        // ------------------------------------------------------------------ zone 1: upper shaft

        /// <summary>Ledges under the sub at station 1 (center, size). The first points of interest sit on these.</summary>
        static readonly (Vector3 center, Vector3 size)[] UpperLedges =
        {
            (new Vector3(-29f, -18f, 4f), new Vector3(16f, 3.5f, 20f)),   // 0 west
            (new Vector3(8f, -30f, 26f), new Vector3(22f, 3.5f, 13f)),    // 1 north
            (new Vector3(28f, -40f, -6f), new Vector3(15f, 3.5f, 18f)),   // 2 east
            (new Vector3(-5f, -46f, -26f), new Vector3(20f, 3.5f, 13f)),  // 3 south
        };

        /// <summary>A point on top of upper ledge <paramref name="index"/>, offset from its middle.</summary>
        static Vector3 OnLedge(int index, float dx, float dz, float lift = 0f)
        {
            var c = UpperLedges[index].center;
            return OnGround(c.x + dx, c.z + dz, c.y, lift);
        }

        static void BuildUpperShaft(Transform t)
        {
            for (int i = 0; i < UpperLedges.Length; i++) Slab("Ledge" + i, t, UpperLedges[i].center, UpperLedges[i].size);

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

        /// <summary>A cave running from a wall's mouth into the rock along <paramref name="into"/>, wandering a bit.</summary>
        static void Tunnel(string name, Transform t, Vector3 mouth, Vector3 into, float radius, float length, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 side = Vector3.Cross(into, Vector3.up).normalized;
            var path = new Vector3[5];
            path[0] = mouth - into * 1.5f; // start just outside so the mouth overlaps the wall
            for (int i = 1; i < path.Length; i++)
                path[i] = mouth + into * (length * i / (path.Length - 1)) + side * R(-3f, 3f) + Vector3.up * R(-1f, 1f);
            CaveTunnel(name, t, path, radius, seed, rockWall);
        }

        static void OldRope(Transform t, Vector3 start, int segments, int seed)
        {
            var rng = new System.Random(seed);
            Vector3 p = start;
            for (int i = 0; i < segments; i++)
            {
                Vector3 next = p + new Vector3(0.6f + (float)rng.NextDouble() * 0.5f, 0f, (float)rng.NextDouble() * 1.2f - 0.6f);
                var seg = Cylinder("OldTether", t, (p + next) * 0.5f, new Vector3(0.05f, (next - p).magnitude * 0.5f, 0.05f), rope);
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, next - p);
                p = next;
            }
        }

        // ------------------------------------------------------------------ zone 2: the wall

        static void BuildWallZone(Transform t)
        {
            // Ledges below the station, and things living on them.
            float s = Station2.y;
            Slab("Ledge_Coral", t, new Vector3(-29f, s - 22f, -8f), new Vector3(15f, 3f, 16f));
            Slab("Ledge_Beacon", t, new Vector3(12f, s - 36f, 26f), new Vector3(18f, 3f, 13f));
            Slab("Ledge_Deep", t, new Vector3(26f, s - 48f, 10f), new Vector3(14f, 3f, 14f));
            CoralLedge(t, 11, OnGround(-29f, -8f, s - 22f));
            SurveyBeacon(t, 14, OnGround(12f, 26f, s - 36f));

            // Inside the caves: crystals in the east one, glow-worms in the south one.
            CrystalVein(t, 12, new Vector3(WallX + 10f, s - 31f, 45f - 52f), 90f);
            GlowGrotto(t, 13, new Vector3(-45f + 55f, s - 41f, -WallZ - 7f));

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
            Stencil("TEAM 7", root, new Vector3(0f, 0.6f, -0.16f), Quaternion.identity, 0.08f, Color.black);
            PointLight("BeaconGlow", root, new Vector3(0f, 1.3f, 0f), new Color(1f, 0.15f, 0.1f), 1.2f, 4f, false);
            MakeScannable(root.gameObject, id, "SURVEY BEACON: TEAM 7", DataCategory.Evidence, 150, 4f, 1.5f);
        }

        static void GlowGrotto(Transform t, int id, Vector3 pos)
        {
            var root = Group("GlowWormGrotto", t);
            root.position = pos;
            var rng = new System.Random(id);
            var dots = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 70; i++)
            {
                var offset = RandomDir(rng) * 5f;
                offset.y = Mathf.Abs(offset.y) * 0.6f + 1f;
                var dot = Sphere("Glow", root, offset, 0.06f, bioLight);
                dots.Add(dot.GetComponent<Renderer>());
            }
            var glow = PointLight("GrottoGlow", root, new Vector3(0f, 2f, 0f), new Color(0.3f, 0.9f, 1f), 3f, 8f, false);
            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", dots.ToArray());
            Assign(pulse, "lamp", glow);
            Assign(pulse, "seconds", 5f);
            MakeScannable(root.gameObject, id, "GLOW-WORM GROTTO", DataCategory.Creature, 130, 5f, 3f);
        }

        static Vector3 RandomDir(System.Random rng) =>
            new((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

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
            Assign(drift, "radius", 20f);
            Assign(drift, "secondsPerLap", 150f);
            Assign(drift, "bobHeight", 10f);
            Assign(drift, "bobSeconds", 40f);
            MakeScannable(root.gameObject, id, "UNKNOWN LARGE ORGANISM", DataCategory.Creature, 300, 8f, 12f);
        }

        // ------------------------------------------------------------------ zone 3: the abyssal terrace

        static void BuildTerrace(Transform t)
        {
            // A huge shelf of rock sticking out of the west side of the shaft, 30 m under the station.
            Heightfield("Terrace_Top", t, new Vector3(-22f, TerraceY, 0f), 32f, 62f, 2f, 1.2f, 0.06f, 11, deepSilt);
            Slab("Terrace_Rock", t, new Vector3(-22f, TerraceY - 9f, 0f), new Vector3(38f, 14f, 68f));

            float g = TerraceY;
            WhaleFall(t, 16, OnGround(-22f, -12f, g));
            ThermalVent(t, 17, OnGround(-26f, 18f, g), "BLACK SMOKER FIELD", 140);
            ThermalVent(t, 0, OnGround(-20f, 24f, g));
            ThermalVent(t, 0, OnGround(-31f, 25f, g));
            TubeWormForest(t, 18, OnGround(-24f, 21f, g));
            Isopod(t, 19, OnGround(-14f, 6f, g, 0.2f), OnGround(-12f, -4f, g, 0.2f));
            // Crystals in the north cave at terrace level.
            CrystalVein(t, 20, new Vector3(45f - 62f, TerraceY + 5f, WallZ + 10f), 200f);
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
                Box("Vertebra", root, new Vector3(0f, 0.5f, z), new Vector3(size, size * 0.8f, 0.5f), bone, collider: false, worldUV: false);
                if (i >= 4 && i < 16 && i % 2 == 0)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var rib = Cylinder("Rib", root, new Vector3(side * 1.6f, 1.6f, z), new Vector3(0.18f, 2.2f, 0.18f), bone);
                        rib.transform.localRotation = Quaternion.Euler(0f, 0f, side * 35f);
                    }
                }
            }
            Primitive(PrimitiveType.Sphere, "Skull", root, new Vector3(0f, 1f, 15f), new Vector3(3.2f, 1.8f, 4.5f), bone, collider: true);
            MakeScannable(root.gameObject, id, "WHALE FALL", DataCategory.Creature, 160, 6f, 8f);
        }

        static void TubeWormForest(Transform t, int id, Vector3 pos)
        {
            var root = Group("TubeWormForest", t);
            root.position = pos;
            var red = Mat("POI_WormPlume", null, new Color(0.8f, 0.08f, 0.06f), emission: new Color(0.15f, 0.0f, 0.0f));
            var rng = new System.Random(id);
            for (int i = 0; i < 60; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 7f;
                float h = 0.8f + (float)rng.NextDouble() * 1.8f;
                var tube = Cylinder("Tube", root, offset + Vector3.up * h * 0.5f, new Vector3(0.1f, h * 0.5f, 0.1f), bone);
                tube.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, 0, (float)rng.NextDouble() * 16f - 8f);
                Sphere("Plume", root, offset + Vector3.up * (h + 0.05f), 0.2f, red);
            }
            MakeScannable(root.gameObject, id, "TUBE WORM FOREST", DataCategory.Creature, 120, 4f, 4f);
        }

        // ------------------------------------------------------------------ zone 4: the shaft floor

        static void BuildShaftFloor(Transform t)
        {
            Heightfield("Shaft_Floor", t, new Vector3(0f, FloorY, 0f), 84f, 76f, 2f, 1.2f, 0.06f, 21, deepSilt, (x, z) =>
            {
                // Rubble piled up against the walls.
                float nearX = Mathf.Max(0f, Mathf.Abs(x) - 30f), nearZ = Mathf.Max(0f, Mathf.Abs(z) - 26f);
                return (nearX * nearX + nearZ * nearZ) * 0.08f;
            });

            float g = FloorY;
            DivingBellWreck(t, 21, OnGround(-12f, 8f, g));
            StructureCluster(t, 22, OnGround(14f, -6f, g));
            // Dive Team 7. Their bodies (and camera chips) are added by the footage step.
            lostDiverSpots = new[] { OnGround(-18f, -4f, g), OnGround(-4f, 14f, g), OnGround(6f, -16f, g), OnGround(22f, 8f, g) };
            OldRope(t, OnGround(-15f, 5f, g, 0.05f), 12, 71);
            OldRope(t, OnGround(0f, -12f, g, 0.05f), 9, 72);
        }

        static void DivingBellWreck(Transform t, int id, Vector3 pos)
        {
            var root = Group("DivingBellWreck", t);
            root.position = pos;
            root.rotation = Quaternion.Euler(12f, 40f, 25f);
            var shell = Mat("POI_BellShell", ProceduralTextures.HullExterior(), new Color(0.55f, 0.45f, 0.4f), metallic: 0.3f);
            Primitive(PrimitiveType.Sphere, "Bell", root, new Vector3(0f, 1.3f, 0f), new Vector3(2.6f, 2.8f, 2.6f), shell, collider: true);
            Cylinder("Window", root, new Vector3(0f, 1.5f, -1.28f), new Vector3(0.6f, 0.03f, 0.6f), glassDark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                var leg = Cylinder("Frame", root, new Vector3(Mathf.Cos(a) * 1.4f, 0.4f, Mathf.Sin(a) * 1.4f), new Vector3(0.12f, 0.9f, 0.12f), rust);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, i == 2 ? 70f : 10f); // one bent flat
            }
            Stencil("TEAM 7 - BELL 2", root, new Vector3(0f, 2.2f, -1.2f), Quaternion.identity, 0.18f, new Color(0.9f, 0.85f, 0.7f));
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
            PointLight("ClusterGlow", root, new Vector3(0f, 3f, 0f), new Color(0.1f, 0.9f, 0.8f), 2f, 12f, false);
            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", glyphs.ToArray());
            Assign(pulse, "seconds", 9f);
            MakeScannable(root.gameObject, id, "STRUCTURE CLUSTER (ORIGIN UNKNOWN)", DataCategory.Structure, 300, 8f, 8f);
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
                ("Upper Shaft", 0, 0.55f, new Color(0.01f, 0.035f, 0.045f), 0.04f),
                ("The Wall", 1, 0.25f, new Color(0.006f, 0.022f, 0.032f), 0.04f),
                ("Abyssal Terrace", 2, 0.09f, new Color(0.004f, 0.012f, 0.02f), 0.045f),
                ("Shaft Floor", 3, 0.02f, new Color(0.001f, 0.004f, 0.008f), 0.05f),
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
