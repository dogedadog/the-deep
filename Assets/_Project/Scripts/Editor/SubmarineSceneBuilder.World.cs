using TheDeep.Creatures;
using TheDeep.Data;
using TheDeep.Submarine;
using UnityEditor;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// The dive site: a canyon that falls away in stages. The sub descends between four stations:
    ///   1 THE SHELF (1280 m)        - silt shelf between rock walls with caves, ending at a sheer drop.
    ///   2 THE WALL (1450 m)         - open black water against the cliff under the shelf. No floor in sight.
    ///   3 ABYSSAL PLAIN (1800 m)    - wide dark plain: a whale fall, black smokers, worm forests.
    ///   4 HADAL TRENCH (2420 m)     - a narrow slot at the bottom. Dive Team 7 is here.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        static readonly Vector3 Station2 = new(100f, -170f, 0f);
        static readonly Vector3 Station3 = new(180f, -520f, 0f);
        static readonly Vector3 Station4 = new(262f, -1140f, 0f);
        const float AbyssFloorY = -562f;
        const float TrenchFloorY = -1205f;

        static Transform[] stationPoints;
        static Light downwellingLight;
        static Material rockWall, silt2, deepSilt, leviathanSkin, bioLight, beaconYellow;

        static void BuildWorld(Transform t, Material rockMat, Material siltMat, Light downwelling)
        {
            InitPoiMaterials();
            downwellingLight = downwelling;
            rockWall = Mat("Env_RockWall", ProceduralTextures.Rock(), new Color(1.6f, 1.6f, 1.65f), smoothness: 0.2f); // texture is dark; brighten so lit rock reads
            silt2 = siltMat;
            deepSilt = Mat("Env_DeepSilt", ProceduralTextures.Silt(), new Color(0.6f, 0.6f, 0.62f), smoothness: 0.05f);
            leviathanSkin = Mat("Creature_Leviathan", null, new Color(0.03f, 0.035f, 0.04f), smoothness: 0.6f);
            bioLight = Mat("Creature_Biolight", null, new Color(0.4f, 0.9f, 1f), emission: new Color(0.5f, 1.6f, 2f));
            beaconYellow = Mat("POI_BeaconYellow", null, new Color(0.8f, 0.6f, 0.08f), metallic: 0.3f, smoothness: 0.4f);

            BuildShelf(Group("Zone1_Shelf", t), rockMat);
            BuildWallZone(Group("Zone2_Wall", t));
            BuildAbyss(Group("Zone3_Abyss", t));
            BuildTrench(Group("Zone4_Trench", t));

            var stations = Group("DiveStations", t);
            stationPoints = new[]
            {
                StationPoint(stations, "Station1_Shelf", Vector3.zero),
                StationPoint(stations, "Station2_Wall", Station2),
                StationPoint(stations, "Station3_Abyss", Station3),
                StationPoint(stations, "Station4_Trench", Station4),
            };
        }

        static Transform StationPoint(Transform parent, string name, Vector3 pos)
        {
            var p = Group(name, parent);
            p.position = pos;
            return p;
        }

        // ------------------------------------------------------------------ zone 1: the shelf

        static void BuildShelf(Transform t, Material rockMat)
        {
            // Silt shelf that rises toward the canyon walls and dips into a lip before the drop-off at x = 70.
            Heightfield("S1_Floor", t, new Vector3(-7.5f, SeafloorY - 0.3f, 0f), 155f, 92f, 2.5f, 1.3f, 0.05f, 1, silt2, (x, z) =>
            {
                float nearWall = Mathf.Max(0f, Mathf.Abs(z) - 30f);
                float lip = x > 60f ? -(x - 60f) * 0.35f : 0f;
                return nearWall * nearWall * 0.06f + lip;
            });

            // Canyon walls with caves.
            var north = new Vector3(-85f, SeafloorY - 8f, 44f);
            CliffWall("S1_WallNorth", t, north, Vector3.right, Vector3.back, 155f, 85f, 3f, 3f, 2, rockWall,
                new CaveMouth { U = 40f, V = 9f, Radius = 3.4f }, new CaveMouth { U = 115f, V = 11f, Radius = 3.8f });
            Tunnel("S1_CaveN1", t, WallPoint(north, Vector3.right, 40f, 9f), Vector3.forward, 3.4f, 24f, 3);
            Tunnel("S1_CaveN2", t, WallPoint(north, Vector3.right, 115f, 11f), Vector3.forward, 3.8f, 30f, 4);

            var south = new Vector3(-85f, SeafloorY - 8f, -44f);
            CliffWall("S1_WallSouth", t, south, Vector3.right, Vector3.forward, 155f, 85f, 3f, 3f, 5, rockWall,
                new CaveMouth { U = 75f, V = 8f, Radius = 3.2f }, new CaveMouth { U = 135f, V = 10f, Radius = 3.6f });
            Tunnel("S1_CaveS1", t, WallPoint(south, Vector3.right, 75f, 8f), Vector3.back, 3.2f, 22f, 6);
            Tunnel("S1_CaveS2", t, WallPoint(south, Vector3.right, 135f, 10f), Vector3.back, 3.6f, 26f, 7);

            // Back of the canyon, and the sheer drop-off at the far end that the sub descends past.
            CliffWall("S1_WallBack", t, new Vector3(-85f, SeafloorY - 8f, -46f), Vector3.forward, Vector3.right, 92f, 85f, 3f, 3f, 8, rockWall);
            var edge = new Vector3(70f, -440f, -48f);
            CliffWall("S1_DropOff", t, edge, Vector3.forward, Vector3.right, 96f, 431f, 4f, 5f, 9, rockWall,
                new CaveMouth { U = 60f, V = 275f, Radius = 4.5f }, new CaveMouth { U = 33f, V = 250f, Radius = 3.8f });
            Tunnel("S2_CaveA", t, WallPoint(edge, Vector3.forward, 60f, 275f), Vector3.left, 4.5f, 28f, 10);
            Tunnel("S2_CaveB", t, WallPoint(edge, Vector3.forward, 33f, 250f), Vector3.left, 3.8f, 24f, 11);

            // Boulders on the shelf, away from the sub and the walls.
            var rng = new System.Random(5);
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            for (int i = 0; i < 55; i++)
            {
                float x = R(-75f, 58f), z = R(-33f, 33f);
                if (new Vector2(x, z).magnitude < 11f) continue;
                var scale = new Vector3(R(1.5f, 7f), R(0.8f, 4f), R(1.5f, 7f));
                var boulder = Primitive(PrimitiveType.Sphere, "Boulder", t, OnGround(x, z, SeafloorY, scale.y * 0.15f), scale, rockMat, collider: true);
                boulder.transform.localRotation = Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15));
            }

            // Something on the seabed that shouldn't be there: an old dive helmet and a snapped tether.
            var helmetMat = Mat("Env_OldBrass", null, new Color(0.3f, 0.26f, 0.15f), metallic: 0.6f, smoothness: 0.2f);
            Sphere("OldDiveHelmet", t, OnGround(9f, 7f, SeafloorY, 0.15f), 0.4f, helmetMat);
            OldRope(t, OnGround(9.4f, 7.2f, SeafloorY, 0.04f), 7, 55);
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
                path[i] = mouth + into * (length * i / (path.Length - 1)) + side * R(-3f, 3f) + Vector3.up * R(-1.5f, 1.5f);
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
            // Ledges sticking out of the cliff under the shelf, and things living on them.
            Ledge(t, new Vector3(72.5f, -160f, -6f), 5f);
            Ledge(t, new Vector3(72.5f, -182f, 18f), 4f);
            Ledge(t, new Vector3(72.5f, -205f, 2f), 6f);
            CoralLedge(t, 11, new Vector3(74f, -159.3f, -6f));
            SurveyBeacon(t, 14, new Vector3(74f, -181.4f, 18f));

            // Inside the caves: crystals in one, glow-worms in the other.
            CrystalVein(t, 12, new Vector3(52f, -166f, 13f), 90f);
            GlowGrotto(t, 13, new Vector3(51f, -190f, -14f));

            // Something enormous circling out in the dark.
            Leviathan(t, 15, Station2 + new Vector3(0f, -12f, 0f));
        }

        static void Ledge(Transform t, Vector3 pos, float size)
        {
            var ledge = Primitive(PrimitiveType.Sphere, "Ledge", t, pos, new Vector3(size * 0.7f, 1.2f, size), rockWall, collider: true);
            ledge.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        }

        static void CoralLedge(Transform t, int id, Vector3 pos)
        {
            var root = Group("DeepCoral", t);
            root.position = pos;
            var coral = Mat("POI_Coral", null, new Color(0.9f, 0.45f, 0.3f), smoothness: 0.3f, emission: new Color(0.08f, 0.02f, 0.01f));
            var rng = new System.Random(id);
            for (int i = 0; i < 16; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() * 2f, 0f, (float)rng.NextDouble() * 3f - 1.5f);
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

        /// <summary>A ~35 m shape that circles the station at the edge of visibility, dotted with faint lights.</summary>
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
            Assign(drift, "radius", 44f);
            Assign(drift, "secondsPerLap", 240f);
            Assign(drift, "bobHeight", 4f);
            Assign(drift, "bobSeconds", 30f);
            MakeScannable(root.gameObject, id, "UNKNOWN LARGE ORGANISM", DataCategory.Creature, 300, 8f, 12f);
        }

        // ------------------------------------------------------------------ zone 3: the abyssal plain

        static void BuildAbyss(Transform t)
        {
            Heightfield("S3_Floor", t, new Vector3(185f, AbyssFloorY, 0f), 170f, 124f, 3f, 3f, 0.03f, 11, deepSilt, (x, z) =>
            {
                float nearWall = Mathf.Max(0f, Mathf.Abs(z) - 45f);
                return nearWall * nearWall * 0.05f;
            });
            var north = new Vector3(100f, AbyssFloorY - 8f, 62f);
            CliffWall("S3_WallNorth", t, north, Vector3.right, Vector3.back, 170f, 120f, 3.5f, 4f, 12, rockWall,
                new CaveMouth { U = 50f, V = 14f, Radius = 4.5f });
            Tunnel("S3_Cave", t, WallPoint(north, Vector3.right, 50f, 14f), Vector3.forward, 4.5f, 30f, 13);
            CliffWall("S3_WallSouth", t, new Vector3(100f, AbyssFloorY - 8f, -62f), Vector3.right, Vector3.forward, 170f, 120f, 3.5f, 4f, 14, rockWall);

            float g = AbyssFloorY;
            WhaleFall(t, 16, OnGround(168f, -16f, g));
            ThermalVent(t, 17, OnGround(205f, 18f, g), "BLACK SMOKER FIELD", 140);
            ThermalVent(t, 0, OnGround(213f, 26f, g));
            ThermalVent(t, 0, OnGround(199f, 29f, g));
            TubeWormForest(t, 18, OnGround(208f, 22f, g));
            Isopod(t, 19, OnGround(160f, 10f, g, 0.2f), OnGround(172f, 22f, g, 0.2f));
            CrystalVein(t, 20, new Vector3(150f, AbyssFloorY + 4f, 84f), 200f);
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

        // ------------------------------------------------------------------ zone 4: the hadal trench

        static void BuildTrench(Transform t)
        {
            Heightfield("S4_Floor", t, new Vector3(265f, TrenchFloorY, 0f), 150f, 40f, 2f, 1.2f, 0.06f, 21, deepSilt, (x, z) =>
            {
                float nearWall = Mathf.Max(0f, Mathf.Abs(z) - 12f);
                return nearWall * nearWall * 0.3f;
            });
            var north = new Vector3(190f, TrenchFloorY - 6f, 18f);
            CliffWall("S4_WallNorth", t, north, Vector3.right, Vector3.back, 150f, 170f, 3f, 3f, 22, rockWall,
                new CaveMouth { U = 88f, V = 9f, Radius = 3.5f });
            Tunnel("S4_Cave", t, WallPoint(north, Vector3.right, 88f, 9f), Vector3.forward, 3.5f, 26f, 23);
            CliffWall("S4_WallSouth", t, new Vector3(190f, TrenchFloorY - 6f, -18f), Vector3.right, Vector3.forward, 150f, 170f, 3f, 3f, 24, rockWall);
            CliffWall("S4_WallEnd", t, new Vector3(340f, TrenchFloorY - 6f, 20f), Vector3.back, Vector3.left, 40f, 170f, 3f, 3f, 25, rockWall);
            CliffWall("S4_WallStart", t, new Vector3(190f, TrenchFloorY - 6f, -20f), Vector3.forward, Vector3.right, 40f, 170f, 3f, 3f, 26, rockWall);

            float g = TrenchFloorY;
            DivingBellWreck(t, 21, OnGround(248f, 5f, g));
            StructureCluster(t, 22, OnGround(292f, 0f, g));
            // Dive Team 7. Their bodies (and camera chips) are added by the footage step.
            lostDiverSpots = new[] { OnGround(242f, -7f, g), OnGround(257f, 9f, g), OnGround(276f, -5f, g), OnGround(300f, 7f, g) };
            OldRope(t, OnGround(244f, 4f, g, 0.05f), 12, 71);
            OldRope(t, OnGround(270f, -8f, g, 0.05f), 9, 72);
        }

        static Vector3[] lostDiverSpots;

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
                ("The Shelf", 0, 0.55f, new Color(0.01f, 0.035f, 0.045f), 0.045f),
                ("The Wall", 1, 0.28f, new Color(0.006f, 0.022f, 0.032f), 0.042f),
                ("Abyssal Plain", 2, 0.1f, new Color(0.004f, 0.012f, 0.02f), 0.05f),
                ("Hadal Trench", 3, 0.02f, new Color(0.001f, 0.004f, 0.008f), 0.058f),
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
