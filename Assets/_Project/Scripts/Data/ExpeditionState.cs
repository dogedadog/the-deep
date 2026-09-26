using TheDeep.Core;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Data
{
    /// <summary>
    /// Server-owned record of this expedition's data: which targets have been documented, every
    /// packet divers have radioed in (and its logging status), and credits earned from submissions.
    /// Everyone reads it; changes go through RPCs so all terminals stay in sync.
    /// </summary>
    public class ExpeditionState : NetworkBehaviour
    {
        public const float LogSeconds = 3f;
        public const float RepairSeconds = 7f;

        NetworkList<DataPacket> packets;
        NetworkList<int> documented;
        NetworkList<SubmissionRecord> history;
        readonly NetworkVariable<int> credits = new();
        readonly NetworkVariable<int> submittedCount = new();
        int nextPacketId = 1;

        public static ExpeditionState Instance { get; private set; }
        public NetworkList<DataPacket> Packets => packets;
        public NetworkList<SubmissionRecord> History => history;
        public int Credits => credits.Value;
        public int SubmittedCount => submittedCount.Value;
        public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        void Awake()
        {
            Instance = this;
            packets = new NetworkList<DataPacket>();
            documented = new NetworkList<int>();
            history = new NetworkList<SubmissionRecord>();
        }

        public bool IsDocumented(int targetId) => IsSpawned && documented.Contains(targetId);

        /// <summary>A diver finished scanning something: nobody else can scan it again.</summary>
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

        /// <summary>Crew submitted everything logged: it turns into credits.</summary>
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
            }
            if (count == 0) return;
            credits.Value += earned;
            submittedCount.Value += count;
            history.Add(new SubmissionRecord { Count = count, Credits = earned });
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
