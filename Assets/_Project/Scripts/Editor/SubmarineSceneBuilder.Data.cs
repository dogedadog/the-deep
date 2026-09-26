using TheDeep.Creatures;
using TheDeep.Data;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// Step 3 content: the shared expedition record and things on the seafloor worth scanning.
    /// Creatures here are placeholders with simple motion until the creature AI step.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        static Material rock, crystal, ventGlow, bone, jelly, isopodShell, monolithGlyph, brine;

        static void BuildExpeditionState()
        {
            // Current expedition (resets on surfacing) + permanent crew progress (saved), one network object.
            var go = new GameObject("ExpeditionState", typeof(NetworkObject), typeof(ExpeditionState), typeof(TheDeep.Progression.CrewProgress), typeof(TheDeep.Footage.FootageArchive));
            go.transform.position = new Vector3(0, 1.3f, 0);
            BuildNavigation(go);
        }

        /// <summary>Materials shared by the point-of-interest builders (safe to call more than once).</summary>
        static void InitPoiMaterials()
        {
            rock = AssetDatabaseMat("Env_Rock");
            crystal = Mat("POI_Crystal", null, new Color(0.3f, 0.9f, 1f), smoothness: 0.9f, emission: new Color(0.25f, 1.1f, 1.4f));
            ventGlow = Mat("POI_VentGlow", null, new Color(1f, 0.45f, 0.1f), emission: new Color(2.2f, 0.8f, 0.15f));
            bone = Mat("POI_Bone", null, new Color(0.82f, 0.8f, 0.72f), smoothness: 0.3f);
            jelly = Mat("POI_Jelly", null, new Color(0.55f, 0.4f, 0.9f), smoothness: 0.8f, emission: new Color(0.35f, 0.3f, 1.2f));
            isopodShell = Mat("POI_Isopod", null, new Color(0.62f, 0.58f, 0.62f), metallic: 0.2f, smoothness: 0.5f);
            monolithGlyph = Mat("POI_Glyph", null, new Color(0.1f, 0.3f, 0.3f), emission: new Color(0.1f, 0.9f, 0.8f));
            brine = Mat("POI_Brine", null, new Color(0.01f, 0.02f, 0.025f), smoothness: 1f, emission: new Color(0.0f, 0.05f, 0.06f));
        }

        /// <summary>Station 1 (the shelf): the first things worth documenting, near the sub.</summary>
        static void BuildPointsOfInterest(Transform t)
        {
            InitPoiMaterials();
            float floor = SeafloorY;
            ThermalVent(t, 1, OnGround(14f, -8f, floor));
            TubeWorms(t, 8, OnGround(15.8f, -6.4f, floor));
            CrystalVein(t, 2, OnGround(-14f, 10f, floor), 20f);
            CrystalVein(t, 3, OnGround(22f, 12f, floor), 140f);
            CrystalVein(t, 4, OnGround(-30f, -6f, floor), 260f);
            BrinePool(t, 5, OnGround(-8f, -18f, floor));
            Jellyfish(t, 6, new Vector3(6f, -4.5f, 12f));
            Isopod(t, 7, OnGround(-20f, -7f, floor, 0.2f), OnGround(-13f, -2f, floor, 0.2f));
            Monolith(t, 9, OnGround(30f, -2f, floor));

            // The old dive helmet on the seabed is evidence about the missing team.
            var helmet = GameObject.Find("OldDiveHelmet");
            if (helmet != null) MakeScannable(helmet, 10, "PERSONAL EFFECTS: DIVE HELMET", DataCategory.Evidence, 110, 4f, 1.2f);
        }

        static Material AssetDatabaseMat(string name) =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");

        static ScanTarget MakeScannable(GameObject go, int id, string title, DataCategory category, int value, float seconds, float radius)
        {
            var target = go.AddComponent<ScanTarget>();
            Assign(target, "id", id);
            Assign(target, "title", title);
            Assign(target, "category", (int)category);
            Assign(target, "value", value);
            Assign(target, "scanSeconds", seconds);
            // Generous invisible scan volume so small or moving things are easy to keep in the beam.
            var zone = go.AddComponent<SphereCollider>();
            zone.isTrigger = true;
            zone.radius = radius / Mathf.Max(go.transform.lossyScale.x, 0.01f);
            return target;
        }

        /// <summary>Rock chimney with a glowing mouth and bubbles. id 0 = scenery only (not scannable).</summary>
        static void ThermalVent(Transform t, int id, Vector3 pos, string title = "HYDROTHERMAL VENT", int value = 60)
        {
            var root = Group("ThermalVent", t);
            root.localPosition = pos;
            // Stacked rock chimney narrowing to a glowing mouth.
            float y = 0f;
            float[] radii = { 1.6f, 1.2f, 0.9f, 0.65f };
            foreach (float r in radii)
            {
                Primitive(PrimitiveType.Cylinder, "Chimney", root, new Vector3(0, y + 0.5f, 0), new Vector3(r * 2, 0.5f, r * 2), rock, collider: true);
                y += 1f;
            }
            Cylinder("VentMouth", root, new Vector3(0, y + 0.02f, 0), new Vector3(0.9f, 0.03f, 0.9f), ventGlow);
            PointLight("VentGlow", root, new Vector3(0, y + 0.6f, 0), new Color(1f, 0.45f, 0.15f), 6f, 9f, false);

            var bubbles = Particles("VentBubbles", root, new Vector3(0, y + 0.1f, 0), ParticleMat("P_Bubbles", false));
            var main = bubbles.main;
            main.startLifetime = 7f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new Color(0.8f, 0.9f, 1f);
            main.maxParticles = 300;
            var emission = bubbles.emission;
            emission.rateOverTime = 30f;
            var shape = bubbles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90, 0, 0);
            var noise = bubbles.noise;
            noise.enabled = true;
            noise.strength = 0.3f;

            if (id > 0) MakeScannable(root.gameObject, id, title, DataCategory.Environment, value, 4f, 2.5f);
        }

        static void TubeWorms(Transform t, int id, Vector3 pos)
        {
            var root = Group("TubeWorms", t);
            root.localPosition = pos;
            var red = Mat("POI_WormPlume", null, new Color(0.8f, 0.08f, 0.06f), emission: new Color(0.15f, 0.0f, 0.0f));
            var rng = new System.Random(8);
            for (int i = 0; i < 14; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 1.4f;
                float h = 0.4f + (float)rng.NextDouble() * 0.6f;
                var tube = Cylinder("Tube", root, offset + Vector3.up * h * 0.5f, new Vector3(0.06f, h * 0.5f, 0.06f), bone);
                tube.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 14f - 7f, 0, (float)rng.NextDouble() * 14f - 7f);
                Sphere("Plume", root, offset + Vector3.up * (h + 0.02f), 0.1f, red);
            }
            MakeScannable(root.gameObject, id, "TUBE WORM COLONY", DataCategory.Creature, 70, 3f, 1.3f);
        }

        static void CrystalVein(Transform t, int id, Vector3 pos, float yaw)
        {
            var root = Group("CrystalVein", t);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            Primitive(PrimitiveType.Sphere, "Outcrop", root, new Vector3(0, 0.3f, 0), new Vector3(2.4f, 1.2f, 1.8f), rock, collider: true);
            var rng = new System.Random(id * 31);
            for (int i = 0; i < 7; i++)
            {
                var shard = Box("Crystal", root,
                    new Vector3((float)rng.NextDouble() * 1.4f - 0.7f, 0.7f, (float)rng.NextDouble() * 1f - 0.5f),
                    new Vector3(0.12f, 0.5f + (float)rng.NextDouble() * 0.7f, 0.12f), crystal, collider: false, worldUV: false);
                shard.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 50f - 25f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 50f - 25f);
            }
            PointLight("CrystalGlow", root, new Vector3(0, 1.2f, 0), new Color(0.3f, 0.9f, 1f), 2.5f, 5f, false);
            MakeScannable(root.gameObject, id, "LUMINOUS MINERAL VEIN", DataCategory.Geology, 45, 3f, 1.8f);
        }

        static void BrinePool(Transform t, int id, Vector3 pos)
        {
            var root = Group("BrinePool", t);
            root.localPosition = pos;
            Cylinder("Rim", root, new Vector3(0, 0.05f, 0), new Vector3(5.6f, 0.05f, 4.4f), rock);
            Cylinder("Brine", root, new Vector3(0, 0.1f, 0), new Vector3(5f, 0.01f, 3.8f), brine);
            // Things that swam in and didn't swim out.
            var rng = new System.Random(55);
            for (int i = 0; i < 6; i++)
            {
                var b = Box("Bones", root, new Vector3((float)rng.NextDouble() * 5f - 2.5f, 0.12f, (float)rng.NextDouble() * 4f - 2f),
                    new Vector3(0.5f, 0.04f, 0.04f), bone, collider: false, worldUV: false);
                b.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0);
            }
            MakeScannable(root.gameObject, id, "BRINE POOL (ANOXIC)", DataCategory.Environment, 70, 4f, 2.6f);
        }

        static void Jellyfish(Transform t, int id, Vector3 pos)
        {
            var root = Group("AbyssalJellyfish", t);
            root.localPosition = pos;
            var bell = Primitive(PrimitiveType.Sphere, "Bell", root, Vector3.zero, new Vector3(1.3f, 0.7f, 1.3f), jelly, collider: false);
            var renderers = new System.Collections.Generic.List<Renderer> { bell.GetComponent<Renderer>() };
            var rng = new System.Random(6);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                float len = 1.2f + (float)rng.NextDouble() * 1.4f;
                var tentacle = Cylinder("Tentacle", root, new Vector3(Mathf.Cos(a) * 0.4f, -len * 0.5f, Mathf.Sin(a) * 0.4f),
                    new Vector3(0.03f, len * 0.5f, 0.03f), jelly);
                renderers.Add(tentacle.GetComponent<Renderer>());
            }
            var glow = PointLight("JellyGlow", root, new Vector3(0, -0.3f, 0), new Color(0.5f, 0.45f, 1f), 2f, 6f, false);

            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", renderers.ToArray());
            Assign(pulse, "lamp", glow);
            Assign(root.gameObject.AddComponent<Drifter>(), "radius", 5f);

            MakeScannable(root.gameObject, id, "ABYSSAL JELLYFISH", DataCategory.Creature, 90, 5f, 1.5f);
        }

        static void Isopod(Transform t, int id, Vector3 from, Vector3 to)
        {
            var root = Group("GiantIsopod", t);
            root.localPosition = from;
            var body = Primitive(PrimitiveType.Capsule, "Body", root, Vector3.zero, new Vector3(0.8f, 0.8f, 0.35f), isopodShell, collider: false);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.55f + i * 0.22f;
                Box("Segment", root, new Vector3(0, 0.12f, z), new Vector3(0.78f, 0.05f, 0.06f), isopodShell, collider: false, worldUV: false);
                foreach (float side in new[] { -1f, 1f })
                    Box("Leg", root, new Vector3(side * 0.42f, -0.1f, z), new Vector3(0.18f, 0.03f, 0.03f), isopodShell, collider: false, worldUV: false)
                        .transform.localRotation = Quaternion.Euler(0, 0, side * -30f);
            }
            Sphere("EyeL", root, new Vector3(-0.18f, 0.05f, 0.8f), 0.07f, glassDark);
            Sphere("EyeR", root, new Vector3(0.18f, 0.05f, 0.8f), 0.07f, glassDark);

            var crawl = root.gameObject.AddComponent<Crawler>();
            Assign(crawl, "pointA", from);
            Assign(crawl, "pointB", to);
            Assign(crawl, "speed", 0.2f);
            MakeScannable(root.gameObject, id, "GIANT ISOPOD", DataCategory.Creature, 80, 4f, 1.4f);
        }

        static void Monolith(Transform t, int id, Vector3 pos)
        {
            var root = Group("Monolith", t);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(4f, 25f, -3f);
            Box("Stone", root, new Vector3(0, 3f, 0), new Vector3(1.3f, 6f, 0.9f), rock);
            // Faint carved lines that shouldn't glow.
            var rng = new System.Random(9);
            var glyphs = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 9; i++)
            {
                float y = 1f + i * 0.5f;
                var g = Box("Glyph", root, new Vector3((float)rng.NextDouble() * 0.6f - 0.3f, y, -0.46f),
                    new Vector3(0.2f + (float)rng.NextDouble() * 0.4f, 0.04f, 0.01f), monolithGlyph, collider: false, worldUV: false);
                glyphs.Add(g.GetComponent<Renderer>());
            }
            var pulse = root.gameObject.AddComponent<GlowPulse>();
            AssignArray(pulse, "renderers", glyphs.ToArray());
            Assign(pulse, "seconds", 7f);
            MakeScannable(root.gameObject, id, "UNIDENTIFIED STRUCTURE", DataCategory.Structure, 150, 6f, 3f);
        }
    }
}
