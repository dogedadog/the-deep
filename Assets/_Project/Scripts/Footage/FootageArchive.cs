using System;
using System.Collections;
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
    /// Footage travels in paced chunks, one clip at a time per connection, so a big clip never clogs
    /// the connection for everything else.
    /// Team 7's chips are generated locally from <see cref="LostDiverFootage"/>.
    /// Chips are kept in the order they went into the reader, oldest first.
    /// </summary>
    public class FootageArchive : NetworkBehaviour
    {
        const int ChunkSize = 3000;
        const float SendBytesPerSecond = 160000f;
        const float SendBurstBytes = 16000f + ChunkSize;
        const float RequestRetrySeconds = 6f;

        /// <summary>A clip on its way to one peer, and how far it got.</summary>
        class Transfer
        {
            public int Id;
            public byte[] Bytes;
            public int Next;
            public int Total => Mathf.Max(1, (Bytes.Length + ChunkSize - 1) / ChunkSize);
        }

        NetworkList<ChipInfo> chips;
        readonly Dictionary<int, byte[]> serverData = new();
        // Server: clients that asked for a clip that was still uploading.
        readonly Dictionary<int, List<ulong>> waiting = new();
        // Clips on their way out, per peer (the server's to each client, a client's to the server). The last one goes first.
        readonly Dictionary<ulong, List<Transfer>> outbox = new();
        readonly Dictionary<int, FootageClip> localClips = new();
        readonly Dictionary<int, byte[][]> incoming = new();
        // Client: when we last asked for a clip, or last heard from it.
        readonly Dictionary<int, float> requestedAt = new();
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
            // This object lives in the scene and outlasts a session, so every session starts empty.
            localClips.Clear();
            incoming.Clear();
            requestedAt.Clear();
            outbox.Clear();
            if (!IsServer) return;
            chips.Clear();
            serverData.Clear();
            waiting.Clear();
            for (int i = 0; i < LostDiverFootage.Count; i++)
                AddOrReplace(new ChipInfo
                {
                    Id = LostDiverFootage.ChipId(i), Diver = 700 + i + 1, Status = ChipStatus.InBody,
                    Seconds = 35f, Death = true, Title = LostDiverFootage.Title(i),
                });
        }

        public override void OnNetworkDespawn()
        {
            StopAllCoroutines();
            serverData.Clear();
            waiting.Clear();
            outbox.Clear();
            localClips.Clear();
            incoming.Clear();
            requestedAt.Clear();
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
            // Registered first, so the chip exists on the server before any of its data arrives.
            RegisterRpc(id, clip.Diver, clip.Duration, clip.EndsInDeath, clip.Title, status);
            byte[] bytes = clip.ToBytes();
            localClips[id] = clip;
            if (IsServer)
            {
                ServerStore(id, bytes);
                return;
            }
            Send(NetworkManager.ServerClientId, id, bytes);
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
            // A chip that was lost in the meantime (the expedition ended) doesn't need its data.
            if (!TryGetInfo(id, out _)) return;
            if (Assemble(id, index, total, data, out byte[] full)) ServerStore(id, full);
        }

        /// <summary>Server: a clip's data is complete. Send it to everyone who asked while it was uploading.</summary>
        void ServerStore(int id, byte[] bytes)
        {
            serverData[id] = bytes;
            if (!waiting.Remove(id, out var clients)) return;
            foreach (ulong client in clients) ServerSendClip(id, bytes, client);
        }

        /// <summary>
        /// Queues a clip for <paramref name="peer"/> ahead of everything queued before it: the newest
        /// request is the one someone is looking at. Asking again moves a clip up without starting it over.
        /// </summary>
        void Send(ulong peer, int id, byte[] bytes)
        {
            bool idle = !outbox.TryGetValue(peer, out var queue);
            if (idle) outbox[peer] = queue = new List<Transfer>();
            int at = queue.FindIndex(t => t.Id == id);
            var transfer = at >= 0 ? queue[at] : new Transfer { Id = id, Bytes = bytes };
            if (at >= 0) queue.RemoveAt(at);
            queue.Add(transfer);
            if (idle) StartCoroutine(Drain(peer, queue));
        }

        /// <summary>
        /// Sends a peer's queue in chunks at about 160 KB/s from one shared budget, so the reliable channel
        /// keeps room for movement and everything else. Stops when the peer leaves.
        /// </summary>
        IEnumerator Drain(ulong peer, List<Transfer> queue)
        {
            float budget = SendBurstBytes;
            while (queue.Count > 0 && IsSpawned && (!IsServer || NetworkManager.ConnectedClients.ContainsKey(peer)))
            {
                var t = queue[queue.Count - 1];
                // A chip lost since it was asked for (the expedition ended) doesn't need sending.
                if (IsServer && !TryGetInfo(t.Id, out _)) { queue.RemoveAt(queue.Count - 1); continue; }
                int offset = t.Next * ChunkSize;
                int len = Mathf.Min(ChunkSize, t.Bytes.Length - offset);
                if (budget < len)
                {
                    yield return null;
                    budget = Mathf.Min(budget + Time.unscaledDeltaTime * SendBytesPerSecond, SendBurstBytes);
                    continue;
                }
                budget -= len;
                var chunk = new byte[len];
                Array.Copy(t.Bytes, offset, chunk, 0, len);
                int total = t.Total;
                if (IsServer) ClipChunkRpc(t.Id, t.Next, total, chunk, RpcTarget.Single(peer, RpcTargetUse.Temp));
                else UploadChunkRpc(t.Id, t.Next, total, chunk);
                if (++t.Next >= total) queue.RemoveAt(queue.Count - 1);
            }
            if (outbox.TryGetValue(peer, out var current) && current == queue) outbox.Remove(peer);
        }

        // ------------------------------------------------------------------ moving chips around (server)

        public void ServerSetStatus(int id, ChipStatus status, ulong carrier)
        {
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i].Id != id) continue;
                var c = chips[i];
                bool inserting = status == ChipStatus.Inserted && c.Status != ChipStatus.Inserted;
                c.Status = status;
                c.Carrier = carrier;
                if (inserting)
                {
                    // To the end of the list, so the list stays in the order chips went into the reader.
                    chips.RemoveAt(i);
                    chips.Add(c);
                }
                else chips[i] = c;
                return;
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
                else if (c.Status != ChipStatus.Inserted)
                {
                    chips.RemoveAt(i);
                    serverData.Remove(c.Id);
                    localClips.Remove(c.Id);
                    incoming.Remove(c.Id);
                    waiting.Remove(c.Id);
                    requestedAt.Remove(c.Id);
                }
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
            if (!IsSpawned) return null;
            if (IsServer)
            {
                // The host has every clip itself; it only waits for the upload to finish.
                if (!serverData.TryGetValue(id, out var bytes)) return null;
                clip = FootageClip.FromBytes(bytes);
                localClips[id] = clip;
                return clip;
            }
            // Ask the server, and ask again if nothing has arrived for a while.
            if (!requestedAt.TryGetValue(id, out float asked) || Time.unscaledTime - asked > RequestRetrySeconds)
            {
                requestedAt[id] = Time.unscaledTime;
                RequestClipRpc(id);
            }
            return null;
        }

        static Vector3 LostDiverBodyPosition(int index)
        {
            foreach (var body in FindObjectsByType<DiverBody>(FindObjectsSortMode.None))
                if (body.LostDiverIndex == index) return body.transform.position;
            return new Vector3(0f, -1200f, 0f);
        }

        [Rpc(SendTo.Server)]
        void RequestClipRpc(int id, RpcParams rpcParams = default)
        {
            ulong client = rpcParams.Receive.SenderClientId;
            if (serverData.TryGetValue(id, out var bytes))
            {
                ServerSendClip(id, bytes, client);
                return;
            }
            // Still uploading: it goes out the moment the last chunk arrives.
            if (!TryGetInfo(id, out _)) return;
            if (!waiting.TryGetValue(id, out var clients)) waiting[id] = clients = new List<ulong>();
            if (!clients.Contains(client)) clients.Add(client);
        }

        void ServerSendClip(int id, byte[] bytes, ulong client)
        {
            // The host reads serverData itself.
            if (client != NetworkManager.LocalClientId) Send(client, id, bytes);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ClipChunkRpc(int id, int index, int total, byte[] data, RpcParams rpcParams)
        {
            // Already here (a repeated request sent it twice): the extra chunks aren't needed.
            if (localClips.ContainsKey(id)) return;
            // Still arriving, so no need to ask again yet.
            if (requestedAt.ContainsKey(id)) requestedAt[id] = Time.unscaledTime;
            if (Assemble(id, index, total, data, out byte[] full))
            {
                localClips[id] = FootageClip.FromBytes(full);
                requestedAt.Remove(id);
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
