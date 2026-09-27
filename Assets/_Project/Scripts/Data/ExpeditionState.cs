using System;
using TheDeep.Core;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.Submarine;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Data
{
    /// <summary>
    /// Server-owned record of the current expedition: which targets have been documented, every
    /// packet divers have radioed in (and its logging status), credits and the account ledger.
    /// Everyone reads it; changes go through RPCs so all terminals stay in sync.
    /// Credits live here, not in the save: they reset when the crew surfaces.
    /// If every diver dies, the sub recalls itself and the expedition fails.
    /// </summary>
    public class ExpeditionState : NetworkBehaviour
    {
        public const float LogSeconds = 3f;
        public const float RepairSeconds = 7f;

        NetworkList<DataPacket> packets;
        NetworkList<int> documented;
        NetworkList<LedgerEntry> ledger;
        readonly NetworkVariable<int> credits = new();
        readonly NetworkVariable<int> submittedCount = new();
        int nextPacketId = 1;

        // Server: whole-crew wipe detection (checked once a second).
        float wipeCheck, wipeTimer;
        bool wipeWarned, wipeLatched;

        public static ExpeditionState Instance { get; private set; }
        /// <summary>Crew-wide announcements (upgrades, surfacing), raised on every client.</summary>
        public static event Action<string> Announced;

        public NetworkList<DataPacket> Packets => packets;
        public NetworkList<LedgerEntry> Ledger => ledger;
        public int Credits => credits.Value;
        public int SubmittedCount => submittedCount.Value;
        public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        void Awake()
        {
            Instance = this;
            packets = new NetworkList<DataPacket>();
            documented = new NetworkList<int>();
            ledger = new NetworkList<LedgerEntry>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            // This object sits in the scene and is only despawned on LEAVE, so the last session's
            // lists and credits would otherwise carry into the next host.
            packets.Clear();
            documented.Clear();
            ledger.Clear();
            credits.Value = 0;
            submittedCount.Value = 0;
            nextPacketId = 1;
            wipeCheck = wipeTimer = 0f;
            wipeWarned = wipeLatched = false;
            DiverHealth.ExpeditionStateAnnounce = message => AnnounceRpc(message);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) DiverHealth.ExpeditionStateAnnounce = null;
        }

        public bool IsDocumented(int targetId) => IsSpawned && documented.Contains(targetId);

        /// <summary>A diver finished scanning something: nobody else can scan it again this expedition.</summary>
        [Rpc(SendTo.Server)]
        public void ReportScanRpc(int targetId)
        {
            if (!documented.Contains(targetId)) documented.Add(targetId);
        }

        /// <summary>A diver lost unsent scans (died, or the expedition ended): the crew may scan those targets again.</summary>
        [Rpc(SendTo.Server)]
        public void ForgetScansRpc(int[] ids)
        {
            foreach (int id in ids)
                if (!HasPacketFor(id)) documented.Remove(id);
        }

        /// <summary>
        /// A diver radioed in their held data. Weak signal corrupts it. Data scanned in an earlier
        /// expedition (sent while the crew was surfacing) is dropped.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void TransmitRpc(int[] targetIds, float signal, int expedition, RpcParams rpcParams = default)
        {
            if (CrewProgress.Instance != null && expedition != CrewProgress.Instance.Expedition) return;
            int diver = CrewNumberOf(rpcParams.Receive.SenderClientId);
            foreach (int targetId in targetIds)
            {
                if (!ScanTarget.TryGet(targetId, out var target) || HasPacketFor(targetId)) continue;
                packets.Add(new DataPacket
                {
                    Id = nextPacketId++,
                    TargetId = targetId,
                    Value = target.Value,
                    Diver = diver,
                    Category = target.Category,
                    Status = signal < SignalModel.CorruptionThreshold ? PacketStatus.Corrupted : PacketStatus.Pending,
                    Title = target.Title,
                });
            }
        }

        /// <summary>Crew clicked LOG (or REPAIR for corrupted data) on a packet.</summary>
        [Rpc(SendTo.Server)]
        public void LogPacketRpc(int packetId)
        {
            int i = IndexOf(packetId);
            if (i < 0) return;
            var p = packets[i];
            if (p.Status == PacketStatus.Pending)
            {
                p.Status = PacketStatus.Logging;
                p.FinishTime = Now + LogSeconds;
            }
            else if (p.Status == PacketStatus.Corrupted)
            {
                p.Status = PacketStatus.Repairing;
                p.FinishTime = Now + RepairSeconds;
            }
            else return;
            packets[i] = p;
        }

        /// <summary>Crew submitted everything logged: it turns into credits; evidence goes into the case files.</summary>
        [Rpc(SendTo.Server)]
        public void SubmitRpc() => ServerSubmitLogged();

        /// <summary>Server: submit every logged packet. Returns the credits earned.</summary>
        int ServerSubmitLogged()
        {
            int count = 0, earned = 0;
            for (int i = 0; i < packets.Count; i++)
            {
                var p = packets[i];
                if (p.Status != PacketStatus.Logged) continue;
                p.Status = PacketStatus.Submitted;
                packets[i] = p;
                count++;
                earned += p.Value;
                if (p.Category == DataCategory.Evidence && CrewProgress.Instance != null)
                    CrewProgress.Instance.AddCaseFile(p.TargetId, p.Title.ToString());
            }
            if (count == 0) return 0;
            credits.Value += earned;
            submittedCount.Value += count;
            ledger.Add(new LedgerEntry { Credits = earned, Label = $"DATA SUBMISSION ({count} PKT)" });
            if (CrewProgress.Instance != null) CrewProgress.Instance.RecordEarnings(earned);
            return earned;
        }

        /// <summary>Server: pay for something if the account can afford it.</summary>
        public bool TrySpend(int amount, string label)
        {
            if (!IsServer || amount > credits.Value) return false;
            credits.Value -= amount;
            ledger.Add(new LedgerEntry { Credits = -amount, Label = label });
            return true;
        }

        /// <summary>
        /// Host only (or anyone, once the host's diver is dead): surface and end this expedition.
        /// Saves, pulls every diver aboard, and resets credits and data for the next one.
        /// Unspent credits are lost.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void EndExpeditionRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId && !HostIsDead()) return;
            ServerEndExpedition(false);
        }

        /// <summary>Server: end the expedition. <paramref name="wiped"/> when the whole crew died.</summary>
        void ServerEndExpedition(bool wiped)
        {
            // Logged data is filed first, so evidence gets this expedition's number and the
            // earnings make it into CompleteExpedition's save.
            ServerSubmitLogged();
            int finished = CrewProgress.Instance != null ? CrewProgress.Instance.Expedition : 0;
            int lost = credits.Value;
            if (CrewProgress.Instance != null) CrewProgress.Instance.CompleteExpedition();

            packets.Clear();
            documented.Clear();
            ledger.Clear();
            credits.Value = 0;
            submittedCount.Value = 0;
            ForceAboardRpc();
            // Bodies of crewmates are left behind; Team 7's chips return to their suits.
            foreach (var body in FindObjectsByType<TheDeep.Footage.DiverBody>(FindObjectsSortMode.None))
            {
                if (body.LostDiverIndex >= 0) body.ServerResetLostDiver();
                else if (body.IsSpawned) body.NetworkObject.Despawn();
            }
            TheDeep.Footage.FootageArchive.Instance?.ServerResetForNewExpedition();
            if (SubNavigation.Instance != null) SubNavigation.Instance.ServerReturnToStart();
            string headline = wiped
                ? $"EXPEDITION #{finished} FAILED  -  ALL CREW LOST  -  UPGRADES AND CASE FILES KEPT"
                : $"EXPEDITION #{finished} COMPLETE  -  PROGRESS SAVED{(lost > 0 ? $"  -  {lost} UNSPENT CR LOST" : "")}";
            AnnounceRpc($"{headline}\nEXPEDITION #{finished + 1} BEGINS");
        }

        [Rpc(SendTo.Everyone)]
        public void AnnounceRpc(string message) => Announced?.Invoke(message);

        [Rpc(SendTo.Everyone)]
        void ForceAboardRpc()
        {
            var local = PlayerNetwork.Local;
            if (local == null || DiveHatch.CabinEntry == null) return;
            // Unsent scans belong to the expedition that just ended; don't upload them on the way in.
            var scanner = local.GetComponent<DiverScanner>();
            if (scanner != null) scanner.DiscardHeld();
            var health = local.GetComponent<DiverHealth>();
            if (health != null) health.Revive(); // also brings the diver aboard
            var diver = local.GetComponent<DiverController>();
            if (diver != null && diver.IsDiving) diver.ExitWater(DiveHatch.CabinEntry);
        }

        /// <summary>Server: the host's diver has no vitals, so anyone may surface the sub.</summary>
        bool HostIsDead()
        {
            return NetworkManager.ConnectedClients.TryGetValue(NetworkManager.ServerClientId, out var host)
                   && host.PlayerObject != null
                   && host.PlayerObject.TryGetComponent(out DiverHealth health) && health.IsDead;
        }

        /// <summary>Server: the D-number of whoever sent an RPC.</summary>
        int CrewNumberOf(ulong clientId)
        {
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null
                && client.PlayerObject.TryGetComponent(out PlayerNetwork player))
                return player.CrewNumber;
            return (int)clientId + 1;
        }

        /// <summary>
        /// Server, once a second: when every diver is dead, warn the crew, then recall the sub.
        /// The latch holds until someone is alive again, so revives still on their way can't re-trigger it.
        /// </summary>
        void CheckForWipe()
        {
            int checkedDivers = 0;
            bool anyoneAlive = false;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue; // still spawning
                if (!client.PlayerObject.TryGetComponent(out DiverHealth health)) continue;
                checkedDivers++;
                if (!health.IsDead) anyoneAlive = true;
            }
            if (checkedDivers == 0 || anyoneAlive)
            {
                wipeTimer = 0f;
                wipeWarned = wipeLatched = false;
                return;
            }
            if (wipeLatched) return;
            wipeTimer += 1f;
            if (wipeTimer >= 3f && !wipeWarned)
            {
                wipeWarned = true;
                AnnounceRpc("ALL DIVERS - NO VITALS.  SUB RECALLING IN 8s");
            }
            if (wipeTimer >= 11f)
            {
                wipeLatched = true;
                ServerEndExpedition(true);
            }
        }

        void Update()
        {
            // IsServer keeps its last value after LEAVE, so also require a live spawn.
            if (!IsServer || !IsSpawned) return;
            wipeCheck += Time.deltaTime;
            if (wipeCheck >= 1f)
            {
                wipeCheck -= 1f;
                CheckForWipe();
            }

            double now = Now;
            for (int i = 0; i < packets.Count; i++)
            {
                var p = packets[i];
                if ((p.Status == PacketStatus.Logging || p.Status == PacketStatus.Repairing) && now >= p.FinishTime)
                {
                    p.Status = PacketStatus.Logged;
                    packets[i] = p;
                }
            }
        }

        /// <summary>0..1 progress of a packet that's being logged/repaired.</summary>
        public float Progress(DataPacket p)
        {
            float duration = p.Status == PacketStatus.Repairing ? RepairSeconds : LogSeconds;
            return Mathf.Clamp01(1f - (float)(p.FinishTime - Now) / duration);
        }

        bool HasPacketFor(int targetId)
        {
            foreach (var p in packets)
                if (p.TargetId == targetId) return true;
            return false;
        }

        int IndexOf(int packetId)
        {
            for (int i = 0; i < packets.Count; i++)
                if (packets[i].Id == packetId) return i;
            return -1;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
