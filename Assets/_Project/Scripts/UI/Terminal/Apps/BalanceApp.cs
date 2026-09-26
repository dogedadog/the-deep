using UnityEngine;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>Placeholder economy screen. Real currency and upgrade purchases come in step 4.</summary>
    public class BalanceApp : TerminalApp
    {
        public override string Title => "Balance";
        public override string IconGlyph => "$CR";
        public override Color IconColor => new(0.45f, 0.4f, 0.1f);
        public override Vector2 WindowSize => new(560, 440);

        static readonly string[] Upgrades = { "Rope Length", "Walkie Range", "Suit: Swim Speed", "Suit: Depth Rating", "Suit: Strength" };

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;

            var heading = RetroUI.Label("Heading", content, "EXPEDITION ACCOUNT", 16, style: FontStyle.Bold);
            RetroUI.Place(heading.rectTransform, 0, 0, width, 22);

            var balance = RetroUI.Readout("Balance", content, "0 CR", 34);
            RetroUI.Place((RectTransform)balance.transform.parent, 0, 26, width, 56);
            balance.alignment = TextAnchor.MiddleRight;

            var historyHeading = RetroUI.Label("HistoryHeading", content, "PURCHASE HISTORY", 15, style: FontStyle.Bold);
            RetroUI.Place(historyHeading.rectTransform, 0, 92, width, 20);
            var history = RetroUI.Readout("History", content, "(no transactions)", 14);
            RetroUI.Place((RectTransform)history.transform.parent, 0, 114, width, 70);

            var upgradesHeading = RetroUI.Label("UpgradesHeading", content, "UPGRADES", 15, style: FontStyle.Bold);
            RetroUI.Place(upgradesHeading.rectTransform, 0, 194, width, 20);

            for (int i = 0; i < Upgrades.Length; i++)
            {
                float y = 218 + i * 34;
                var label = RetroUI.Label("Upgrade", content, Upgrades[i], 15);
                RetroUI.Place(label.rectTransform, 4, y, 260, 30);
                var buy = RetroUI.DisabledButton("Buy", content, "LOCKED", 14);
                RetroUI.Place(buy.GetComponent<RectTransform>(), width - 130, y, 130, 30);
            }
        }
    }
}
