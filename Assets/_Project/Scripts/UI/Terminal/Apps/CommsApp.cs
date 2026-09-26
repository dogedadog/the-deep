using UnityEngine;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>Placeholder: where divers' radioed-in data packets will be logged and submitted (step 3).</summary>
    public class CommsApp : TerminalApp
    {
        public override string Title => "Comms / Data";
        public override string IconGlyph => "((o))";
        public override Color IconColor => new(0.55f, 0.35f, 0.15f);
        public override Vector2 WindowSize => new(600, 420);

        public override void BuildContent(RectTransform content)
        {
            var status = RetroUI.Label("Status", content, "CHANNEL 1  |  WALKIE LINK: IDLE  |  PACKETS PENDING: 0", 15);
            RetroUI.Place(status.rectTransform, 0, 0, WindowSize.x - 16, 24);

            var log = RetroUI.Readout("Log", content,
                $"[{RetroUI.Timestamp()}] {TerminalOS.OsName} DATA LINK READY\n" +
                "> Awaiting transmissions from dive team...\n" +
                "> No divers deployed.\n\n" +
                "_", 15);
            RetroUI.Stretch((RectTransform)log.transform.parent, 0, 30, 0, 50);

            var logButton = RetroUI.DisabledButton("LogPacket", content, "LOG PACKET");
            var submit = RetroUI.DisabledButton("Submit", content, "SUBMIT DATA");
            var lr = logButton.GetComponent<RectTransform>();
            var sr = submit.GetComponent<RectTransform>();
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0, 0);
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(1, 0);
            lr.sizeDelta = sr.sizeDelta = new Vector2(170, 36);
        }
    }
}
