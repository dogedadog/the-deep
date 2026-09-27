using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheDeep.Progression
{
    /// <summary>
    /// Four save slots as JSON files in the player's data folder. The host picks one before
    /// hosting; it's then the <see cref="Active"/> save for that session. Each slot keeps the last
    /// save as a .bak, and reading falls back to it (or to a half-finished .tmp) if the main file
    /// is missing or damaged.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 4;
        const string TempSuffix = ".tmp";
        const string BackupSuffix = ".bak";

        public static int ActiveSlot { get; private set; } = -1;
        public static SaveData Active { get; private set; }

        static string Folder => Path.Combine(Application.persistentDataPath, "saves");
        static string PathFor(int slot) => Path.Combine(Folder, $"slot{slot + 1}.json");

        /// <summary>True if the slot has anything on disk: the save, its backup or an unfinished write.</summary>
        public static bool Exists(int slot)
        {
            string path = PathFor(slot);
            return File.Exists(path) || File.Exists(path + TempSuffix) || File.Exists(path + BackupSuffix);
        }

        /// <summary>
        /// Reads a slot without making it active (for the slot list): the save, else the unfinished
        /// write, else the backup. Null if empty or nothing is readable.
        /// </summary>
        public static SaveData Peek(int slot)
        {
            string path = PathFor(slot);
            return TryRead(path) ?? TryRead(path + TempSuffix) ?? TryRead(path + BackupSuffix);
        }

        /// <summary>The main save file is there but can't be read (<see cref="Peek"/> may still recover a backup).</summary>
        public static bool IsDamaged(int slot)
        {
            string path = PathFor(slot);
            return File.Exists(path) && TryRead(path) == null;
        }

        static SaveData TryRead(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                string json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.upgrades == null) return null;
                data.caseFiles ??= new List<CaseFile>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] {Path.GetFileName(file)} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>Make a slot active: load it, or start a fresh save there if it's empty.</summary>
        public static void Use(int slot, bool startNew)
        {
            ActiveSlot = slot;
            var loaded = Peek(slot);
            // About to overwrite a save nothing can be recovered from: keep a copy of it first.
            if (loaded == null && IsDamaged(slot)) KeepDamagedCopy(PathFor(slot));
            Active = (!startNew ? loaded : null) ?? SaveData.CreateNew();

            var levels = new int[UpgradeCatalog.Count];
            if (Active.upgrades != null) Array.Copy(Active.upgrades, levels, Math.Min(levels.Length, Active.upgrades.Length));
            // Hand-edited or buggy saves: keep every level in range.
            for (int i = 0; i < levels.Length; i++) levels[i] = Math.Clamp(levels[i], 0, UpgradeCatalog.MaxLevel);
            Active.upgrades = levels;
            // Also turns a recovered .tmp/.bak back into the main file.
            Save();
        }

        static void KeepDamagedCopy(string path)
        {
            try
            {
                string copy = $"{path}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(path, copy, true);
                Debug.LogWarning($"[Save] {Path.GetFileName(path)} is unreadable; kept a copy as {Path.GetFileName(copy)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Couldn't copy damaged {Path.GetFileName(path)}: {e.Message}");
            }
        }

        /// <summary>Writes the active save. False (and logged) if nothing was written; the data stays in memory.</summary>
        public static bool Save()
        {
            if (ActiveSlot < 0 || Active == null) return false;
            try
            {
                Directory.CreateDirectory(Folder);
                Active.lastPlayedUtc = DateTime.UtcNow.ToString("o");
                // Write a temp file, then swap it in and keep the previous save as .bak: a crash at any
                // point leaves at least one readable copy, and Peek tries all three.
                string path = PathFor(ActiveSlot);
                string temp = path + TempSuffix;
                File.WriteAllText(temp, JsonUtility.ToJson(Active, true));
                if (File.Exists(path)) File.Replace(temp, path, path + BackupSuffix, true);
                else File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Couldn't write slot {ActiveSlot + 1}: {e.Message}");
                return false;
            }
        }

        public static void Delete(int slot)
        {
            string path = PathFor(slot);
            try
            {
                foreach (string file in new[] { path, path + TempSuffix, path + BackupSuffix })
                    if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Couldn't delete slot {slot + 1}: {e.Message}");
            }
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
