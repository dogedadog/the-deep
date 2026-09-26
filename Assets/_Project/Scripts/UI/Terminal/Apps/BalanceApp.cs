using System.Text;
using TheDeep.Data;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>Expedition credits and submission history. Buying upgrades arrives in step 4.</summary>
    public class BalanceApp : TerminalApp
    {
        static readonly string[] Upgrades = { "Rope Length", "Walkie Range", "Suit: Swim Speed", "Suit: Depth Rating", "Suit: Strength" };

        Text balance, history;
        bool open;

        public override string Title => "Balance";
        public override string IconGlyph => "$CR";
        public override Color IconColor => new(0.45f, 0.4f, 0.1f);
        public override Vector2 WindowSize => new(560, 440);

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;

            var heading = RetroUI.Label("Heading", content, "EXPEDITION ACCOUNT", 16, style: FontStyle.Bold);
            RetroUI.Place(heading.rectTransform, 0, 0, width, 22);

            balance = RetroUI.Readout("Balance", content, "0 CR", 34);
            RetroUI.Place((RectTransform)balance.transform.parent, 0, 26, width, 56);
            balance.alignment = TextAnchor.MiddleRight;

            var historyHeading = RetroUI.Label("HistoryHeading", content, "SUBMISSION HISTORY", 15, style: FontStyle.Bold);
            RetroUI.Place(historyHeading.rectTransform, 0, 92, width, 20);
            history = RetroUI.Readout("History", content, "(no transactions)", 14);
            RetroUI.Place((RectTransform)history.transform.parent, 0, 114, width, 70);

            var upgradesHeading = RetroUI.Label("UpgradesHeading", content, "UPGRADES  (coming soon)", 15, style: FontStyle.Bold);
            RetroUI.Place(upgradesHeading.rectTransform, 0, 194, width, 20);

            for (int i = 0; i < Upgrades.Length; i++)
            {
                float y = 218 + i * 34;
                var label = RetroUI.Label("Upgrade", content, Upgrades[i], 15);
                RetroUI.Place(label.rectTransform, 4, y, 260, 30);
                var buy = RetroUI.DisabledButton("Buy", content, "LOCKED", 14);
                RetroUI.Place(buy.GetComponent<RectTransform>(), width - 130, y, 130, 30);
            }
            Refresh();
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (open && Time.frameCount % 15 == 0) Refresh();
        }

        void Refresh()
        {
            var state = ExpeditionState.Instance;
            if (state == null || !state.IsSpawned)
            {
                balance.text = "-- CR";
                history.text = "ACCOUNT OFFLINE";
                return;
            }
            balance.text = $"{state.Credits} CR";
            if (state.History.Count == 0)
            {
                history.text = "(no transactions)";
                return;
            }
            var sb = new StringBuilder();
            // Latest three submissions.
            for (int i = state.History.Count - 1, shown = 0; i >= 0 && shown < 3; i--, shown++)
            {
                var record = state.History[i];
                sb.Append($"#{i + 1:00}  DATA SUBMISSION  {record.Count} PACKET(S)   +{record.Credits} CR\n");
            }
            history.text = sb.ToString();
        }
    }
}
