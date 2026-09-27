using TheDeep.Core;
using TheDeep.Core.Rendering;
using TheDeep.Networking;
using TheDeep.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TheDeep.EditorTools
{
    /// <summary>Multiplayer pieces: the networked player prefab, NetworkManager, session/menu, spawn points.</summary>
    public static partial class SubmarineSceneBuilder
    {
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        const string NetworkPrefabsPath = "Assets/_Project/ScriptableObjects/NetworkPrefabs.asset";

        static void BuildSpawnPoints(Transform t)
        {
            // Midship walkway, facing the bow.
            Vector3[] spots = { new(-1.6f, 0.05f, 0.45f), new(-1.6f, 0.05f, -0.35f), new(-0.6f, 0.05f, 0.5f), new(0.6f, 0.05f, 0.45f), new(1.4f, 0.05f, -0.2f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var point = Group($"Spawn_{i}", t);
                point.localPosition = spots[i];
                point.localRotation = Quaternion.Euler(0, 90, 0);
                Assign(point.gameObject.AddComponent<PlayerSpawnPoint>(), "index", i);
            }
        }

        /// <summary>
        /// Player prefab: first-person rig (camera off until we know this is the local player) and a
        /// simple suited body other players see, tinted per player.
        /// </summary>
        static GameObject BuildPlayerPrefab(GameObject bodyPrefab)
        {
            var suit = Mat("Player_Suit", null, Color.white, smoothness: 0.35f);
            var player = new GameObject("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.9f, 0);

            // Body visuals: a jointed diving suit, posed by DiverAnimator.
            var body = Group("Body", player.transform);
            var rig = BuildDiverRig(body, suit);

            var head = Group("Head", player.transform);
            head.localPosition = new Vector3(0, 1.65f, 0);

            // First-person camera (activated only for the owner).
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(head, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 70f;
            cam.cullingMask = NormalCullingMask;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Assign(camGo.AddComponent<PixelatedCamera>(), "screenMaterial", RetroScreenMaterial());
            camGo.SetActive(false);

            var fpc = player.AddComponent<FirstPersonController>();
            Assign(fpc, "head", head);
            fpc.enabled = false;
            var interactor = player.AddComponent<PlayerInteractor>();
            Assign(interactor, "playerCamera", cam);
            interactor.enabled = false;

            // Swimming: a physics body + capsule, only active while in the water.
            var rb = player.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 80f;
            rb.linearDamping = 2.2f;
            rb.angularDamping = 10f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0, 0.9f, 0);
            capsule.enabled = false;

            var lampGo = new GameObject("Headlamp", typeof(Light));
            lampGo.transform.SetParent(head, false);
            lampGo.transform.localPosition = new Vector3(0, 0.12f, 0.18f);
            var lamp = lampGo.GetComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = new Color(0.85f, 0.93f, 1f);
            lamp.intensity = 140f;
            lamp.range = 38f;
            lamp.spotAngle = 62f;
            lamp.innerSpotAngle = 25f;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;

            var diver = player.AddComponent<DiverController>();
            Assign(diver, "head", head);
            Assign(diver, "headlamp", lamp);

            // Marine snow that travels with the diver, so it's there on the deepest ledges and the shaft floor,
            // not only near the sub. Spawns in a shell 4-10 m out so nothing pops in right at the visor.
            // DiverController only turns it on for the local diver while in the water.
            var diverSnow = Particles("DiverSnow", head, Vector3.zero, ParticleMat("P_MarineSnow", false));
            var diverSnowMain = diverSnow.main;
            diverSnowMain.startLifetime = 14f;
            diverSnowMain.startSpeed = 0.03f;
            diverSnowMain.gravityModifier = 0.003f;
            diverSnowMain.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            diverSnowMain.startColor = new Color(0.8f, 0.85f, 0.8f);
            diverSnowMain.maxParticles = 900;
            var diverSnowEmission = diverSnow.emission;
            diverSnowEmission.rateOverTime = 45f;
            diverSnowEmission.rateOverDistance = 3f; // keeps up with a sprinting diver
            var diverSnowShape = diverSnow.shape;
            diverSnowShape.shapeType = ParticleSystemShapeType.Sphere;
            diverSnowShape.radius = 10f;
            diverSnowShape.radiusThickness = 0.6f;
            var diverSnowNoise = diverSnow.noise;
            diverSnowNoise.enabled = true;
            diverSnowNoise.strength = 0.05f;
            diverSnow.gameObject.SetActive(false);
            Assign(diver, "snow", diverSnow);

            // Air, suit crush and death (leaves a body), and the helmet camera.
            var health = player.AddComponent<DiverHealth>();
            Assign(health, "bodyPrefab", bodyPrefab);
            Assign(health, "headlamp", lamp);
            var helmetCam = player.AddComponent<TheDeep.Footage.HelmetCamera>();
            Assign(helmetCam, "head", head);
            Assign(helmetCam, "headlamp", lamp);

            // Tether clips onto the back of the harness, under the air tank.
            var harness = Group("Harness", player.transform);
            harness.localPosition = new Vector3(0, 0.85f, -0.25f);
            var tether = player.AddComponent<DiverTether>();
            Assign(tether, "harness", harness);
            // High-visibility yellow safety line with a faint reflective sheen, so it reads in the dark.
            Assign(tether, "ropeMaterial", Mat("Tether_HiVis", null, new Color(0.95f, 0.78f, 0.12f), smoothness: 0.4f, emission: new Color(0.09f, 0.07f, 0.01f)));

            // Networking: owner moves itself; everyone else interpolates.
            player.AddComponent<NetworkObject>();
            var nt = player.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            var net = player.AddComponent<PlayerNetwork>();
            Assign(net, "head", head);
            Assign(net, "cameraRoot", camGo);
            AssignArray(net, "suitRenderers", rig.Suit.ToArray());
            AssignArray(net, "bodyRenderers", rig.All.ToArray());
            AssignArray(net, "ownerOnly", new Object[] { fpc, interactor });

            var scanner = player.AddComponent<DiverScanner>();
            Assign(scanner, "head", head);
            Assign(scanner, "beamMaterial", Mat("Scanner_Beam", null, new Color(0.4f, 0.95f, 1f), emission: new Color(0.6f, 2f, 2.4f)));

            // Voice: helmet radio light (red while transmitting) and bubbles when talking or breathing underwater.
            var radioLed = rig.RadioLight;
            radioLed.GetComponent<Renderer>().enabled = false;
            var bubbleSystem = Particles("TalkBubbles", rig.Neck, new Vector3(0f, 0.2f, 0.22f), ParticleMat("P_Bubbles", false));
            var bubbleMain = bubbleSystem.main;
            bubbleMain.prewarm = false;
            bubbleMain.startLifetime = 2.5f;
            bubbleMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            bubbleMain.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            bubbleMain.startColor = new Color(0.8f, 0.9f, 1f);
            bubbleMain.gravityModifier = -0.15f;
            var bubbleEmission = bubbleSystem.emission;
            bubbleEmission.rateOverTime = 0f;
            var bubbleShape = bubbleSystem.shape;
            bubbleShape.shapeType = ParticleSystemShapeType.Sphere;
            bubbleShape.radius = 0.05f;
            var voice = player.AddComponent<TheDeep.Voice.PlayerVoice>();
            Assign(voice, "head", head);
            Assign(voice, "radioLight", radioLed.GetComponent<Renderer>());
            Assign(voice, "bubbles", bubbleSystem);

            var animator = player.AddComponent<DiverAnimator>();
            Assign(animator, "head", head);
            Assign(animator, "bubbles", bubbleSystem);
            AssignRig(animator, rig);
            // Own layer, so ropes (and later footage cameras) can ignore players.
            int playerLayer = LayerMask.NameToLayer("Player");
            foreach (var child in player.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = playerLayer;

            System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        static void BuildNetworking(GameObject playerPrefab, ExteriorPreviewCamera menuCamera, GameObject bodyPrefab)
        {
            var prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (prefabs == null)
            {
                prefabs = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabs, NetworkPrefabsPath);
            }
            foreach (var prefab in new[] { playerPrefab, bodyPrefab })
                if (!prefabs.Contains(prefab)) prefabs.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(prefabs);

            var go = new GameObject("NetworkManager", typeof(NetworkManager), typeof(UnityTransport));
            var manager = go.GetComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = go.GetComponent<UnityTransport>(),
                PlayerPrefab = playerPrefab,
                ConnectionApproval = false,
                EnableSceneManagement = true,
            };
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            // Headroom for hitches (loading, alt-tab) so packets aren't dropped.
            go.GetComponent<UnityTransport>().MaxPacketQueueSize = 512;

            new GameObject("SessionManager", typeof(SessionManager));
            var menu = new GameObject("ConnectionMenu", typeof(TheDeep.Core.SettingsRuntime)).AddComponent<ConnectionMenu>();
            Assign(menu, "menuCamera", menuCamera);

            PlayerSettings.runInBackground = true;
        }

        static Object[] Renderers(params GameObject[] objects)
        {
            var result = new Object[objects.Length];
            for (int i = 0; i < objects.Length; i++) result[i] = objects[i].GetComponent<Renderer>();
            return result;
        }

        static void AssignArray(Component component, string field, Object[] values)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
