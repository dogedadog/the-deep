using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheDeep.Core;
using TheDeep.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Sonar-style top-down map for the sub crew: divers as coloured blips around the sub (bow up),
    /// plus each diver's depth, distance, tether length/tension and signal strength.
    /// </summary>
    public class DiverMapApp : TerminalApp
    {
        const float MapSize = 400f;
        const float MetersAcross = 90f; // map shows 45 m in every direction
        const float PixelsPerMeter = MapSize / MetersAcross;

        readonly Dictionary<DiverController, (RectTransform blip, Text label)> blips = new();
        readonly Button[] winchButtons = new Button[5];
        readonly DiverController[] winchTargets = new DiverController[5];
        RectTransform map;
        Text info;
        bool open;
        float nextScan;
        DiverController[] divers = System.Array.Empty<DiverController>();

        public override string Title => "Diver Map";
        public override string IconGlyph => "MAP";
        public override Color IconColor => new(0.15f, 0.45f, 0.25f);
        public override Vector2 WindowSize => new(640, 510);

        public override void BuildContent(RectTransform content)
        {
            var mapImage = RetroUI.Panel("Map", content, RetroUI.Screen);
            map = mapImage.rectTransform;
            RetroUI.Place(map, 0, 0, MapSize, MapSize);
            RetroUI.Bevel(map, raised: false);
            map.gameObject.AddComponent<RectMask2D>();

            // Grid lines every 10 m.
            var gridColor = RetroUI.PhosphorDim * new Color(1, 1, 1, 0.5f);
            for (int i = 1; i < 9; i++)
            {
                float p = i / 9f;
                var h = RetroUI.Panel("GridH", map, gridColor).rectTransform;
                h.anchorMin = new Vector2(0, p);
                h.anchorMax = new Vector2(1, p);
                h.sizeDelta = new Vector2(0, 1);
                var v = RetroUI.Panel("GridV", map, gridColor).rectTransform;
                v.anchorMin = new Vector2(p, 0);
                v.anchorMax = new Vector2(p, 1);
                v.sizeDelta = new Vector2(1, 0);
            }

            var sweep = RetroUI.Panel("Sweep", map, RetroUI.Phosphor * new Color(1, 1, 1, 0.6f)).rectTransform;
            sweep.anchorMin = sweep.anchorMax = new Vector2(0.5f, 0.5f);
            sweep.pivot = new Vector2(0f, 0.5f);
            sweep.sizeDelta = new Vector2(MapSize * 0.75f, 2);
            sweep.gameObject.AddComponent<Spin>().DegreesPerSecond = -60f;

            // The sub, drawn to scale (about 17 m long, bow up).
            var sub = RetroUI.Panel("Sub", map, new Color(1f, 0.85f, 0.2f)).rectTransform;
            sub.anchorMin = sub.anchorMax = new Vector2(0.5f, 0.5f);
            sub.sizeDelta = new Vector2(5f * PixelsPerMeter, 16.7f * PixelsPerMeter);
            sub.anchoredPosition = new Vector2(0, 0.55f * PixelsPerMeter);
            var subLabel = RetroUI.Label("SubLabel", map, "SUB", 13, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
            subLabel.rectTransform.anchorMin = subLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            subLabel.rectTransform.anchoredPosition = new Vector2(0, -50);
            subLabel.rectTransform.sizeDelta = new Vector2(60, 20);
            var scale = RetroUI.Label("Scale", map, "GRID 10 M   BOW UP", 12, RetroUI.PhosphorDim, TextAnchor.LowerLeft);
            RetroUI.Stretch(scale.rectTransform, 8, 0, 0, 6);

            info = RetroUI.Readout("Info", content, "", 14);
            RetroUI.Place((RectTransform)info.transform.parent, MapSize + 10, 0, WindowSize.x - 16 - MapSize - 10, MapSize);

            // Winch controls: the crew can reel any diver back in.
            var winchLabel = RetroUI.Label("WinchLabel", content, "WINCH", 15, style: FontStyle.Bold);
            RetroUI.Place(winchLabel.rectTransform, 0, MapSize + 12, 70, 34);
            for (int i = 0; i < winchButtons.Length; i++)
            {
                int slot = i;
                winchButtons[i] = RetroUI.Button("Winch" + i, content, "", () => ToggleWinch(slot), 13);
                RetroUI.Place(winchButtons[i].GetComponent<RectTransform>(), 72 + i * 110, MapSize + 12, 104, 34);
                winchButtons[i].gameObject.SetActive(false);
            }
            Refresh();
        }

        public override void OnOpened() => open = true;
        public override void OnClosed() => open = false;

        void Update()
        {
            if (!open || map == null) return;
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                divers = FindObjectsByType<DiverController>(FindObjectsSortMode.None)
                    .OrderBy(d => d.OwnerClientId).ToArray();
            }
            Refresh();
        }

        void Refresh()
        {
            var sb = new StringBuilder();
            var subPos = TheDeep.Submarine.SubNavigation.SubPosition;
            int deployed = 0;
            foreach (var diver in divers)
            {
                if (diver == null) continue;
                bool out_ = diver.IsDiving;
                var (blip, label) = BlipFor(diver);
                blip.gameObject.SetActive(out_);
                label.gameObject.SetActive(out_);
                if (!out_) continue;
                deployed++;

                var net = diver.GetComponent<PlayerNetwork>();
                var tether = diver.GetComponent<DiverTether>();
                Vector3 rel = diver.transform.position - subPos;
                float dist = new Vector2(rel.x, rel.z).magnitude;
                // Bow (+X) is up on the map, starboard (-Z) is right.
                Vector2 pos = new Vector2(-rel.z, rel.x) * PixelsPerMeter;
                float edge = MapSize * 0.5f - 8f;
                pos = new Vector2(Mathf.Clamp(pos.x, -edge, edge), Mathf.Clamp(pos.y, -edge, edge));
                blip.anchoredPosition = pos;
                blip.GetComponent<Image>().color = net.SuitColor;
                label.rectTransform.anchoredPosition = pos + new Vector2(0, 14);
                label.text = $"D{net.CrewNumber}";

                float signal = SignalModel.Strength(diver.transform.position);
                var scanner = diver.GetComponent<DiverScanner>();
                string tetherText = tether != null && tether.HasRope
                    ? $"{tether.PaidOut:0}/{tether.MaxLength:0} M  T{tether.Tension * 100f:0}%{(tether.IsReeling ? " REEL" : "")}"
                    : "NONE";
                sb.Append($"DIVER {net.CrewNumber}\n");
                sb.Append($" DEPTH  {WorldInfo.DepthAt(diver.transform.position.y):0000} M\n");
                sb.Append($" RANGE  {dist:0} M\n");
                sb.Append($" TETHER {tetherText}\n");
                sb.Append($" SIGNAL {SignalModel.Bars(signal)} {signal * 100f:0}%\n");
                sb.Append($" DATA   {(scanner != null ? scanner.HeldCount : 0)} PKT HELD\n\n");
            }

            UpdateWinchButtons();
            info.text = $"DIVERS OUT: {deployed}\nSUB DEPTH: {WorldInfo.DepthAt(subPos.y):0} M\n\n" +
                        (deployed == 0 ? "-- NO DIVERS DEPLOYED --\n\nDive hatch is at the stern." : sb.ToString());
        }

        void UpdateWinchButtons()
        {
            int slot = 0;
            foreach (var diver in divers)
            {
                if (slot >= winchButtons.Length) break;
                if (diver == null || !diver.IsDiving) continue;
                var tether = diver.GetComponent<DiverTether>();
                if (tether == null) continue;
                winchTargets[slot] = diver;
                var button = winchButtons[slot];
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<Text>().text = $"D{diver.GetComponent<PlayerNetwork>().CrewNumber} {(tether.IsReeling ? "STOP" : "REEL IN")}";
                button.targetGraphic.color = tether.IsReeling ? new Color(1f, 0.75f, 0.4f) : RetroUI.Face;
                slot++;
            }
            for (; slot < winchButtons.Length; slot++)
            {
                winchTargets[slot] = null;
                winchButtons[slot].gameObject.SetActive(false);
            }
        }

        void ToggleWinch(int slot)
        {
            var diver = winchTargets[slot];
            var tether = diver != null ? diver.GetComponent<DiverTether>() : null;
            if (tether != null) tether.SetWinchRpc(!tether.IsReeling);
        }

        (RectTransform, Text) BlipFor(DiverController diver)
        {
            if (blips.TryGetValue(diver, out var existing) && existing.blip != null) return existing;
            var blip = RetroUI.Panel("Blip", map, Color.white).rectTransform;
            blip.anchorMin = blip.anchorMax = new Vector2(0.5f, 0.5f);
            blip.sizeDelta = new Vector2(10, 10);
            var label = RetroUI.Label("BlipLabel", map, "", 13, RetroUI.Phosphor, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(40, 16);
            blips[diver] = (blip, label);
            return (blip, label);
        }
    }

    /// <summary>Rotates a UI element continuously.</summary>
    public class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 90f;
        void Update() => transform.Rotate(0f, 0f, DegreesPerSecond * Time.deltaTime);
    }
}
