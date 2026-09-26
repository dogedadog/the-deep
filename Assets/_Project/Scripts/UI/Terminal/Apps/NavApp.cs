using TheDeep.Progression;
using TheDeep.Submarine;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Move the sub between dive stations. Deeper stations need a better suit depth rating, and
    /// every diver has to be back aboard first.
    /// </summary>
    public class NavApp : TerminalApp
    {
        const float RowHeight = 62f;

        Button[] goButtons;
        Text[] rowTexts;
        Text status;
        bool open;

        public override string Title => "Nav / Depth";
        public override string IconGlyph => "NAV";
        public override Color IconColor => new(0.15f, 0.3f, 0.55f);
        public override Vector2 WindowSize => new(560, 400);

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;
            var header = RetroUI.Label("Header", content, "DIVE STATIONS", 16, style: FontStyle.Bold);
            RetroUI.Place(header.rectTransform, 0, 0, width, 22);

            var nav = SubNavigation.Instance;
            int count = nav != null ? nav.Stations.Length : 0;
            goButtons = new Button[count];
            rowTexts = new Text[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                float y = 28 + i * RowHeight;
                rowTexts[i] = RetroUI.Readout("Station", content, "", 14);
                RetroUI.Place((RectTransform)rowTexts[i].transform.parent, 0, y, width - 130, RowHeight - 6);
                goButtons[i] = RetroUI.Button("Go", content, "GO", () =>
                {
                    if (SubNavigation.Instance != null) SubNavigation.Instance.RequestTravelRpc(index);
                }, 16);
                RetroUI.Place(goButtons[i].GetComponent<RectTransform>(), width - 122, y, 122, RowHeight - 6);
            }
            status = RetroUI.Label("Status", content, "", 13);
            RetroUI.Place(status.rectTransform, 0, 28 + count * RowHeight, width, 40);
            Refresh();
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (open && Time.frameCount % 10 == 0) Refresh();
        }

        void Refresh()
        {
            var nav = SubNavigation.Instance;
            if (nav == null || !nav.IsSpawned)
            {
                status.text = "NAVIGATION OFFLINE";
                foreach (var b in goButtons) b.interactable = false;
                return;
            }
            for (int i = 0; i < goButtons.Length; i++)
            {
                var s = nav.Stations[i];
                string reason = nav.CanTravel(i);
                bool here = i == nav.Current;
                rowTexts[i].text = $"{i + 1}  {s.name.ToUpperInvariant()}   {SubNavigation.DepthOf(s):0} M\n" +
                                   (here ? "   << SUB IS HERE" : reason != null ? $"   {reason}" : "   READY");
                rowTexts[i].color = here ? new Color(1f, 0.85f, 0.3f) : reason != null && reason.StartsWith("NEEDS") ? RetroUI.PhosphorDim : RetroUI.Phosphor;
                goButtons[i].interactable = reason == null;
                goButtons[i].GetComponentInChildren<Text>().text = here ? "HERE" : SubNavigation.DepthOf(s) > SubNavigation.DepthOf(nav.Stations[nav.Current]) ? "DESCEND" : "ASCEND";
            }
            int level = CrewProgress.Instance != null ? CrewProgress.Instance.Level(UpgradeType.DepthRating) : 0;
            status.text = nav.Travelling ? "IN TRANSIT..."
                : $"SUIT DEPTH RATING: {UpgradeCatalog.DepthRating(level)} M (L{level}).  Upgrade it on the Balance app to go deeper.";
        }
    }
}
