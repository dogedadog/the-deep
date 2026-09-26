using System;
using System.IO;
using UnityEngine;

namespace TheDeep.Progression
{
    /// <summary>
    /// Four save slots as JSON files in the player's data folder. The host picks one before
    /// hosting; it's then the <see cref="Active"/> save for that session.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 4;

        public static int ActiveSlot { get; private set; } = -1;
        public static SaveData Active { get; private set; }

        static string Folder => Path.Combine(Application.persistentDataPath, "saves");
        static string PathFor(int slot) => Path.Combine(Folder, $"slot{slot + 1}.json");

        public static bool Exists(int slot) => File.Exists(PathFor(slot));

        /// <summary>Reads a slot without making it active (for the slot list). Null if empty or unreadable.</summary>
        public static SaveData Peek(int slot)
        {
            try
            {
                return Exists(slot) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFor(slot))) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Slot {slot + 1} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>Make a slot active: load it, or start a fresh save there if it's empty.</summary>
        public static void Use(int slot, bool startNew)
        {
            ActiveSlot = slot;
            Active = (!startNew ? Peek(slot) : null) ?? SaveData.CreateNew();
            if (Active.upgrades == null || Active.upgrades.Length != UpgradeCatalog.Count)
            {
                var levels = new int[UpgradeCatalog.Count];
                if (Active.upgrades != null) Array.Copy(Active.upgrades, levels, Math.Min(levels.Length, Active.upgrades.Length));
                Active.upgrades = levels;
            }
            Save();
        }

        public static void Save()
        {
            if (ActiveSlot < 0 || Active == null) return;
            Directory.CreateDirectory(Folder);
            Active.lastPlayedUtc = DateTime.UtcNow.ToString("o");
            // Write to a temp file first so a crash mid-save can't corrupt the slot.
            string path = PathFor(ActiveSlot);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(Active, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static void Delete(int slot)
        {
            if (Exists(slot)) File.Delete(PathFor(slot));
            if (slot == ActiveSlot)
            {
                ActiveSlot = -1;
                Active = null;
            }
        }

        /// <summary>One-line description for the slot picker.</summary>
        public static string Describe(SaveData data)
        {
            if (data == null) return "EMPTY";
            string last = DateTime.TryParse(data.lastPlayedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "?";
            var time = TimeSpan.FromSeconds(data.playSeconds);
            return $"EXPEDITIONS {data.expeditionsCompleted}   UPGRADES {data.UpgradeLevelTotal}   CASE FILES {data.caseFiles.Count}\n" +
                   $"LAST DIVE {last}   PLAYED {(int)time.TotalHours}h {time.Minutes:00}m";
        }
    }
}
