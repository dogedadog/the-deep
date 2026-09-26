using System;
using System.Collections.Generic;

namespace TheDeep.Progression
{
    /// <summary>
    /// Everything that survives between expeditions, stored per save slot (the host's).
    /// Credits are deliberately NOT here: they reset every expedition.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string createdUtc;
        public string lastPlayedUtc;
        public int expeditionsCompleted;
        public int lifetimeCredits;
        public float playSeconds;
        /// <summary>Level per <see cref="UpgradeType"/>, indexed by the enum value.</summary>
        public int[] upgrades = new int[UpgradeCatalog.Count];
        public List<CaseFile> caseFiles = new();

        public static SaveData CreateNew() => new()
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            lastPlayedUtc = DateTime.UtcNow.ToString("o"),
        };

        public int UpgradeLevelTotal
        {
            get
            {
                int total = 0;
                foreach (int level in upgrades) total += level;
                return total;
            }
        }
    }

    /// <summary>A piece of recovered evidence about what happened down here.</summary>
    [Serializable]
    public class CaseFile
    {
        public int targetId;
        public string title;
        public int expedition;
        public string recoveredUtc;
    }
}
