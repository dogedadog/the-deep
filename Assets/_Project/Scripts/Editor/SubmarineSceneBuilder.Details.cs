using TheDeep.Submarine;
using UnityEditor;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>Interior atmosphere: wet floor, drips, dust, safety gear, hanging cables, signs, sound.</summary>
    public static partial class SubmarineSceneBuilder
    {
        static void BuildInteriorDetails(Transform t)
        {
            var water = Mat("Sub_Water", null, new Color(0.02f, 0.03f, 0.035f), metallic: 0.1f, smoothness: 0.95f);
            var white = Mat("Sub_WhitePaint", null, new Color(0.75f, 0.74f, 0.7f), metallic: 0.2f);
            var dust = ParticleMat("P_Dust", false);

            // Puddles around the dive hatch: divers come back in dripping.
            Puddle(t, new Vector3(-2.6f, 0.013f, 0.85f), new Vector2(0.75f, 0.5f), 20f, water);
            Puddle(t, new Vector3(-4.35f, 0.013f, 0.3f), new Vector2(0.55f, 0.75f), -10f, water);
            Puddle(t, new Vector3(-2.75f, 0.013f, -0.45f), new Vector2(0.4f, 0.3f), 45f, water);
            Drip(t, new Vector3(-2.6f, 2.24f, 0.95f), dust);
            Drip(t, new Vector3(-4.3f, 2.45f, 0.35f), dust);

            // Dust hanging in the air, only visible where the lamps catch it.
            var motes = Particles("DustMotes", t, new Vector3(0, 1.3f, 0), dust);
            var main = motes.main;
            main.startLifetime = 14f;
            main.startSpeed = 0.02f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.startColor = new Color(0.9f, 0.85f, 0.7f);
            main.maxParticles = 250;
            var emission = motes.emission;
            emission.rateOverTime = 16f;
            var shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(9.4f, 2.3f, 2.9f);
            var noise = motes.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;

            // Fire extinguisher by the terminal.
            Box("ExtinguisherBracket", t, new Vector3(1.55f, 0.55f, 1.56f), new Vector3(0.18f, 0.06f, 0.06f), rubber, collider: false, worldUV: false);
            Cylinder("Extinguisher", t, new Vector3(1.55f, 0.5f, 1.47f), new Vector3(0.15f, 0.26f, 0.15f), redPaint);
            Cylinder("ExtinguisherHead", t, new Vector3(1.55f, 0.8f, 1.47f), new Vector3(0.05f, 0.05f, 0.05f), rubber);
            Box("ExtinguisherHose", t, new Vector3(1.6f, 0.62f, 1.4f), new Vector3(0.02f, 0.3f, 0.02f), rubber, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 0, -15);

            // First aid box on the starboard wall.
            Box("FirstAid", t, new Vector3(-1.75f, 1.5f, -1.53f), new Vector3(0.36f, 0.28f, 0.1f), white, collider: false, worldUV: false);
            Box("FirstAidCrossH", t, new Vector3(-1.75f, 1.5f, -1.477f), new Vector3(0.12f, 0.04f, 0.005f), redPaint, collider: false, worldUV: false);
            Box("FirstAidCrossV", t, new Vector3(-1.75f, 1.5f, -1.477f), new Vector3(0.04f, 0.12f, 0.005f), redPaint, collider: false, worldUV: false);

            // A wetsuit hanging next to the lockers.
            Box("SuitHook", t, new Vector3(-2.7f, 1.95f, 1.55f), new Vector3(0.05f, 0.05f, 0.1f), brass, collider: false, worldUV: false);
            Capsule("Wetsuit_Body", t, new Vector3(-2.7f, 1.3f, 1.47f), new Vector3(0.36f, 0.55f, 0.12f), Quaternion.identity);
            Capsule("Wetsuit_ArmL", t, new Vector3(-2.93f, 1.42f, 1.47f), new Vector3(0.09f, 0.3f, 0.09f), Quaternion.Euler(0, 0, -10));
            Capsule("Wetsuit_ArmR", t, new Vector3(-2.47f, 1.42f, 1.47f), new Vector3(0.09f, 0.3f, 0.09f), Quaternion.Euler(0, 0, 10));
            Box("Wetsuit_Stripe", t, new Vector3(-2.7f, 1.45f, 1.405f), new Vector3(0.3f, 0.03f, 0.01f), yellowTank, collider: false, worldUV: false);

            // Sagging cables between ceiling brackets.
            SaggingCable(t, new Vector3(0.6f, 2.5f, -0.55f), new Vector3(2.0f, 2.5f, -0.55f), 0.28f);
            SaggingCable(t, new Vector3(-1.6f, 2.5f, -0.4f), new Vector3(-0.4f, 2.5f, -0.6f), 0.2f);

            // Amber beacon above the dive hatch (will spin when the hatch is open).
            Cylinder("HatchBeaconBase", t, new Vector3(-3.4f, 2.56f, 0.9f), new Vector3(0.14f, 0.03f, 0.14f), rubber);
            Sphere("HatchBeacon", t, new Vector3(-3.4f, 2.5f, 0.9f), 0.12f,
                Mat("Sub_BeaconOff", null, new Color(0.45f, 0.25f, 0.02f), smoothness: 0.8f));

            // Signs.
            Stencil("CHECK YOUR TETHER", t, new Vector3(-3.3f, 1.95f, -1.575f), Quaternion.Euler(0, 180, 0), 0.06f, new Color(0.95f, 0.8f, 0.1f));
            Stencil("NO DIVER LEAVES ALONE", t, new Vector3(-3.3f, 1.85f, -1.575f), Quaternion.Euler(0, 180, 0), 0.04f, new Color(0.9f, 0.9f, 0.85f));
            Stencil("RATED 4000 M", t, new Vector3(4.97f, 2.2f, 0.1f), Quaternion.Euler(0, 90, 0), 0.06f, new Color(0.9f, 0.85f, 0.7f));
            Stencil("FIRE", t, new Vector3(1.55f, 0.9f, 1.575f), Quaternion.identity, 0.05f, new Color(0.9f, 0.2f, 0.15f));

            // Background sound: hum, creaks, drips.
            var ambience = new GameObject("Ambience", typeof(AudioSource), typeof(SubAmbience));
            ambience.transform.SetParent(t, false);
        }

        static void Puddle(Transform t, Vector3 pos, Vector2 size, float angle, Material water)
        {
            Cylinder("Puddle", t, pos, new Vector3(size.x, 0.002f, size.y), water)
                .transform.localRotation = Quaternion.Euler(0, angle, 0);
        }

        static void Drip(Transform t, Vector3 from, Material mat)
        {
            var ps = Particles("Drip", t, from, mat);
            var main = ps.main;
            main.startLifetime = Mathf.Sqrt(2f * from.y / 9.81f); // lands on the floor
            main.startSpeed = 0f;
            main.gravityModifier = 1f;
            main.startSize = 0.02f;
            main.startColor = new Color(0.6f, 0.75f, 0.8f);
            main.maxParticles = 4;
            var emission = ps.emission;
            emission.rateOverTime = 0.5f;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.03f;
        }

        static void SaggingCable(Transform t, Vector3 a, Vector3 b, float sag)
        {
            Vector3 mid = (a + b) * 0.5f + Vector3.down * sag;
            CableSegment(t, a, mid);
            CableSegment(t, mid, b);
        }

        static void CableSegment(Transform t, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            var seg = Cylinder("Cable", t, (a + b) * 0.5f, new Vector3(0.025f, d.magnitude * 0.5f, 0.025f), rubber);
            seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d);
        }

        static void Capsule(string name, Transform t, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = Primitive(PrimitiveType.Capsule, name, t, pos, scale, rubber, collider: false);
            go.transform.localRotation = rot;
        }

        static ParticleSystem Particles(string name, Transform parent, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            // A speck right in front of the camera shouldn't balloon into a big square.
            renderer.maxParticleSize = 0.01f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = 0f;
            return ps;
        }

        /// <summary>Opaque particle material; lit ones only show up where lights hit them.</summary>
        static Material ParticleMat(string name, bool unlit)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(unlit
                    ? "Universal Render Pipeline/Particles/Unlit"
                    : "Universal Render Pipeline/Particles/Simple Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
