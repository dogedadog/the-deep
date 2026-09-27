using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Data;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Where divers' radioed-in data lands. The crew must LOG each packet (REPAIR corrupted ones,
    /// which takes longer), then SUBMIT logged data for credits. Shared by everyone via ExpeditionState.
    /// </summary>
    public class CommsApp : TerminalApp
    {
        const int Rows = 7;
        const float RowHeight = 38f;
        static readonly Color FooterColor = new(1f, 0.85f, 0.3f);

        readonly Row[] rows = new Row[Rows];
        readonly List<DataPacket> visible = new();
        Text status, summary, empty;
        Button submit;
        bool open;
        float nextRefresh;

        public override string Title => "Comms / Data";
        public override string IconGlyph => "((o))";
        public override Color IconColor => new(0.55f, 0.35f, 0.15f);
        public override Vector2 WindowSize => new(640, 420);

        class Row
        {
            public GameObject Root;
            public Text Label, State;
            public Image Bar;
            public Button Action;
            public Text ActionText;
            public int PacketId;
            /// <summary>"+N MORE" line instead of a packet: no bar, no button.</summary>
            public bool Footer;
        }

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;
            status = RetroUI.Label("Status", content, "", 15, style: FontStyle.Bold);
            RetroUI.Place(status.rectTransform, 0, 0, width, 22);

            var list = RetroUI.Panel("List", content, RetroUI.Screen);
            RetroUI.Place(list.rectTransform, 0, 26, width, Rows * RowHeight + 8);
            RetroUI.Bevel(list.rectTransform, raised: false);
            for (int i = 0; i < Rows; i++) rows[i] = BuildRow(list.transform, i, width - 8);
            empty = RetroUI.Label("Empty", list.transform, "", 15, RetroUI.PhosphorDim, TextAnchor.UpperLeft);
            RetroUI.Stretch(empty.rectTransform, 12, 10, 12, 10);

            float y = 26 + Rows * RowHeight + 16;
            summary = RetroUI.Label("Summary", content, "", 15);
            RetroUI.Place(summary.rectTransform, 0, y, width - 230, 40);
            submit = RetroUI.Button("Submit", content, "SUBMIT LOGGED DATA", () => ExpeditionState.Instance?.SubmitRpc(), 16);
            RetroUI.Place(submit.GetComponent<RectTransform>(), width - 220, y, 220, 40);
            Refresh();
        }

        Row BuildRow(Transform parent, int index, float width)
        {
            var row = new Row();
            var bg = RetroUI.Panel("Row" + index, parent, new Color(1, 1, 1, index % 2 == 0 ? 0.03f : 0f));
            RetroUI.Place(bg.rectTransform, 4, 4 + index * RowHeight, width, RowHeight - 2);
            row.Root = bg.gameObject;

            row.Bar = RetroUI.Panel("Progress", bg.transform, new Color(0.2f, 0.8f, 0.35f, 0.25f));
            RetroUI.Place(row.Bar.rectTransform, 0, 0, 0, RowHeight - 2);
            row.Label = RetroUI.Label("Label", bg.transform, "", 15, RetroUI.Phosphor);
            RetroUI.Place(row.Label.rectTransform, 8, 0, width - 250, RowHeight - 2);
            row.State = RetroUI.Label("State", bg.transform, "", 14, RetroUI.Phosphor, TextAnchor.MiddleRight, FontStyle.Bold);
            RetroUI.Place(row.State.rectTransform, width - 240, 0, 130, RowHeight - 2);

            row.Action = RetroUI.Button("Action", bg.transform, "LOG", () =>
            {
                // The footer row has no packet behind it and must never send LOG.
                var state = ExpeditionState.Instance;
                if (!row.Footer && row.PacketId > 0 && state != null && state.IsSpawned) state.LogPacketRpc(row.PacketId);
            }, 14);
            RetroUI.Place(row.Action.GetComponent<RectTransform>(), width - 100, 4, 94, RowHeight - 10);
            row.ActionText = row.Action.GetComponentInChildren<Text>();
            return row;
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (!open || status == null) return;
            // Progress bars move smoothly; the rest only needs a few refreshes a second.
            if (Time.time >= nextRefresh)
            {
                nextRefresh = Time.time + 0.2f;
                Refresh();
            }
            UpdateProgressBars();
        }

        void Refresh()
        {
            var state = ExpeditionState.Instance;
            bool online = state != null && state.IsSpawned;
            visible.Clear();
            int pending = 0, logged = 0, loggedValue = 0, hidden = 0, hiddenNeedLog = 0;
            if (online)
            {
                var packets = state.Packets;
                for (int i = 0; i < packets.Count; i++)
                {
                    var p = packets[i];
                    if (p.Status == PacketStatus.Submitted) continue;
                    if (p.Status == PacketStatus.Logged) { logged++; loggedValue += p.Value; }
                    else pending++;
                }

                // Oldest first, so new arrivals go at the bottom and rows don't shift under the cursor.
                // Too many to fit: leave out LOGGED rows (the summary counts them), then give the
                // last row over to a "+N MORE" footer.
                bool skipLogged = pending + logged > Rows;
                int candidates = skipLogged ? pending : pending + logged;
                int slots = candidates > Rows ? Rows - 1 : Rows;
                for (int i = 0; i < packets.Count; i++)
                {
                    var p = packets[i];
                    if (p.Status == PacketStatus.Submitted || (skipLogged && p.Status == PacketStatus.Logged)) continue;
                    if (visible.Count < slots)
                    {
                        visible.Add(p);
                        continue;
                    }
                    hidden++;
                    if (p.Status == PacketStatus.Pending || p.Status == PacketStatus.Corrupted) hiddenNeedLog++;
                }
            }

            status.text = online
                ? $"CHANNEL 1  |  AWAITING LOG: {pending}  |  LOGGED: {logged}  |  CREDITS: {state.Credits} CR"
                : "DATA LINK OFFLINE";
            bool isEmpty = visible.Count == 0;
            empty.gameObject.SetActive(isEmpty);
            if (isEmpty)
            {
                // Built here because Scan and Transmit can be rebound in Settings.
                empty.text = "> Awaiting transmissions from dive team...\n" +
                             $"> Divers: scan with {Controls.Label(GameAction.Scan)}, transmit with {Controls.Label(GameAction.Transmit)}.\n" +
                             $"> Data sent below {SignalModel.CorruptionThreshold * 100f:0}% signal arrives CORRUPTED and must be REPAIRED.";
            }

            for (int i = 0; i < Rows; i++)
            {
                var row = rows[i];
                row.Footer = hidden > 0 && i == visible.Count;
                bool show = i < visible.Count || row.Footer;
                row.Root.SetActive(show);
                if (!show) continue;
                if (row.Footer)
                {
                    row.PacketId = 0;
                    row.Label.text = $"+{hidden} MORE  ({hiddenNeedLog} NEED LOG)";
                    row.Label.color = FooterColor;
                    row.State.text = "";
                    row.Action.gameObject.SetActive(false);
                    row.Bar.rectTransform.sizeDelta = new Vector2(0f, RowHeight - 2);
                    continue;
                }

                var p = visible[i];
                row.PacketId = p.Id;
                row.Label.color = RetroUI.Phosphor;
                row.Label.text = $"[D{p.Diver}] {p.Title}  {ScanTarget.ClassFor(p.Value)}  {p.Value} CR";
                (string text, Color color, string action) info = p.Status switch
                {
                    PacketStatus.Pending => ("RECEIVED", RetroUI.Phosphor, "LOG"),
                    PacketStatus.Corrupted => ("CORRUPTED", new Color(1f, 0.4f, 0.3f), "REPAIR"),
                    PacketStatus.Logging => ("LOGGING...", new Color(1f, 0.85f, 0.3f), null),
                    PacketStatus.Repairing => ("REPAIRING...", new Color(1f, 0.6f, 0.3f), null),
                    _ => ("LOGGED", new Color(0.5f, 1f, 0.6f), null),
                };
                row.State.text = info.text;
                row.State.color = info.color;
                row.Action.gameObject.SetActive(info.action != null);
                if (info.action != null) row.ActionText.text = info.action;
                // Corrupted data shows as garbled until repaired.
                if (p.Status == PacketStatus.Corrupted || p.Status == PacketStatus.Repairing)
                    row.Label.text = Garble(row.Label.text, p.Id);
            }

            submit.interactable = logged > 0;
            summary.text = online
                ? $"READY TO SUBMIT: {logged} PACKET(S) = {loggedValue} CR\nSUBMITTED THIS EXPEDITION: {state.SubmittedCount}"
                : "";
        }

        void UpdateProgressBars()
        {
            var state = ExpeditionState.Instance;
            float width = WindowSize.x - 24;
            // Only packet rows: the footer (row index visible.Count) has no bar.
            for (int i = 0; i < visible.Count && i < Rows; i++)
            {
                if (rows[i].Footer) continue;
                var p = visible[i];
                float fill = p.Status is PacketStatus.Logging or PacketStatus.Repairing && state != null ? state.Progress(p)
                    : p.Status == PacketStatus.Logged ? 1f : 0f;
                rows[i].Bar.rectTransform.sizeDelta = new Vector2(width * fill, RowHeight - 2);
            }
        }

        /// <summary>Replaces some characters with noise, deterministically per packet.</summary>
        static string Garble(string text, int seed)
        {
            var chars = text.ToCharArray();
            var rng = new System.Random(seed);
            const string noise = "#%&@?/\\|=+*";
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] != ' ' && rng.NextDouble() < 0.35) chars[i] = noise[rng.Next(noise.Length)];
            return new string(chars);
        }
    }
}
