using System.Collections.Generic;
using TheDeep.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// The diver model: a jointed suit (pivots at the neck, shoulders, elbows, hips, knees and ankles)
    /// so <see cref="DiverAnimator"/> can pose it in code. Used by the player and by dead bodies.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        /// <summary>The joints and renderers of one built diver.</summary>
        class DiverRig
        {
            public Transform Root, Hips, Neck, ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR, HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
            public GameObject FinL, FinR, Scanner, HelmetCamLight, RadioLight, HeadlampHousing;
            public Renderer ScannerLens;
            public readonly List<Renderer> Suit = new();
            public readonly List<Renderer> All = new();
        }

        /// <summary>Pivot height of the whole body (the base of the neck): the body swings around it when swimming.</summary>
        const float RigPivotY = 1.45f;

        static DiverRig BuildDiverRig(Transform parent, Material suit)
        {
            var rig = new DiverRig();
            var seam = Mat("Player_SuitSeam", null, new Color(0.08f, 0.08f, 0.09f), smoothness: 0.3f);
            var steel = Mat("Player_Steel", null, new Color(0.45f, 0.47f, 0.5f), metallic: 0.8f, smoothness: 0.55f);
            var lead = Mat("Player_Lead", null, new Color(0.22f, 0.22f, 0.24f), metallic: 0.3f, smoothness: 0.25f);
            var finMat = Mat("Player_Fin", null, new Color(0.05f, 0.05f, 0.06f), smoothness: 0.5f);
            var lensMat = Mat("Player_ScannerLens", null, new Color(0.4f, 0.95f, 1f), emission: new Color(0.3f, 1f, 1.2f));

            // Only the big silhouette parts cast shadows (about 15 per diver): bolts, straps and lights would
            // be a pixel of shadow each, but every one is drawn into every shadow map.
            GameObject Part(PrimitiveType type, string name, Transform p, Vector3 pos, Vector3 scale, Material mat, bool isSuit = false, bool shadows = false)
            {
                var go = Primitive(type, name, p, pos, scale, mat, collider: false);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                rig.All.Add(r);
                if (isSuit) rig.Suit.Add(r);
                return go;
            }
            Transform Joint(string name, Transform p, Vector3 pos)
            {
                var j = Group(name, p);
                j.localPosition = pos;
                return j;
            }
            // A limb segment hanging down from its joint.
            void Segment(string name, Transform joint, float length, float thickness, Material mat, bool isSuit = true, bool shadows = false) =>
                Part(PrimitiveType.Capsule, name, joint, new Vector3(0f, -length * 0.5f, 0f), new Vector3(thickness, length * 0.5f, thickness), mat, isSuit, shadows);

            rig.Root = Joint("Rig", parent, new Vector3(0f, RigPivotY, 0f));

            // ---- torso
            Part(PrimitiveType.Capsule, "Chest", rig.Root, new Vector3(0f, -0.2f, 0f), new Vector3(0.5f, 0.24f, 0.34f), suit, true, shadows: true);
            Part(PrimitiveType.Cylinder, "Collar", rig.Root, new Vector3(0f, 0.02f, 0f), new Vector3(0.34f, 0.04f, 0.32f), brass);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                Part(PrimitiveType.Sphere, "CollarBolt", rig.Root, new Vector3(Mathf.Cos(a) * 0.16f, 0.06f, Mathf.Sin(a) * 0.15f), Vector3.one * 0.035f, steel);
            }
            // Harness straps over the shoulders.
            foreach (float x in new[] { -0.11f, 0.11f })
            {
                Part(PrimitiveType.Cube, "StrapFront", rig.Root, new Vector3(x, -0.24f, 0.165f), new Vector3(0.05f, 0.4f, 0.02f), seam);
                Part(PrimitiveType.Cube, "StrapBack", rig.Root, new Vector3(x, -0.24f, -0.165f), new Vector3(0.05f, 0.4f, 0.02f), seam);
            }
            Part(PrimitiveType.Cube, "ChestPlate", rig.Root, new Vector3(0f, -0.3f, 0.17f), new Vector3(0.16f, 0.1f, 0.03f), steel);

            // Twin air tanks with a valve and a hose to the helmet.
            foreach (float x in new[] { -0.09f, 0.09f })
            {
                Part(PrimitiveType.Capsule, "AirTank", rig.Root, new Vector3(x, -0.3f, -0.25f), new Vector3(0.16f, 0.27f, 0.16f), yellowTank, shadows: true);
                Part(PrimitiveType.Cylinder, "TankBand", rig.Root, new Vector3(x, -0.38f, -0.25f), new Vector3(0.17f, 0.015f, 0.17f), seam);
            }
            Part(PrimitiveType.Cylinder, "Valve", rig.Root, new Vector3(0f, -0.03f, -0.25f), new Vector3(0.06f, 0.05f, 0.06f), steel);
            var hose = Part(PrimitiveType.Cylinder, "Hose", rig.Root, new Vector3(0.12f, 0.02f, -0.12f), new Vector3(0.035f, 0.14f, 0.035f), seam);
            hose.transform.localRotation = Quaternion.Euler(-50f, 0f, -20f);

            // ---- hips and legs
            rig.Hips = Joint("Hips", rig.Root, new Vector3(0f, -0.5f, 0f));
            Part(PrimitiveType.Capsule, "Belly", rig.Hips, new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.14f, 0.3f), suit, true, shadows: true);
            Part(PrimitiveType.Cylinder, "WeightBelt", rig.Hips, new Vector3(0f, -0.04f, 0f), new Vector3(0.44f, 0.035f, 0.32f), seam);
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                Part(PrimitiveType.Cube, "LeadWeight", rig.Hips, new Vector3(Mathf.Cos(a) * 0.21f, -0.04f, Mathf.Sin(a) * 0.15f), new Vector3(0.07f, 0.06f, 0.05f), lead)
                    .transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            }
            Part(PrimitiveType.Cube, "Buckle", rig.Hips, new Vector3(0f, -0.04f, 0.16f), new Vector3(0.07f, 0.05f, 0.02f), steel);

            (Transform hip, Transform knee, Transform ankle, GameObject fin) Leg(string side, float x)
            {
                var hip = Joint("Hip" + side, rig.Hips, new Vector3(x, -0.08f, 0f));
                Segment("Thigh" + side, hip, 0.44f, 0.17f, suit, shadows: true);
                var knee = Joint("Knee" + side, hip, new Vector3(0f, -0.42f, 0f));
                Part(PrimitiveType.Sphere, "KneePad" + side, knee, new Vector3(0f, 0f, 0.06f), new Vector3(0.12f, 0.12f, 0.08f), seam);
                Segment("Shin" + side, knee, 0.42f, 0.15f, suit, shadows: true);
                var ankle = Joint("Ankle" + side, knee, new Vector3(0f, -0.39f, 0f));
                Part(PrimitiveType.Cube, "Boot" + side, ankle, new Vector3(0f, -0.04f, 0.05f), new Vector3(0.13f, 0.09f, 0.26f), rubber);
                var fin = Group("Fin" + side, ankle).gameObject;
                Part(PrimitiveType.Cube, "FinBlade" + side, fin.transform, new Vector3(0f, -0.05f, 0.4f), new Vector3(0.2f, 0.015f, 0.5f), finMat, shadows: true);
                Part(PrimitiveType.Cube, "FinRail" + side, fin.transform, new Vector3(0f, -0.04f, 0.35f), new Vector3(0.22f, 0.03f, 0.4f), finMat)
                    .transform.localScale = new Vector3(0.22f, 0.025f, 0.42f);
                return (hip, knee, ankle, fin);
            }
            (rig.HipL, rig.KneeL, rig.AnkleL, rig.FinL) = Leg("L", -0.11f);
            (rig.HipR, rig.KneeR, rig.AnkleR, rig.FinR) = Leg("R", 0.11f);

            // ---- arms
            (Transform shoulder, Transform elbow, Transform hand) Arm(string side, float x)
            {
                var shoulder = Joint("Shoulder" + side, rig.Root, new Vector3(x, -0.07f, 0f));
                Part(PrimitiveType.Sphere, "ShoulderPad" + side, shoulder, Vector3.zero, new Vector3(0.17f, 0.15f, 0.17f), suit, true);
                Segment("UpperArm" + side, shoulder, 0.3f, 0.13f, suit, shadows: true);
                var elbow = Joint("Elbow" + side, shoulder, new Vector3(0f, -0.29f, 0f));
                Segment("Forearm" + side, elbow, 0.28f, 0.12f, suit, shadows: true);
                Part(PrimitiveType.Cylinder, "Cuff" + side, elbow, new Vector3(0f, -0.25f, 0f), new Vector3(0.12f, 0.02f, 0.12f), brass);
                var hand = Joint("Hand" + side, elbow, new Vector3(0f, -0.28f, 0f));
                Part(PrimitiveType.Cube, "Glove" + side, hand, new Vector3(0f, -0.06f, 0.01f), new Vector3(0.07f, 0.12f, 0.1f), rubber);
                Part(PrimitiveType.Capsule, "Thumb" + side, hand, new Vector3(0f, -0.03f, 0.055f), new Vector3(0.035f, 0.04f, 0.035f), rubber)
                    .transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
                return (shoulder, elbow, hand);
            }
            (rig.ShoulderL, rig.ElbowL, rig.HandL) = Arm("L", -0.27f);
            (rig.ShoulderR, rig.ElbowR, rig.HandR) = Arm("R", 0.27f);

            // Hand scanner in the right glove (only carried in the water).
            rig.Scanner = Group("Scanner", rig.HandR).gameObject;
            rig.Scanner.transform.localPosition = new Vector3(0f, -0.1f, 0.05f);
            Part(PrimitiveType.Cube, "ScannerBody", rig.Scanner.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.06f, 0.06f, 0.18f), steel);
            Part(PrimitiveType.Cube, "ScannerGrip", rig.Scanner.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.04f, 0.08f, 0.05f), rubber);
            var lens = Part(PrimitiveType.Cylinder, "ScannerLens", rig.Scanner.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.05f, 0.01f, 0.05f), lensMat);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rig.ScannerLens = lens.GetComponent<Renderer>();

            // ---- helmet: an old brass diving helmet with viewports, bolts, headlamp and camera.
            rig.Neck = Joint("Neck", rig.Root, new Vector3(0f, 0.1f, 0f));
            var helmet = rig.Neck;
            Part(PrimitiveType.Sphere, "Helmet", helmet, new Vector3(0f, 0.2f, 0f), new Vector3(0.38f, 0.4f, 0.38f), brass, shadows: true);
            Part(PrimitiveType.Cylinder, "FrontPortRim", helmet, new Vector3(0f, 0.2f, 0.17f), new Vector3(0.22f, 0.03f, 0.22f), brass)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Cylinder, "FrontPort", helmet, new Vector3(0f, 0.2f, 0.19f), new Vector3(0.18f, 0.01f, 0.18f), glassDark)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 3; i++) // guard bars over the front port
                Part(PrimitiveType.Cube, "PortGuard", helmet, new Vector3(-0.05f + i * 0.05f, 0.2f, 0.205f), new Vector3(0.012f, 0.18f, 0.012f), brass);
            foreach (float x in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cylinder, "SidePortRim", helmet, new Vector3(x * 0.17f, 0.21f, 0.03f), new Vector3(0.13f, 0.025f, 0.13f), brass)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Part(PrimitiveType.Cylinder, "SidePort", helmet, new Vector3(x * 0.185f, 0.21f, 0.03f), new Vector3(0.1f, 0.01f, 0.1f), glassDark)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            Part(PrimitiveType.Cylinder, "TopValve", helmet, new Vector3(0f, 0.4f, -0.04f), new Vector3(0.06f, 0.03f, 0.06f), brass);
            Part(PrimitiveType.Cylinder, "HoseFitting", helmet, new Vector3(0.1f, 0.12f, -0.15f), new Vector3(0.05f, 0.04f, 0.05f), steel)
                .transform.localRotation = Quaternion.Euler(60f, 0f, 0f);

            rig.HeadlampHousing = Part(PrimitiveType.Cube, "HeadlampHousing", helmet, new Vector3(0f, 0.36f, 0.12f), new Vector3(0.08f, 0.06f, 0.07f), rubber);
            Part(PrimitiveType.Cylinder, "HeadlampLens", helmet, new Vector3(0f, 0.36f, 0.16f), new Vector3(0.05f, 0.005f, 0.05f), glassDark)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Helmet camera on the left, with a red tally light that is on while recording.
            Part(PrimitiveType.Cube, "HelmetCam", helmet, new Vector3(-0.16f, 0.33f, 0.07f), new Vector3(0.06f, 0.05f, 0.1f), rubber);
            Part(PrimitiveType.Cylinder, "HelmetCamLens", helmet, new Vector3(-0.16f, 0.33f, 0.125f), new Vector3(0.035f, 0.005f, 0.035f), glassDark)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rig.HelmetCamLight = Part(PrimitiveType.Sphere, "HelmetCamTally", helmet, new Vector3(-0.14f, 0.36f, 0.12f), Vector3.one * 0.02f, lampRed);
            // Radio light on the right, red while transmitting.
            rig.RadioLight = Part(PrimitiveType.Sphere, "RadioLight", helmet, new Vector3(0.19f, 0.28f, 0.02f), Vector3.one * 0.045f, lampRed);
            Part(PrimitiveType.Cylinder, "Antenna", helmet, new Vector3(0.14f, 0.45f, -0.08f), new Vector3(0.01f, 0.1f, 0.01f), steel)
                .transform.localRotation = Quaternion.Euler(-15f, 0f, -20f);
            return rig;
        }

        /// <summary>Hooks a built rig up to a <see cref="DiverAnimator"/>.</summary>
        static void AssignRig(DiverAnimator animator, DiverRig rig)
        {
            Assign(animator, "body", rig.Root);
            Assign(animator, "hips", rig.Hips);
            Assign(animator, "neck", rig.Neck);
            Assign(animator, "shoulderL", rig.ShoulderL);
            Assign(animator, "shoulderR", rig.ShoulderR);
            Assign(animator, "elbowL", rig.ElbowL);
            Assign(animator, "elbowR", rig.ElbowR);
            Assign(animator, "handL", rig.HandL);
            Assign(animator, "handR", rig.HandR);
            Assign(animator, "hipL", rig.HipL);
            Assign(animator, "hipR", rig.HipR);
            Assign(animator, "kneeL", rig.KneeL);
            Assign(animator, "kneeR", rig.KneeR);
            Assign(animator, "ankleL", rig.AnkleL);
            Assign(animator, "ankleR", rig.AnkleR);
            Assign(animator, "finL", rig.FinL);
            Assign(animator, "finR", rig.FinR);
            Assign(animator, "scanner", rig.Scanner);
            Assign(animator, "scannerLens", rig.ScannerLens);
            Assign(animator, "recordLight", rig.HelmetCamLight.GetComponent<Renderer>());
        }

        /// <summary>Poses a rig as a body lying face down on the seabed, limbs sprawled.</summary>
        static void PoseLimp(DiverRig rig)
        {
            rig.Root.localPosition = new Vector3(0f, 0.22f, 0.25f);
            rig.Root.localRotation = Quaternion.Euler(92f, 0f, 6f);
            rig.Neck.localRotation = Quaternion.Euler(-35f, 25f, 0f);
            rig.ShoulderL.localRotation = Quaternion.Euler(-60f, 0f, -70f);
            rig.ElbowL.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            rig.ShoulderR.localRotation = Quaternion.Euler(20f, 0f, 55f);
            rig.ElbowR.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            rig.HipL.localRotation = Quaternion.Euler(-8f, 0f, -14f);
            rig.KneeL.localRotation = Quaternion.Euler(35f, 0f, 0f);
            rig.HipR.localRotation = Quaternion.Euler(12f, 0f, 8f);
            rig.KneeR.localRotation = Quaternion.Euler(10f, 0f, 0f);
            rig.AnkleL.localRotation = Quaternion.Euler(50f, 0f, 0f);
            rig.AnkleR.localRotation = Quaternion.Euler(65f, 0f, 0f);
            rig.Scanner.SetActive(false);
            rig.HelmetCamLight.SetActive(false);
            rig.RadioLight.SetActive(false);
        }
    }
}
