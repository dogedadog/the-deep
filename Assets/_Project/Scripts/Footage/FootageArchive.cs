using System;
using System.Collections.Generic;
using TheDeep.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Footage
{
    public enum ChipStatus : byte
    {
        /// <summary>In a dead diver's suit, waiting to be recovered.</summary>
        InBody,
        /// <summary>A living diver picked it up and is carrying it.</summary>
        Carried,
        /// <summary>In the sub's chip reader: watchable in the Footage app.</summary>
        Inserted,
    }

    /// <summary>What everyone knows about a camera chip.</summary>
    public struct ChipInfo : INetworkSerializable, IEquatable<ChipInfo>
    {
        public int Id;
        public int Diver;
        public ChipStatus Status;
        public ulong Carrier;
        public float Seconds;
        public bool Death;
        public FixedString64Bytes Title;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Diver);
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref Carrier);
            serializer.SerializeValue(ref Seconds);
            serializer.SerializeValue(ref Death);
            serializer.SerializeValue(ref Title);
        }

        public bool Equals(ChipInfo other) => Id == other.Id && Status == other.Status && Carrier == other.Carrier;
        public override bool Equals(object obj) => obj is ChipInfo other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, (int)Status, Carrier);
    }

    /// <summary>
    /// Every camera chip in the session: where it is (in a body, carried, in the reader) and its
    /// footage. The server keeps the footage data; clients fetch it when they want to watch.
    /// Team 7's chips are generated locally from <see cref="LostDiverFootage"/>.
    /// </summary>
    public class FootageArchive : NetworkBehaviour
    {
        const int ChunkSize = 3000;

        NetworkList<ChipInfo> chips;
        readonly Dictionary<int, byte[]> serverData = new();
        readonly Dictionary<int, FootageClip> localClips = new();
        readonly Dictionary<int, byte[][]> incoming = new();
        readonly HashSet<int> requested = new();
        static int nextLocalId;

        public static FootageArchive Instance { get; private set; }
        public NetworkList<ChipInfo> Chips => chips;

        void Awake()
        {
            Instance = this;
            chips = new NetworkList<ChipInfo>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            for (int i = 0; i < LostDiverFootage.Count; i++)
                AddOrReplace(new ChipInfo
                {
                    Id = LostDiverFootage.ChipId(i), Diver = 700 + i + 1, Status = ChipStatus.InBody,
                    Seconds = 35f, Death = true, Title = LostDiverFootage.Title(i),
                });
        }

        /// <summary>A fresh id for a chip recorded on this machine.</summary>
        public int NewChipId() => (int)(NetworkManager.LocalClientId + 1) * 100000 + ++nextLocalId;

        public bool TryGetInfo(int id, out ChipInfo info)
        {
            foreach (var c in chips)
                if (c.Id == id) { info = c; return true; }
            info = default;
            return false;
        }

        // ------------------------------------------------------------------ uploading your own footage

        /// <summary>Owner: send a recording to the server under <paramref name="status"/>.</summary>
        public void Submit(int id, FootageClip clip, ChipStatus status)
        {
            RegisterRpc(id, clip.Diver, clip.Duration, clip.EndsInDeath, clip.Title, status);
            byte[] bytes = clip.ToBytes();
            localClips[id] = clip;
            int total = Mathf.Max(1, Mathf.CeilToInt(bytes.Length / (float)ChunkSize));
            for (int i = 0; i < total; i++)
            {
                int len = Mathf.Min(ChunkSize, bytes.Length - i * ChunkSize);
                var chunk = new byte[len];
                Array.Copy(bytes, i * ChunkSize, chunk, 0, len);
                UploadChunkRpc(id, i, total, chunk);
            }
        }

        [Rpc(SendTo.Server)]
        void RegisterRpc(int id, int diver, float seconds, bool death, FixedString64Bytes title, ChipStatus status, RpcParams rpcParams = default)
        {
            AddOrReplace(new ChipInfo
            {
                Id = id, Diver = diver, Status = status, Seconds = seconds, Death = death, Title = title,
                Carrier = status == ChipStatus.Carried ? rpcParams.Receive.SenderClientId : 0,
            });
        }

        [Rpc(SendTo.Server)]
        void UploadChunkRpc(int id, int index, int total, byte[] data)
        {
            if (Assemble(id, index, total, data, out byte[] full)) serverData[id] = full;
        }

        // ------------------------------------------------------------------ moving chips around (server)

        public void ServerSetStatus(int id, ChipStatus status, ulong carrier)
        {
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i].Id != id) continue;
                var c = chips[i];
                c.Status = status;
                c.Carrier = carrier;
                chips[i] = c;
            }
        }

        /// <summary>Server: all chips a player is carrying.</summary>
        public List<int> ServerCarriedBy(ulong clientId)
        {
            var list = new List<int>();
            foreach (var c in chips)
                if (c.Status == ChipStatus.Carried && c.Carrier == clientId) list.Add(c.Id);
            return list;
        }

        /// <summary>A player at the chip reader inserts every chip they're carrying.</summary>
        [Rpc(SendTo.Server)]
        public void InsertCarriedRpc(RpcParams rpcParams = default)
        {
            foreach (int id in ServerCarriedBy(rpcParams.Receive.SenderClientId))
            {
                ServerSetStatus(id, ChipStatus.Inserted, 0);
                // Team 7 footage is evidence: it goes into the case files for good.
                if (LostDiverFootage.IsLostDiverChip(id) && CrewProgress.Instance != null)
                    CrewProgress.Instance.AddCaseFile(1000 - id, $"FOOTAGE: TEAM 7 DIVER {LostDiverFootage.DiverIndex(id) + 1}");
            }
        }

        /// <summary>Server: the expedition ended. Chips left in bodies or carried are lost; Team 7's reset.</summary>
        public void ServerResetForNewExpedition()
        {
            for (int i = chips.Count - 1; i >= 0; i--)
            {
                var c = chips[i];
                if (LostDiverFootage.IsLostDiverChip(c.Id)) { c.Status = ChipStatus.InBody; c.Carrier = 0; chips[i] = c; }
                else if (c.Status != ChipStatus.Inserted) chips.RemoveAt(i);
            }
        }

        void AddOrReplace(ChipInfo info)
        {
            for (int i = 0; i < chips.Count; i++)
                if (chips[i].Id == info.Id) { chips[i] = info; return; }
            chips.Add(info);
        }

        // ------------------------------------------------------------------ watching

        /// <summary>The footage for a chip if it's available here yet (it's fetched from the server on first ask).</summary>
        public FootageClip GetClip(int id)
        {
            if (localClips.TryGetValue(id, out var clip)) return clip;
            if (LostDiverFootage.IsLostDiverChip(id))
            {
                int index = LostDiverFootage.DiverIndex(id);
                clip = LostDiverFootage.Create(index, LostDiverBodyPosition(index));
                localClips[id] = clip;
                return clip;
            }
            if (IsServer && serverData.TryGetValue(id, out var bytes))
            {
                clip = FootageClip.FromBytes(bytes);
                localClips[id] = clip;
                return clip;
            }
            if (requested.Add(id)) RequestClipRpc(id);
            return null;
        }

        static Vector3 LostDiverBodyPosition(int index)
        {
            foreach (var body in FindObjectsByType<DiverBody>(FindObjectsSortMode.None))
                if (body.LostDiverIndex == index) return body.transform.position;
            return new Vector3(260f, -1205f, 0f);
        }

        [Rpc(SendTo.Server)]
        void RequestClipRpc(int id, RpcParams rpcParams = default)
        {
            if (!serverData.TryGetValue(id, out var bytes)) return;
            var target = RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp);
            int total = Mathf.Max(1, Mathf.CeilToInt(bytes.Length / (float)ChunkSize));
            for (int i = 0; i < total; i++)
            {
                int len = Mathf.Min(ChunkSize, bytes.Length - i * ChunkSize);
                var chunk = new byte[len];
                Array.Copy(bytes, i * ChunkSize, chunk, 0, len);
                ClipChunkRpc(id, i, total, chunk, target);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ClipChunkRpc(int id, int index, int total, byte[] data, RpcParams rpcParams)
        {
            if (Assemble(id, index, total, data, out byte[] full))
            {
                localClips[id] = FootageClip.FromBytes(full);
                requested.Remove(id);
            }
        }

        bool Assemble(int id, int index, int total, byte[] data, out byte[] full)
        {
            full = null;
            if (!incoming.TryGetValue(id, out var parts) || parts.Length != total)
                incoming[id] = parts = new byte[total][];
            parts[index] = data;
            foreach (var p in parts)
                if (p == null) return false;
            int length = 0;
            foreach (var p in parts) length += p.Length;
            full = new byte[length];
            int offset = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, full, offset, p.Length);
                offset += p.Length;
            }
            incoming.Remove(id);
            return true;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
