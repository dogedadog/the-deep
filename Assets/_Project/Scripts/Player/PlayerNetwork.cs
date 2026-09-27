using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheDeep.Networking;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDeep.Player
{
    /// <summary>
    /// Networked player root. The owner gets the camera, controls and HUD; everyone else just sees
    /// the suited body (tinted per player) moved by the NetworkTransform, with the head pitch synced.
    /// </summary>
    public class PlayerNetwork : NetworkBehaviour
    {
        static readonly Color[] SuitColors =
        {
            new(0.85f, 0.42f, 0.08f), // orange
            new(0.85f, 0.75f, 0.12f), // yellow
            new(0.1f, 0.55f, 0.55f),  // teal
            new(0.7f, 0.12f, 0.1f),   // red
            new(0.8f, 0.8f, 0.78f),   // white
        };

        /// <summary>Crew slots D1-D5, one per possible player.</summary>
        const int CrewSlots = 5;
        const byte NoSlot = 255;

        static readonly List<PlayerNetwork> spawned = new();

        [SerializeField] Transform head;
        [SerializeField] GameObject cameraRoot;
        [SerializeField] Renderer[] suitRenderers;
        [SerializeField] Renderer[] bodyRenderers;
        [SerializeField, Tooltip("Components only the owning player runs (controls, interaction).")]
        Behaviour[] ownerOnly;

        readonly NetworkVariable<float> headPitch = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // Picked by the server (lowest free slot), because NGO client ids only ever go up: a friend who
        // rejoins keeps being D2 instead of becoming D3, D4...
        readonly NetworkVariable<byte> crewSlot = new(NoSlot);

        public static PlayerNetwork Local { get; private set; }
        /// <summary>Every spawned player on this machine, including the local one.</summary>
        public static IReadOnlyList<PlayerNetwork> All => spawned;
        public Color SuitColor => SuitColors[(CrewNumber - 1) % SuitColors.Length];
        public static Color ColorForCrew(int crewNumber) => SuitColors[Mathf.Max(0, crewNumber - 1) % SuitColors.Length];
        /// <summary>1-based crew number shown on the terminal (D1, D2...).</summary>
        public int CrewNumber => crewSlot.Value == NoSlot ? (int)OwnerClientId + 1 : crewSlot.Value + 1;
        public FirstPersonController Controller { get; private set; }

        void Awake() => Controller = GetComponent<FirstPersonController>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) crewSlot.Value = FreeCrewSlot();
            spawned.Add(this);
            crewSlot.OnValueChanged += OnCrewSlotChanged;
            ApplyTint();

            if (IsOwner)
            {
                Local = this;
                cameraRoot.SetActive(true);
                foreach (var b in ownerOnly) b.enabled = true;
                // You don't see your own body, but it still casts a shadow.
                foreach (var r in bodyRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                StartCoroutine(MoveToSpawnPoint());
            }
            else
            {
                cameraRoot.SetActive(false);
                foreach (var b in ownerOnly) b.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            spawned.Remove(this);
            crewSlot.OnValueChanged -= OnCrewSlotChanged;
            if (Local == this) Local = null;
        }

        /// <summary>Owner only: swap between first-person view and the exterior camera (F2 view, spectating).</summary>
        public void SetFirstPersonView(bool on)
        {
            cameraRoot.SetActive(on);
            Controller.SetLock(FirstPersonController.Lock.View, !on);
        }

        /// <summary>Server: the lowest crew slot no other spawned player is using.</summary>
        byte FreeCrewSlot()
        {
            for (byte slot = 0; slot < CrewSlots; slot++)
            {
                bool taken = false;
                foreach (var p in spawned)
                    if (p != this && p.crewSlot.Value == slot) taken = true;
                if (!taken) return slot;
            }
            return (byte)(OwnerClientId % CrewSlots);
        }

        void OnCrewSlotChanged(byte previous, byte current) => ApplyTint();

        void ApplyTint()
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", SuitColor);
            foreach (var r in suitRenderers) r.SetPropertyBlock(block);
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                float pitch = head.localEulerAngles.x;
                headPitch.Value = pitch > 180f ? pitch - 360f : pitch;
            }
            else
            {
                head.localRotation = Quaternion.Euler(headPitch.Value, 0f, 0f);
            }
        }

        IEnumerator MoveToSpawnPoint()
        {
            yield return null; // let NetworkTransform finish spawning first
            // The spawn point follows the crew slot, which the server may still be sending.
            float giveUp = Time.realtimeSinceStartup + 3f;
            while (crewSlot.Value == NoSlot && Time.realtimeSinceStartup < giveUp) yield return null;
            if (!IsSpawned) yield break;
            var points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None).OrderBy(p => p.Index).ToArray();
            if (points.Length == 0) yield break;
            var point = points[(CrewNumber - 1) % points.Length].transform;

            var cc = GetComponent<CharacterController>();
            cc.enabled = false;
            GetComponent<NetworkTransform>().Teleport(point.position, point.rotation, transform.localScale);
            cc.enabled = true;
        }
    }
}
