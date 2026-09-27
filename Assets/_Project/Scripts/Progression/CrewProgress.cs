using System;
using TheDeep.Core;
using TheDeep.Data;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Progression
{
    /// <summary>A recovered piece of evidence, as synced to every player.</summary>
    public struct CaseFileEntry : INetworkSerializable, IEquatable<CaseFileEntry>
    {
        public int TargetId;
        public int Expedition;
        public FixedString64Bytes Title;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Expedition);
            serializer.SerializeValue(ref Title);
        }

        public bool Equals(CaseFileEntry other) => TargetId == other.TargetId && Expedition == other.Expedition;
        public override bool Equals(object obj) => obj is CaseFileEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TargetId, Expedition);
    }

    /// <summary>
    /// The permanent side of the save, live in the session: upgrade levels and case files. The host
    /// loads them from its active save slot and writes back whenever they change; everyone else
    /// just reads the synced values to apply upgrade effects.
    /// </summary>
    public class CrewProgress : NetworkBehaviour
    {
        const float AutosaveSeconds = 60f;

        NetworkList<int> levels;
        NetworkList<CaseFileEntry> caseFiles;
        readonly NetworkVariable<int> expedition = new(1);
        float autosaveTimer;

        public static CrewProgress Instance { get; private set; }
        public NetworkList<CaseFileEntry> CaseFiles => caseFiles;
        /// <summary>Number of the expedition in progress (1 for a brand new save).</summary>
        public int Expedition => expedition.Value;
        /// <summary>Raised on every client when upgrade levels change.</summary>
        public static event Action Changed;

        void Awake()
        {
            Instance = this;
            levels = new NetworkList<int>();
            caseFiles = new NetworkList<CaseFileEntry>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Hosting without picking a slot (e.g. test launch options): use slot 1.
                if (SaveSystem.Active == null) SaveSystem.Use(0, startNew: false);
                var save = SaveSystem.Active;
                levels.Clear();
                foreach (int level in save.upgrades) levels.Add(level);
                caseFiles.Clear();
                foreach (var file in save.caseFiles)
                    caseFiles.Add(new CaseFileEntry { TargetId = file.targetId, Expedition = file.expedition, Title = file.title });
                expedition.Value = save.expeditionsCompleted + 1;
            }
            // Named handler, removed on despawn: the in-scene object is spawned again on every rehost.
            levels.OnListChanged += OnLevelsChanged;
            ApplyEffects();
        }

        public override void OnNetworkDespawn()
        {
            levels.OnListChanged -= OnLevelsChanged;
            if (IsServer) SaveSystem.Save();
            SignalModel.RangeMultiplier = 1f;
        }

        public int Level(UpgradeType type) => IsSpawned && levels.Count > (int)type ? levels[(int)type] : 0;

        void OnLevelsChanged(NetworkListEvent<int> change) => ApplyEffects();

        void ApplyEffects()
        {
            SignalModel.RangeMultiplier = UpgradeCatalog.SignalRange(Level(UpgradeType.WalkieRange));
            Changed?.Invoke();
        }

        /// <summary>
        /// Crew clicked BUY on the Balance app. <paramref name="fromLevel"/> is the level the buyer saw,
        /// so a double-click (or two crew clicking at once) installs one level, not two.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void BuyUpgradeRpc(UpgradeType type, int fromLevel)
        {
            if ((int)type < 0 || (int)type >= UpgradeCatalog.Count || !UpgradeCatalog.IsAvailable(type)) return;
            int level = Level(type);
            if (level != fromLevel) return;
            int cost = UpgradeCatalog.Cost(type, level);
            var expeditionState = ExpeditionState.Instance;
            if (cost < 0 || expeditionState == null) return;
            if (!expeditionState.TrySpend(cost, $"{UpgradeCatalog.Name(type).ToUpperInvariant()} L{level + 1}")) return;

            levels[(int)type] = level + 1;
            SaveSystem.Active.upgrades[(int)type] = level + 1;
            // If the write fails the purchase stands: it's in memory and the next autosave retries.
            SaveSystem.Save();
            expeditionState.AnnounceRpc($"UPGRADE INSTALLED: {UpgradeCatalog.Name(type).ToUpperInvariant()} (LEVEL {level + 1})");
        }

        /// <summary>Server: evidence was submitted; keep it in the case files for good.</summary>
        public void AddCaseFile(int targetId, string title)
        {
            if (!IsServer) return;
            foreach (var file in caseFiles)
                if (file.TargetId == targetId) return;
            caseFiles.Add(new CaseFileEntry { TargetId = targetId, Expedition = expedition.Value, Title = title });
            SaveSystem.Active.caseFiles.Add(new CaseFile
            {
                targetId = targetId, title = title, expedition = expedition.Value, recoveredUtc = DateTime.UtcNow.ToString("o"),
            });
            SaveSystem.Save();
        }

        /// <summary>Server: credits earned from submitted data (for lifetime stats).</summary>
        public void RecordEarnings(int credits)
        {
            if (IsServer && SaveSystem.Active != null) SaveSystem.Active.lifetimeCredits += credits;
        }

        /// <summary>Server: the crew surfaced. Count it and save.</summary>
        public void CompleteExpedition()
        {
            if (!IsServer) return;
            SaveSystem.Active.expeditionsCompleted++;
            SaveSystem.Save();
            expedition.Value = SaveSystem.Active.expeditionsCompleted + 1;
        }

        void Update()
        {
            // IsServer stays set after LEAVE (the in-scene object is despawned, not destroyed), so
            // without IsSpawned menu time would be counted and autosaved into the last slot.
            if (!IsServer || !IsSpawned || SaveSystem.Active == null) return;
            SaveSystem.Active.playSeconds += Time.unscaledDeltaTime;
            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer >= AutosaveSeconds)
            {
                autosaveTimer = 0f;
                SaveSystem.Save();
            }
        }

        void OnApplicationQuit()
        {
            if (IsServer && IsSpawned) SaveSystem.Save();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
