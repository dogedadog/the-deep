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

        public bool IsDocumented(int targetId) => IsSpawned && documented.Contains(targetId);

        /// <summary>A diver finished scanning something: nobody else can scan it again this expedition.</summary>
        [Rpc(SendTo.Server)]
        public void ReportScanRpc(int targetId)
        {
            if (!documented.Contains(targetId)) documented.Add(targetId);
        }

        /// <summary>A diver radioed in their held data. Weak signal corrupts it.</summary>
        [Rpc(SendTo.Server)]
        public void TransmitRpc(int[] targetIds, float signal, RpcParams rpcParams = default)
        {
            int diver = (int)rpcParams.Receive.SenderClientId + 1;
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
        public void SubmitRpc()
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
            if (count == 0) return;
            credits.Value += earned;
            submittedCount.Value += count;
            ledger.Add(new LedgerEntry { Credits = earned, Label = $"DATA SUBMISSION ({count} PKT)" });
            if (CrewProgress.Instance != null) CrewProgress.Instance.RecordEarnings(earned);
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
        /// Host only: surface and end this expedition. Saves, pulls every diver aboard, and resets
        /// credits and data for the next one. Unspent credits are lost.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void EndExpeditionRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            int finished = CrewProgress.Instance != null ? CrewProgress.Instance.Expedition : 0;
            int lost = credits.Value;
            if (CrewProgress.Instance != null) CrewProgress.Instance.CompleteExpedition();

            packets.Clear();
            documented.Clear();
            ledger.Clear();
            credits.Value = 0;
            submittedCount.Value = 0;
            ForceAboardRpc();
            AnnounceRpc($"EXPEDITION #{finished} COMPLETE  -  PROGRESS SAVED{(lost > 0 ? $"  -  {lost} UNSPENT CR LOST" : "")}\n" +
                        $"EXPEDITION #{finished + 1} BEGINS");
        }

        [Rpc(SendTo.Everyone)]
        public void AnnounceRpc(string message) => Announced?.Invoke(message);

        [Rpc(SendTo.Everyone)]
        void ForceAboardRpc()
        {
            var local = PlayerNetwork.Local;
            if (local == null || DiveHatch.CabinEntry == null) return;
            var diver = local.GetComponent<DiverController>();
            if (diver != null && diver.IsDiving) diver.ExitWater(DiveHatch.CabinEntry);
        }

        void Update()
        {
            if (!IsServer) return;
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
