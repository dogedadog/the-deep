using TheDeep.Data;
using TheDeep.Footage;
using TheDeep.UI.Terminal.Apps;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace TheDeep.EditorTools
{
    /// <summary>Helmet footage pieces: the body prefab, Team 7's remains, the chip reader, and the replay camera.</summary>
    public static partial class SubmarineSceneBuilder
    {
        const string BodyPrefabPath = "Assets/_Project/Prefabs/DiverBody.prefab";

        static int FootageProxyLayer => LayerMask.NameToLayer("FootageProxy");
        static int PlayerLayer => LayerMask.NameToLayer("Player");
        /// <summary>What normal cameras draw: everything except things that exist only in footage.</summary>
        static int NormalCullingMask => ~(1 << FootageProxyLayer);

        /// <summary>A diver lying dead on the seabed, camera light blinking while its chip is inside.</summary>
        static GameObject BuildBodyPrefab()
        {
            var suit = Mat("Player_Suit", null, Color.white, smoothness: 0.35f);
            var root = new GameObject("DiverBody");
            var body = Group("Pose", root.transform);
            var rig = BuildDiverRig(body, suit);
            PoseLimp(rig);
            rig.FinR.SetActive(false); // one fin lost
            // Cracked front port, and the helmet camera light still blinking while its chip is inside.
            var crack = Mat("Body_VisorCrack", null, new Color(0.8f, 0.85f, 0.85f), emission: new Color(0.05f, 0.05f, 0.05f));
            for (int i = 0; i < 3; i++)
                Box("Crack", rig.Neck, new Vector3(-0.03f + 0.03f * i, 0.2f, 0.2f), new Vector3(0.008f, 0.12f, 0.004f), crack, collider: false, worldUV: false)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, -30f + i * 35f);
            var chipLight = Sphere("CameraLight", rig.Neck, new Vector3(-0.14f, 0.36f, 0.12f), 0.04f, lampRed);

            var zone = root.AddComponent<SphereCollider>();
            zone.isTrigger = true;
            zone.center = new Vector3(0f, 0.3f, -0.5f);
            zone.radius = 1.5f;

            root.AddComponent<NetworkObject>();
            var diverBody = root.AddComponent<DiverBody>();
            AssignArray(diverBody, "suitRenderers", rig.Suit.ToArray());
            Assign(diverBody, "chipLight", chipLight.GetComponent<Renderer>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, BodyPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Dive Team 7: four bodies on the shaft floor, faded suits, their helmet cameras still in them.</summary>
        static void BuildLostDivers(GameObject bodyPrefab, Transform parent)
        {
            var faded = Mat("Body_FadedSuit", null, new Color(0.35f, 0.36f, 0.3f), smoothness: 0.15f);
            var rng = new System.Random(77);
            for (int i = 0; i < lostDiverSpots.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab);
                instance.name = $"Team7_Diver{i + 1}";
                instance.transform.SetParent(parent, true);
                instance.transform.SetPositionAndRotation(lostDiverSpots[i] + Vector3.up * 0.05f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                var body = instance.GetComponent<DiverBody>();
                var so = new SerializedObject(body);
                so.FindProperty("lostDiverIndex").intValue = i;
                so.ApplyModifiedPropertiesWithoutUndo();
                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                    if (r.name is "Torso" or "ArmL" or "ArmR") r.sharedMaterial = faded;
                MakeScannable(instance, 23 + i, $"REMAINS: TEAM 7 DIVER {i + 1}", DataCategory.Evidence, 200, 5f, 1.6f);
            }
        }

        /// <summary>Slot on the wall by the terminal for camera chips.</summary>
        static void BuildChipReader(Transform terminalRoot)
        {
            var reader = Group("ChipReader", terminalRoot);
            reader.localPosition = new Vector3(-1.05f, 1.3f, 0.37f);
            Box("Case", reader, Vector3.zero, new Vector3(0.34f, 0.22f, 0.12f), painted, worldUV: false);
            Box("Slot", reader, new Vector3(0f, 0.02f, -0.061f), new Vector3(0.14f, 0.015f, 0.005f), rubber, collider: false, worldUV: false);
            var led = Box("Led", reader, new Vector3(0.12f, 0.07f, -0.061f), new Vector3(0.025f, 0.025f, 0.005f), indGreen, collider: false, worldUV: false);
            Stencil("CHIP READER", reader, new Vector3(0f, 0.17f, -0.062f), Quaternion.identity, 0.035f, new Color(0.9f, 0.9f, 0.8f));
            var chipReader = reader.gameObject.AddComponent<ChipReader>();
            Assign(chipReader, "busyLight", led.GetComponent<Renderer>());
        }

        /// <summary>The hidden camera that replays footage, plus the creature only it can see.</summary>
        static void BuildFootageRig()
        {
            var go = new GameObject("FootageCamera", typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.015f, 0.02f);
            cam.cullingMask = ~(1 << PlayerLayer); // today's players aren't in old footage

            var lampGo = new GameObject("FootageLamp", typeof(Light));
            lampGo.transform.SetParent(go.transform, false);
            var lamp = lampGo.GetComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = new Color(0.85f, 0.93f, 1f);
            lamp.intensity = 140f;
            lamp.range = 38f;
            lamp.spotAngle = 62f;
            lamp.shadows = LightShadows.None;

            // Stand-in for what took Team 7: body, tail, fin and faint lights.
            var creature = Group("FootageProxy_Creature", null);
            var hide = Primitive(PrimitiveType.Capsule, "Body", creature, Vector3.zero, new Vector3(6f, 17f, 5f), leviathanSkin, collider: false);
            hide.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Capsule, "Tail", creature, new Vector3(0f, 0f, -19f), new Vector3(2f, 7f, 1.2f), leviathanSkin, collider: false)
                .transform.localRotation = Quaternion.Euler(80f, 0f, 0f);
            for (int i = 0; i < 16; i++)
                Sphere("Photophore", creature, new Vector3(i % 2 == 0 ? 2.9f : -2.9f, 0f, -12f + i * 1.5f), 0.35f, bioLight);
            foreach (var t in creature.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = FootageProxyLayer;

            var playback = go.AddComponent<FootagePlayback>();
            Assign(playback, "lamp", lamp);
            Assign(playback, "proxy", creature);
        }

        /// <summary>Normal cameras never draw footage-only things.</summary>
        static void ApplyNormalCullingMasks()
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam.GetComponent<FootagePlayback>() == null) cam.cullingMask = NormalCullingMask;
        }

        static void AddFootageApp(GameObject screen) =>
            Assign(screen.AddComponent<FootageApp>(), "feedMaterial", ShaderMaterial("M_CCTVFeed", "TheDeep/CCTVFeed"));
    }
}
