using TheDeep.Core;
using TheDeep.Player;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Footage
{
    /// <summary>
    /// A dead diver on the seabed: either a crewmate (spawned where they died) or one of Dive Team 7
    /// (placed on the shaft floor). Their camera chips are still in the suit; a diver can take them.
    /// </summary>
    public class DiverBody : NetworkBehaviour, IProximityInteractable
    {
        [SerializeField, Tooltip("Team 7 diver index (0-3), or -1 for a crewmate's body.")] int lostDiverIndex = -1;
        [SerializeField] Renderer[] suitRenderers;
        [SerializeField] Renderer chipLight;

        readonly NetworkVariable<int> crew = new(0);
        NetworkList<int> chips;

        public int LostDiverIndex => lostDiverIndex;
        public int ChipCount => chips != null ? chips.Count : 0;

        void Awake() => chips = new NetworkList<int>();

        public override void OnNetworkSpawn()
        {
            if (IsServer && lostDiverIndex >= 0 && chips.Count == 0) chips.Add(LostDiverFootage.ChipId(lostDiverIndex));
            crew.OnValueChanged += (_, _) => ApplyColor();
            ApplyColor();
        }

        /// <summary>Server, right after spawning a crewmate's body.</summary>
        public void ServerInit(int crewNumber, System.Collections.Generic.IEnumerable<int> chipIds)
        {
            crew.Value = crewNumber;
            foreach (int id in chipIds)
            {
                chips.Add(id);
                FootageArchive.Instance?.ServerSetStatus(id, ChipStatus.InBody, 0);
            }
        }

        /// <summary>Server: Team 7's chips return for the next expedition.</summary>
        public void ServerResetLostDiver()
        {
            if (!IsServer || lostDiverIndex < 0) return;
            chips.Clear();
            chips.Add(LostDiverFootage.ChipId(lostDiverIndex));
        }

        void ApplyColor()
        {
            if (lostDiverIndex >= 0) return; // Team 7's faded suits are baked into their material
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", PlayerNetwork.ColorForCrew(crew.Value) * 0.7f);
            foreach (var r in suitRenderers) r.SetPropertyBlock(block);
        }

        void Update()
        {
            // The camera's little red light keeps blinking while a chip is inside.
            if (chipLight != null) chipLight.enabled = ChipCount > 0 && Mathf.Repeat(Time.time, 1.4f) < 0.25f;
        }

        public string Prompt => $"Take camera chip{(ChipCount > 1 ? $" ({ChipCount})" : "")}";

        public bool CanInteract(PlayerInteractor interactor)
        {
            var diver = interactor.GetComponent<DiverController>();
            return ChipCount > 0 && diver != null && diver.IsDiving;
        }

        public void Interact(PlayerInteractor interactor) => TakeChipRpc();

        [Rpc(SendTo.Server)]
        void TakeChipRpc(RpcParams rpcParams = default)
        {
            if (chips.Count == 0) return;
            int id = chips[0];
            chips.RemoveAt(0);
            FootageArchive.Instance?.ServerSetStatus(id, ChipStatus.Carried, rpcParams.Receive.SenderClientId);
        }
    }
}
