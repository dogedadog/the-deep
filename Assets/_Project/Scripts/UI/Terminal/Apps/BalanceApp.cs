using System.Collections.Generic;
using System.Text;
using TheDeep.Data;
using TheDeep.Footage;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.Submarine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Expedition account and upgrade shop. Credits reset when the crew surfaces, upgrades are
    /// permanent (saved in the host's slot). The host ends the expedition from here (anyone may
    /// while the host is dead), after a second click that lists everything surfacing will lose.
    /// </summary>
    public class BalanceApp : TerminalApp
    {
        const float RowY = 196f;
        const float RowHeight = 36f;
        const float ConfirmSeconds = 6f;
        const float BuyLockSeconds = 0.5f;

        readonly Button[] buyButtons = new Button[UpgradeCatalog.Count];
        readonly Text[] levelTexts = new Text[UpgradeCatalog.Count];
        readonly Text[] effectTexts = new Text[UpgradeCatalog.Count];
        // Level each row showed at the last refresh, and when its BUY button unlocks after a click.
        readonly int[] shownLevels = new int[UpgradeCatalog.Count];
        readonly float[] buyLockUntil = new float[UpgradeCatalog.Count];
        Text balance, ledger, heading;
        Button endButton;
        Text endText, endWarning;
        float confirmUntil;
        bool open;

        public override string Title => "Balance";
        public override string IconGlyph => "$CR";
        public override Color IconColor => new(0.45f, 0.4f, 0.1f);
        public override Vector2 WindowSize => new(600, 520);

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;

            heading = RetroUI.Label("Heading", content, "", 16, style: FontStyle.Bold);
            RetroUI.Place(heading.rectTransform, 0, 0, width, 22);

            balance = RetroUI.Readout("Balance", content, "0 CR", 34);
            RetroUI.Place((RectTransform)balance.transform.parent, 0, 24, width, 54);
            balance.alignment = TextAnchor.MiddleRight;

            ledger = RetroUI.Readout("Ledger", content, "", 13);
            RetroUI.Place((RectTransform)ledger.transform.parent, 0, 84, width, 78);

            var upgradesHeading = RetroUI.Label("UpgradesHeading", content, "PERMANENT UPGRADES", 15, style: FontStyle.Bold);
            RetroUI.Place(upgradesHeading.rectTransform, 0, 170, width, 22);

            for (int i = 0; i < UpgradeCatalog.Count; i++)
            {
                var type = (UpgradeType)i;
                float y = RowY + i * RowHeight;
                var name = RetroUI.Label("Name", content, UpgradeCatalog.Name(type), 15);
                RetroUI.Place(name.rectTransform, 4, y, 170, RowHeight - 4);
                levelTexts[i] = RetroUI.Label("Level", content, "", 14, style: FontStyle.Bold);
                RetroUI.Place(levelTexts[i].rectTransform, 176, y, 56, RowHeight - 4);
                effectTexts[i] = RetroUI.Label("Effect", content, "", 13);
                RetroUI.Place(effectTexts[i].rectTransform, 232, y, 200, RowHeight - 4);
                buyButtons[i] = RetroUI.Button("Buy", content, "", () => Buy(type), 14);
                RetroUI.Place(buyButtons[i].GetComponent<RectTransform>(), width - 144, y, 144, RowHeight - 4);
            }

            endButton = RetroUI.Button("End", content, "", EndExpedition, 15);
            RetroUI.Place(endButton.GetComponent<RectTransform>(), 0, RowY + UpgradeCatalog.Count * RowHeight + 8, width, 40);
            endText = endButton.GetComponentInChildren<Text>();
            endWarning = RetroUI.Label("EndWarning", content, "", 13, new Color(0.62f, 0.06f, 0.03f), TextAnchor.UpperLeft);
            RetroUI.Place(endWarning.rectTransform, 0, RowY + UpgradeCatalog.Count * RowHeight + 52, width, 36);
            Refresh();
        }

        public override void OnOpened()
        {
            open = true;
            Refresh();
        }

        public override void OnClosed() => open = false;

        void Update()
        {
            if (open && Time.frameCount % 10 == 0) Refresh();
        }

        void Buy(UpgradeType type)
        {
            var progress = CrewProgress.Instance;
            int row = (int)type;
            if (progress == null || !progress.IsSpawned || Time.unscaledTime < buyLockUntil[row]) return;
            // Lock the row briefly and send the level the player saw, not the live one: on the host the
            // RPC runs at once, so a double-click's second click would otherwise buy the next level too.
            // The server's level check still covers two crew members clicking together.
            buyLockUntil[row] = Time.unscaledTime + BuyLockSeconds;
            buyButtons[row].interactable = false;
            progress.BuyUpgradeRpc(type, shownLevels[row]);
        }

        void EndExpedition()
        {
            // Two clicks, since it throws away unspent credits (and whatever else the warning lists).
            if (Time.time > confirmUntil)
            {
                confirmUntil = Time.time + ConfirmSeconds;
                Refresh();
                return;
            }
            confirmUntil = 0f;
            if (ExpeditionState.Instance != null) ExpeditionState.Instance.EndExpeditionRpc();
        }

        void Refresh()
        {
            var state = ExpeditionState.Instance;
            var progress = CrewProgress.Instance;
            bool online = state != null && state.IsSpawned && progress != null && progress.IsSpawned;
            if (!online)
            {
                heading.text = "EXPEDITION ACCOUNT";
                balance.text = "-- CR";
                ledger.text = "ACCOUNT OFFLINE";
                foreach (var b in buyButtons) b.interactable = false;
                endButton.gameObject.SetActive(false);
                endWarning.text = "";
                return;
            }

            heading.text = $"EXPEDITION #{progress.Expedition} ACCOUNT   (credits reset when you surface)";
            balance.text = $"{state.Credits} CR";

            var sb = new StringBuilder();
            if (state.Ledger.Count == 0) sb.Append("(no transactions this expedition)");
            for (int i = state.Ledger.Count - 1, shown = 0; i >= 0 && shown < 4; i--, shown++)
            {
                var entry = state.Ledger[i];
                sb.Append($"{entry.Label,-34} {(entry.Credits >= 0 ? "+" : "")}{entry.Credits} CR\n");
            }
            ledger.text = sb.ToString();

            for (int i = 0; i < UpgradeCatalog.Count; i++)
            {
                var type = (UpgradeType)i;
                var label = buyButtons[i].GetComponentInChildren<Text>();
                if (!UpgradeCatalog.IsAvailable(type))
                {
                    levelTexts[i].text = "--";
                    effectTexts[i].text = "NOT FITTED YET";
                    label.text = "OFFLINE";
                    buyButtons[i].interactable = false;
                    continue;
                }
                int level = progress.Level(type);
                int cost = UpgradeCatalog.Cost(type, level);
                shownLevels[i] = level;
                levelTexts[i].text = $"L{level}/{UpgradeCatalog.MaxLevel}";
                string effect = cost < 0
                    ? UpgradeCatalog.Describe(type, level)
                    : $"{UpgradeCatalog.Describe(type, level)} > {UpgradeCatalog.Describe(type, level + 1)}";
                if (type == UpgradeType.DepthRating && cost >= 0 && StationNeeding(level + 1) is string station)
                    effect += $" ({station})";
                effectTexts[i].text = effect;
                label.text = cost < 0 ? "MAXED"
                    : cost > state.Credits ? $"NEED {cost - state.Credits} CR"
                    : $"BUY  {cost} CR";
                buyButtons[i].interactable = cost >= 0 && state.Credits >= cost && Time.unscaledTime >= buyLockUntil[i];
            }

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            bool hostDead = !isHost && HostIsDead();
            bool canEnd = isHost || hostDead;
            bool confirming = canEnd && Time.time < confirmUntil;
            endButton.gameObject.SetActive(true);
            endButton.interactable = canEnd;
            endText.text = !canEnd ? "ONLY THE HOST CAN END THE EXPEDITION"
                : confirming ? "CLICK AGAIN TO SURFACE"
                : hostDead ? "HOST HAS NO VITALS - ANY CREW MAY SURFACE"
                : "END EXPEDITION  -  SURFACE AND SAVE";
            endButton.targetGraphic.color = confirming ? new Color(1f, 0.7f, 0.5f) : RetroUI.Face;
            endWarning.text = confirming ? SurfaceLosses(state) : "";
        }

        /// <summary>Name of the dive station that needs exactly this depth-rating level, or null.</summary>
        static string StationNeeding(int depthLevel)
        {
            var nav = SubNavigation.Instance;
            if (nav == null || nav.Stations == null) return null;
            foreach (var s in nav.Stations)
                if (s.requiredDepthLevel == depthLevel) return s.name;
            return null;
        }

        /// <summary>The host's diver has no vitals, so it can't reach a terminal to end the expedition.</summary>
        static bool HostIsDead()
        {
            foreach (var health in FindObjectsByType<DiverHealth>(FindObjectsSortMode.None))
                if (health.IsSpawned && health.OwnerClientId == NetworkManager.ServerClientId) return health.IsDead;
            return false;
        }

        /// <summary>Everything ending the expedition now throws away (only the non-zero lines).</summary>
        static string SurfaceLosses(ExpeditionState state)
        {
            var lines = new List<string>();
            if (state.Credits > 0) lines.Add($"{state.Credits} CR UNSPENT WILL BE LOST");

            // LOGGED packets are submitted automatically when the crew surfaces; these aren't.
            int unlogged = 0;
            foreach (var p in state.Packets)
                if (p.Status == PacketStatus.Pending || p.Status == PacketStatus.Corrupted ||
                    p.Status == PacketStatus.Logging || p.Status == PacketStatus.Repairing) unlogged++;
            if (unlogged > 0) lines.Add($"{unlogged} UNLOGGED {Plural(unlogged, "PACKET")} WILL BE LOST");

            int chips = 0;
            var archive = FootageArchive.Instance;
            if (archive != null && archive.IsSpawned)
                foreach (var chip in archive.Chips)
                    if (chip.Status != ChipStatus.Inserted && !LostDiverFootage.IsLostDiverChip(chip.Id)) chips++;
            if (chips > 0) lines.Add($"{chips} CAMERA {Plural(chips, "CHIP")} NOT IN THE READER WILL BE LOST");

            int outside = 0;
            foreach (var diver in FindObjectsByType<DiverController>(FindObjectsSortMode.None))
                if (diver.IsSpawned && diver.IsDiving) outside++;
            if (outside > 0) lines.Add($"{outside} {Plural(outside, "DIVER")} STILL OUTSIDE");

            return string.Join("   /   ", lines);
        }

        static string Plural(int n, string word) => n == 1 ? word : word + "S";
    }
}
