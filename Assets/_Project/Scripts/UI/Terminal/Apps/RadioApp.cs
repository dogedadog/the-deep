using System.Linq;
using System.Text;
using TheDeep.Core;
using TheDeep.Voice;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// The sub's radio monitor: who's on the channel, where they are, their signal, who is
    /// transmitting right now (with a live level meter), and a log of recent transmissions.
    /// </summary>
    public class RadioApp : TerminalApp
    {
        const int MaxCrew = 5;
        const float RowHeight = 40f;

        readonly Image[] txLights = new Image[MaxCrew];
        readonly Image[] meters = new Image[MaxCrew];
        readonly Text[] rows = new Text[MaxCrew];
        readonly GameObject[] rowRoots = new GameObject[MaxCrew];
        Text log, footer;
        bool open;

        public override string Title => "Radio";
        public override string IconGlyph => "RAD";
        public override Color IconColor => new(0.6f, 0.2f, 0.15f);
        public override Vector2 WindowSize => new(600, 470);

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;
            var header = RetroUI.Label("Header", content, "CHANNEL 1  -  DIVE TEAM   (143.600 MHz)", 16, style: FontStyle.Bold);
            RetroUI.Place(header.rectTransform, 0, 0, width, 22);

            var panel = RetroUI.Panel("Crew", content, RetroUI.Screen);
            RetroUI.Place(panel.rectTransform, 0, 26, width, MaxCrew * RowHeight + 8);
            RetroUI.Bevel(panel.rectTransform, raised: false);
            for (int i = 0; i < MaxCrew; i++)
            {
                var row = RetroUI.Rect("Row" + i, panel.transform);
                RetroUI.Place(row, 4, 4 + i * RowHeight, width - 8, RowHeight - 4);
                rowRoots[i] = row.gameObject;
                txLights[i] = RetroUI.Panel("TX", row, new Color(0.25f, 0.05f, 0.04f));
                RetroUI.Place(txLights[i].rectTransform, 8, 8, 20, 20);
                rows[i] = RetroUI.Label("Text", row, "", 15, RetroUI.Phosphor);
                RetroUI.Place(rows[i].rectTransform, 36, 0, width - 200, RowHeight - 4);
                var meterBg = RetroUI.Panel("MeterBg", row, new Color(0.05f, 0.12f, 0.07f));
                RetroUI.Place(meterBg.rectTransform, width - 160, 10, 140, 16);
                meters[i] = RetroUI.Panel("Meter", meterBg.transform, RetroUI.Phosphor);
                RetroUI.Place(meters[i].rectTransform, 0, 0, 0, 16);
            }

            var logHeading = RetroUI.Label("LogHeading", content, "TRANSMISSION LOG", 15, style: FontStyle.Bold);
            RetroUI.Place(logHeading.rectTransform, 0, 34 + MaxCrew * RowHeight, width, 20);
            log = RetroUI.Readout("Log", content, "", 13);
            RetroUI.Place((RectTransform)log.transform.parent, 0, 56 + MaxCrew * RowHeight, width, 116);
            footer = RetroUI.Label("Footer", content, "", 13);
            RetroUI.Place(footer.rectTransform, 0, 176 + MaxCrew * RowHeight, width, 22);
            Refresh();
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (open) Refresh();
        }

        void Refresh()
        {
            var crew = PlayerVoice.All.OrderBy(v => v.CrewNumber).ToList();
            float meterWidth = 140f;
            for (int i = 0; i < MaxCrew; i++)
            {
                bool show = i < crew.Count;
                rowRoots[i].SetActive(show);
                if (!show) continue;
                var v = crew[i];
                bool live = v.RadioHeardRecently;
                float signal = v.IsDiving ? SignalModel.Strength(v.transform.position) : 1f;
                string where = v.IsDiving ? $"DIVING  {WorldInfo.DepthAt(v.transform.position.y):0} M" : "ABOARD";
                rows[i].text = $"D{v.CrewNumber}{(v.IsOwner ? " (YOU)" : "")}   {where,-16}  SIG {SignalModel.Bars(signal)}  {(live ? "TRANSMITTING" : v.Talking ? "talking" : "")}";
                rows[i].color = live ? new Color(1f, 0.55f, 0.45f) : RetroUI.Phosphor;
                txLights[i].color = live ? new Color(1f, 0.15f, 0.1f) : new Color(0.25f, 0.05f, 0.04f);
                float level = v.IsOwner ? (live ? 0.7f : 0f) : v.ReceivedLevel;
                meters[i].rectTransform.sizeDelta = new Vector2(meterWidth * Mathf.Clamp01(level), 16);
                meters[i].color = live ? new Color(1f, 0.5f, 0.3f) : RetroUI.Phosphor;
            }

            if (Time.frameCount % 15 != 0) return;
            var sb = new StringBuilder();
            if (PlayerVoice.RadioLog.Count == 0) sb.Append("(channel quiet)");
            for (int i = PlayerVoice.RadioLog.Count - 1, shown = 0; i >= 0 && shown < 7; i--, shown++)
            {
                var e = PlayerVoice.RadioLog[i];
                sb.Append($"{e.Time}   D{e.Crew}   {e.Seconds:0.0}s   SIG {e.Signal * 100f:0}%{(e.Signal < SignalModel.CorruptionThreshold ? "  (BROKEN UP)" : "")}\n");
            }
            log.text = sb.ToString();
            footer.text = $"Hold {Controls.Label(GameAction.Radio)} to talk on the radio (works while seated here).  " +
                          $"{(PlayerVoice.MicMuted ? "Your mic is muted." : "")}";
        }
    }
}
