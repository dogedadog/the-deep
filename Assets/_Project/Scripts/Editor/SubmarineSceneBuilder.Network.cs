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
        static GameObject BuildPlayerPrefab()
        {
            var suit = Mat("Player_Suit", null, Color.white, smoothness: 0.35f);
            var player = new GameObject("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.9f, 0);

            // Body visuals.
            var body = Group("Body", player.transform);
            var torso = Primitive(PrimitiveType.Capsule, "Torso", body, new Vector3(0, 0.82f, 0), new Vector3(0.5f, 0.8f, 0.36f), suit, collider: false);
            var armL = Primitive(PrimitiveType.Capsule, "ArmL", body, new Vector3(-0.32f, 1.05f, 0), new Vector3(0.15f, 0.36f, 0.15f), suit, collider: false);
            var armR = Primitive(PrimitiveType.Capsule, "ArmR", body, new Vector3(0.32f, 1.05f, 0), new Vector3(0.15f, 0.36f, 0.15f), suit, collider: false);
            var tank = Primitive(PrimitiveType.Capsule, "AirTank", body, new Vector3(0, 1.05f, -0.24f), new Vector3(0.22f, 0.32f, 0.22f), yellowTank, collider: false);

            var head = Group("Head", player.transform);
            head.localPosition = new Vector3(0, 1.65f, 0);
            var helmet = Sphere("Helmet", head, new Vector3(0, 0.02f, 0), 0.36f, brass);
            var visor = Primitive(PrimitiveType.Sphere, "Visor", head, new Vector3(0, 0.02f, 0.1f), new Vector3(0.24f, 0.18f, 0.18f), glassDark, collider: false);

            // First-person camera (activated only for the owner).
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(head, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 70f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Assign(camGo.AddComponent<PixelatedCamera>(), "screenMaterial", RetroScreenMaterial());
            camGo.SetActive(false);

            var fpc = player.AddComponent<FirstPersonController>();
            Assign(fpc, "head", head);
            fpc.enabled = false;
            var interactor = player.AddComponent<PlayerInteractor>();
            Assign(interactor, "playerCamera", cam);
            interactor.enabled = false;

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
            AssignArray(net, "suitRenderers", Renderers(torso, armL, armR));
            AssignArray(net, "bodyRenderers", Renderers(torso, armL, armR, tank, helmet, visor));
            AssignArray(net, "ownerOnly", new Object[] { fpc, interactor });

            System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        static void BuildNetworking(GameObject playerPrefab, ExteriorPreviewCamera menuCamera)
        {
            var prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (prefabs == null)
            {
                prefabs = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabs, NetworkPrefabsPath);
            }
            if (!prefabs.Contains(playerPrefab))
                prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
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

            new GameObject("SessionManager", typeof(SessionManager));
            var menu = new GameObject("ConnectionMenu").AddComponent<ConnectionMenu>();
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
