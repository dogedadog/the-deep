using System.Text;
using TheDeep.Data;
using TheDeep.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Expedition account and upgrade shop. Credits reset when the crew surfaces, upgrades are
    /// permanent (saved in the host's slot). The host ends the expedition from here.
    /// </summary>
    public class BalanceApp : TerminalApp
    {
        const float RowY = 196f;
        const float RowHeight = 36f;

        readonly Button[] buyButtons = new Button[UpgradeCatalog.Count];
        readonly Text[] levelTexts = new Text[UpgradeCatalog.Count];
        readonly Text[] effectTexts = new Text[UpgradeCatalog.Count];
        Text balance, ledger, heading;
        Button endButton;
        Text endText;
        float confirmUntil;
        bool open;

        public override string Title => "Balance";
        public override string IconGlyph => "$CR";
        public override Color IconColor => new(0.45f, 0.4f, 0.1f);
        public override Vector2 WindowSize => new(600, 500);

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
                buyButtons[i] = RetroUI.Button("Buy", content, "", () =>
                {
                    if (CrewProgress.Instance != null) CrewProgress.Instance.BuyUpgradeRpc(type);
                }, 14);
                RetroUI.Place(buyButtons[i].GetComponent<RectTransform>(), width - 144, y, 144, RowHeight - 4);
            }

            endButton = RetroUI.Button("End", content, "", EndExpedition, 15);
            RetroUI.Place(endButton.GetComponent<RectTransform>(), 0, RowY + UpgradeCatalog.Count * RowHeight + 8, width, 40);
            endText = endButton.GetComponentInChildren<Text>();
            Refresh();
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (open && Time.frameCount % 10 == 0) Refresh();
        }

        void EndExpedition()
        {
            // Two clicks, since it throws away unspent credits.
            if (Time.time > confirmUntil)
            {
                confirmUntil = Time.time + 4f;
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
                int level = progress.Level(type);
                int cost = UpgradeCatalog.Cost(type, level);
                levelTexts[i].text = $"L{level}/{UpgradeCatalog.MaxLevel}";
                effectTexts[i].text = cost < 0
                    ? UpgradeCatalog.Describe(type, level)
                    : $"{UpgradeCatalog.Describe(type, level)} > {UpgradeCatalog.Describe(type, level + 1)}";
                var label = buyButtons[i].GetComponentInChildren<Text>();
                label.text = cost < 0 ? "MAXED" : $"BUY  {cost} CR";
                buyButtons[i].interactable = cost >= 0 && state.Credits >= cost;
            }

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            endButton.gameObject.SetActive(true);
            endButton.interactable = isHost;
            endText.text = !isHost ? "ONLY THE HOST CAN END THE EXPEDITION"
                : Time.time < confirmUntil ? $"CLICK AGAIN TO SURFACE  ({state.Credits} UNSPENT CR WILL BE LOST)"
                : "END EXPEDITION  -  SURFACE AND SAVE";
            endButton.targetGraphic.color = Time.time < confirmUntil ? new Color(1f, 0.7f, 0.5f) : RetroUI.Face;
        }
    }
}
