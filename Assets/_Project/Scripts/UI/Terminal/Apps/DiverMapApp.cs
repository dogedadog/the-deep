using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheDeep.Core;
using TheDeep.Player;
using TheDeep.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Sonar map for the sub crew, with divers as coloured blips. PROFILE (the default) is a side view
    /// of the shaft below the sub, with the rope limit and clean-signal range straight down; PLAN is the
    /// top-down view around the sub (bow up). Beside it: each diver's air, depth, range, tether and signal.
    /// </summary>
    public class DiverMapApp : TerminalApp
    {
        const float MapSize = 400f;
        const float MetersAcross = 90f; // plan view shows 45 m in every direction
        const float PixelsPerMeter = MapSize / MetersAcross;
        const float ProfilePixelsPerMeter = MapSize / 60f; // profile view shows 30 m either side of the sub
        const float ProfileTop = 10f;       // the sub's level, in pixels below the top of the map
        const float ProfileBottom = 6f;
        const float AnchorBelowSub = 1.22f; // rope fairlead below the sub's origin
        const float SubCenterHeight = 1.3f; // SignalModel.SubCenter above the sub's origin
        const float LostRange = 150f;       // a dead diver this far away was left behind when the sub moved
        const int GridLines = 16;

        static readonly Color SubColor = new(1f, 0.85f, 0.2f);
        static readonly Color RopeColor = new(0.25f, 0.7f, 0.35f);
        static readonly Color SignalColor = new(1f, 0.72f, 0.15f);

        class Blip
        {
            public RectTransform Dot;
            public Image Image;
            public Text Label;
            public int Crew = -1;
            public bool Used;
        }

        readonly Dictionary<DiverController, Blip> planBlips = new();
        readonly Dictionary<DiverController, Blip> profileBlips = new();
        readonly List<DiverController> stale = new();
        readonly Button[] winchButtons = new Button[5];
        readonly DiverController[] winchTargets = new DiverController[5];
        readonly RectTransform[] gridLines = new RectTransform[GridLines];
        readonly Text[] gridLabels = new Text[GridLines];
        readonly StringBuilder sb = new();
        RectTransform map, planLayer, profileLayer, ropeLine, signalLine;
        Text info, ropeLabel, signalLabel, profileScale;
        Button planButton, profileButton;
        bool open, profileView = true;
        float nextScan, cleanSignalBelow;
        // What the profile's labels show now, so their text is only rebuilt when it changes.
        int gridStep, gridCount, gridSubDepth = int.MinValue, ropeShown = -1, signalShown = -1;
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

            planLayer = RetroUI.Stretch(RetroUI.Rect("Plan", map));
            BuildPlan(planLayer);
            profileLayer = RetroUI.Stretch(RetroUI.Rect("Profile", map));
            BuildProfile(profileLayer);

            // View toggle over the map's top-right corner (made after the map so it draws on top).
            planButton = ViewButton("PlanView", content, "PLAN", MapSize - 130, false);
            profileButton = ViewButton("ProfileView", content, "PROFILE", MapSize - 66, true);
            SetView(profileView);

            info = RetroUI.Readout("Info", content, "", 13);
            var infoBox = (RectTransform)info.transform.parent;
            RetroUI.Place(infoBox, MapSize + 10, 0, WindowSize.x - 16 - MapSize - 10, MapSize);
            infoBox.gameObject.AddComponent<RectMask2D>(); // clip instead of spilling over the winch row

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
            cleanSignalBelow = CleanSignalBelow();
            Refresh();
        }

        /// <summary>Top-down view: grid, radar sweep and the sub, bow up.</summary>
        static void BuildPlan(RectTransform layer)
        {
            // Grid lines every 10 m.
            var gridColor = RetroUI.PhosphorDim * new Color(1, 1, 1, 0.5f);
            for (int i = 1; i < 9; i++)
            {
                float p = i / 9f;
                var h = RetroUI.Panel("GridH", layer, gridColor).rectTransform;
                h.anchorMin = new Vector2(0, p);
                h.anchorMax = new Vector2(1, p);
                h.sizeDelta = new Vector2(0, 1);
                var v = RetroUI.Panel("GridV", layer, gridColor).rectTransform;
                v.anchorMin = new Vector2(p, 0);
                v.anchorMax = new Vector2(p, 1);
                v.sizeDelta = new Vector2(1, 0);
            }

            var sweep = RetroUI.Panel("Sweep", layer, RetroUI.Phosphor * new Color(1, 1, 1, 0.6f)).rectTransform;
            sweep.anchorMin = sweep.anchorMax = new Vector2(0.5f, 0.5f);
            sweep.pivot = new Vector2(0f, 0.5f);
            sweep.sizeDelta = new Vector2(MapSize * 0.75f, 2);
            sweep.gameObject.AddComponent<Spin>().DegreesPerSecond = -60f;

            // The sub, drawn to scale (about 17 m long, bow up).
            var sub = RetroUI.Panel("Sub", layer, SubColor).rectTransform;
            sub.anchorMin = sub.anchorMax = new Vector2(0.5f, 0.5f);
            sub.sizeDelta = new Vector2(5f * PixelsPerMeter, 16.7f * PixelsPerMeter);
            sub.anchoredPosition = new Vector2(0, 0.55f * PixelsPerMeter);
            var subLabel = RetroUI.Label("SubLabel", layer, "SUB", 13, SubColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            subLabel.rectTransform.anchorMin = subLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            subLabel.rectTransform.anchoredPosition = new Vector2(0, -50);
            subLabel.rectTransform.sizeDelta = new Vector2(60, 20);
            var scale = RetroUI.Label("Scale", layer, "GRID 10 M   BOW UP", 12, RetroUI.PhosphorDim, TextAnchor.LowerLeft);
            RetroUI.Stretch(scale.rectTransform, 8, 0, 0, 6);
        }

        /// <summary>Side view: the sub at the top, depth lines below it, rope limit and clean-signal lines.</summary>
        void BuildProfile(RectTransform layer)
        {
            var gridColor = RetroUI.PhosphorDim * new Color(1, 1, 1, 0.5f);
            var plumb = RetroUI.Panel("Plumb", layer, gridColor).rectTransform; // straight down from the sub
            plumb.anchorMin = new Vector2(0.5f, 0);
            plumb.anchorMax = new Vector2(0.5f, 1);
            plumb.offsetMin = new Vector2(-0.5f, 0);
            plumb.offsetMax = new Vector2(0.5f, -ProfileTop);

            // A pool of depth lines, placed and labelled every frame in UpdateProfile.
            for (int i = 0; i < GridLines; i++)
            {
                gridLines[i] = DepthLine("GridDepth", layer, gridColor, 1f);
                var label = RetroUI.Label("DepthLabel", layer, "", 11, RetroUI.PhosphorDim, TextAnchor.LowerRight);
                var rt = label.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
                rt.sizeDelta = new Vector2(70, 14);
                gridLabels[i] = label;
            }
            ropeLine = DepthLine("RopeLimit", layer, RopeColor, 2f);
            ropeLabel = LineLabel("RopeLabel", layer, RopeColor);
            signalLine = DepthLine("CleanSignal", layer, SignalColor, 1f);
            signalLabel = LineLabel("SignalLabel", layer, SignalColor);

            // The sub side on (about 17 m long, bow to the right), sitting on the 0 m level.
            var sub = RetroUI.Panel("Sub", layer, SubColor).rectTransform;
            sub.anchorMin = sub.anchorMax = new Vector2(0.5f, 1);
            sub.sizeDelta = new Vector2(16.7f * ProfilePixelsPerMeter, 8);
            sub.anchoredPosition = new Vector2(0.55f * ProfilePixelsPerMeter, -(ProfileTop - 2f));
            var subLabel = RetroUI.Label("SubLabel", layer, "SUB", 11, SubColor, TextAnchor.MiddleRight, FontStyle.Bold);
            var subLabelRt = subLabel.rectTransform;
            subLabelRt.anchorMin = subLabelRt.anchorMax = new Vector2(0.5f, 1);
            subLabelRt.sizeDelta = new Vector2(40, 14);
            subLabelRt.anchoredPosition = new Vector2(-8.35f * ProfilePixelsPerMeter - 20f, -(ProfileTop - 2f));

            profileScale = RetroUI.Label("Scale", layer, "", 12, RetroUI.PhosphorDim, TextAnchor.LowerLeft);
            RetroUI.Stretch(profileScale.rectTransform, 8, 0, 0, 6);
        }

        static RectTransform DepthLine(string name, Transform parent, Color color, float thickness)
        {
            var rt = RetroUI.Panel(name, parent, color).rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(0, thickness);
            return rt;
        }

        static Text LineLabel(string name, Transform parent, Color color)
        {
            var t = RetroUI.Label(name, parent, "", 11, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(260, 14);
            return t;
        }

        Button ViewButton(string name, Transform parent, string text, float x, bool profile)
        {
            var button = RetroUI.Button(name, parent, text, () => SetView(profile), 11);
            RetroUI.Place(button.GetComponent<RectTransform>(), x, 6, 60, 20);
            button.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            return button;
        }

        void SetView(bool profile)
        {
            profileView = profile;
            planLayer.gameObject.SetActive(!profile);
            profileLayer.gameObject.SetActive(profile);
            // The selected view's button looks pressed in.
            planButton.targetGraphic.color = profile ? RetroUI.Face : RetroUI.Shadow;
            profileButton.targetGraphic.color = profile ? RetroUI.Shadow : RetroUI.Face;
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
                    .OrderBy(CrewOf).ToArray();
                RemoveDestroyed(planBlips);
                RemoveDestroyed(profileBlips);
                cleanSignalBelow = CleanSignalBelow(); // changes only with the walkie upgrade or a new station
            }
            Refresh();
        }

        static int CrewOf(DiverController diver)
        {
            var net = diver.GetComponent<PlayerNetwork>();
            return net != null ? net.CrewNumber : int.MaxValue;
        }

        void Refresh()
        {
            var subPos = TheDeep.Submarine.SubNavigation.SubPosition;
            float ropeMax = UpgradeCatalog.RopeLength(CrewProgress.Instance != null ? CrewProgress.Instance.Level(UpgradeType.RopeLength) : 0);

            // The profile reaches past the rope limit, or the deepest diver if one is further down.
            float deepest = 0f;
            foreach (var diver in divers)
                if (diver != null && diver.IsDiving && !IsLost(diver, subPos))
                    deepest = Mathf.Max(deepest, subPos.y - diver.transform.position.y);
            float viewDepth = Mathf.Ceil(Mathf.Max(ropeMax + 10f, deepest + 10f) / 10f) * 10f;
            float metersToPixels = (MapSize - ProfileTop - ProfileBottom) / viewDepth;
            UpdateProfile(subPos.y, viewDepth, metersToPixels, ropeMax);

            foreach (var pair in planBlips) pair.Value.Used = false;
            foreach (var pair in profileBlips) pair.Value.Used = false;
            sb.Clear();
            int deployed = 0;
            float edge = MapSize * 0.5f - 8f;
            foreach (var diver in divers)
            {
                if (diver == null || !diver.IsDiving) continue;
                deployed++;

                var net = diver.GetComponent<PlayerNetwork>();
                int crew = net != null ? net.CrewNumber : 0;
                Vector3 position = diver.transform.position;
                float depth = WorldInfo.DepthAt(position.y);
                var health = diver.GetComponent<DiverHealth>();
                bool dead = health != null && health.IsDead;
                if (dead && Vector3.Distance(position, subPos) > LostRange)
                {
                    // Left behind when the sub moved on: no blip, just a note.
                    sb.Append($"<color=#ff5040>D{crew} NO VITALS (LOST {depth:0} M)</color>\n");
                    continue;
                }

                Color suit = net != null ? net.SuitColor : Color.white;
                Vector3 rel = position - subPos;
                // Plan: bow (+X) is up, starboard (-Z) is right.
                Vector2 plan = new Vector2(-rel.z, rel.x) * PixelsPerMeter;
                plan = new Vector2(Mathf.Clamp(plan.x, -edge, edge), Mathf.Clamp(plan.y, -edge, edge));
                ShowBlip(BlipFor(diver, planBlips, planLayer), plan, suit, crew);
                // Profile: bow to the right, metres below the sub downward.
                float below = subPos.y - position.y;
                float y = Mathf.Clamp(ProfileTop + below * metersToPixels, 6f, MapSize - 8f);
                var profile = new Vector2(Mathf.Clamp(rel.x * ProfilePixelsPerMeter, -edge, edge), MapSize * 0.5f - y);
                ShowBlip(BlipFor(diver, profileBlips, profileLayer), profile, suit, crew);

                // Four short lines per diver so five divers fit the column.
                float air = health != null ? health.Air01 : 1f;
                if (dead) sb.Append($"<color=#ff5040>D{crew} !! NO VITALS !!</color>\n");
                else if (air < 0.25f) sb.Append($"<color=#ff5040>D{crew} AIR {air * 100f:0}%  {depth:0} M</color>\n");
                else sb.Append($"D{crew} AIR {air * 100f:0}%  {depth:0} M\n");

                // Range is 3D to the hull centre, the same distance the signal strength uses.
                float range = Vector3.Distance(position, SignalModel.SubCenter);
                sb.Append(below >= 0f ? $"RNG {range:0} M  BELOW {below:0} M\n" : $"RNG {range:0} M  ABOVE {-below:0} M\n");

                var tether = diver.GetComponent<DiverTether>();
                sb.Append(tether != null && tether.HasRope
                    ? $"ROPE {tether.PaidOut:0}/{tether.MaxLength:0} T{tether.Tension * 100f:0}%\n"
                    : "ROPE NONE\n");

                float signal = SignalModel.Strength(position);
                var scanner = diver.GetComponent<DiverScanner>();
                int held = scanner != null ? scanner.HeldCount : 0;
                sb.Append(signal < SignalModel.CorruptionThreshold
                    ? $"<color=#ffc040>SIG {SignalModel.Bars(signal)} {signal * 100f:0}% {held} PKT</color>\n"
                    : $"SIG {SignalModel.Bars(signal)} {signal * 100f:0}% {held} PKT\n");
            }
            HideUnused(planBlips);
            HideUnused(profileBlips);

            UpdateWinchButtons();
            string header = $"DIVERS OUT {deployed}   SUB {WorldInfo.DepthAt(subPos.y):0} M\n";
            info.text = deployed == 0
                ? header + "\n-- NO DIVERS DEPLOYED --\n\nDive hatch is at the stern."
                : header + sb.ToString();
        }

        /// <summary>A dead diver left behind when the sub moved on: no blip here, and no crew alert on the desktop.</summary>
        public static bool IsLost(DiverController diver, Vector3 subPos)
        {
            var health = diver.GetComponent<DiverHealth>();
            return health != null && health.IsDead && Vector3.Distance(diver.transform.position, subPos) > LostRange;
        }

        /// <summary>Profile depth lines (absolute depth labels), the rope limit and the clean-signal limit.</summary>
        void UpdateProfile(float subY, float viewDepth, float metersToPixels, float ropeMax)
        {
            int step = viewDepth > 150f ? 20 : 10;
            int count = Mathf.Min(GridLines, Mathf.FloorToInt(viewDepth / step));
            int subDepth = Mathf.RoundToInt(WorldInfo.DepthAt(subY));
            bool relabel = step != gridStep || count != gridCount || subDepth != gridSubDepth;
            for (int i = 0; i < GridLines; i++)
            {
                bool show = i < count;
                gridLines[i].gameObject.SetActive(show);
                gridLabels[i].gameObject.SetActive(show);
                if (!show) continue;
                int metres = (i + 1) * step;
                float y = ProfileTop + metres * metersToPixels;
                gridLines[i].anchoredPosition = new Vector2(0f, -y);
                gridLabels[i].rectTransform.anchoredPosition = new Vector2(-4f, -(y - 14f));
                if (relabel) gridLabels[i].text = $"{subDepth + metres} M";
            }
            if (step != gridStep) profileScale.text = $"GRID {step} M   SIDE VIEW";
            gridStep = step;
            gridCount = count;
            gridSubDepth = subDepth;

            // Both limits are distances from a point on the hull, so the lines show the reach straight down.
            float ropeBelow = ropeMax + AnchorBelowSub;
            // The shallower line is labelled above, the deeper one below, so close labels never overlap.
            bool ropeHigher = ropeBelow < cleanSignalBelow;
            PlaceLine(ropeLine, ropeLabel, ropeBelow, viewDepth, metersToPixels, labelAbove: ropeHigher);
            PlaceLine(signalLine, signalLabel, cleanSignalBelow, viewDepth, metersToPixels, labelAbove: !ropeHigher);
            int ropeMetres = Mathf.RoundToInt(ropeBelow);
            if (ropeMetres != ropeShown)
            {
                ropeShown = ropeMetres;
                ropeLabel.text = $"ROPE LIMIT: {ropeMetres} M STRAIGHT DOWN";
            }
            int signalMetres = Mathf.RoundToInt(cleanSignalBelow);
            if (signalMetres != signalShown)
            {
                signalShown = signalMetres;
                signalLabel.text = $"CLEAN SIGNAL: {signalMetres} M STRAIGHT DOWN";
            }
        }

        static void PlaceLine(RectTransform line, Text label, float below, float viewDepth, float metersToPixels, bool labelAbove)
        {
            bool show = below <= viewDepth;
            line.gameObject.SetActive(show);
            label.gameObject.SetActive(show);
            if (!show) return;
            float y = ProfileTop + below * metersToPixels;
            line.anchoredPosition = new Vector2(0f, -y);
            label.rectTransform.anchoredPosition = new Vector2(6f, labelAbove ? -(y - 15f) : -(y + 2f));
        }

        /// <summary>Metres below the sub, straight down, where transmitted data starts arriving corrupted.</summary>
        static float CleanSignalBelow()
        {
            Vector3 center = SignalModel.SubCenter;
            float lo = 0f, hi = 400f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (SignalModel.Strength(center + Vector3.down * mid) < SignalModel.CorruptionThreshold) hi = mid;
                else lo = mid;
            }
            return Mathf.Max(0f, lo - SubCenterHeight);
        }

        void UpdateWinchButtons()
        {
            int slot = 0;
            foreach (var diver in divers)
            {
                if (slot >= winchButtons.Length) break;
                if (diver == null || !diver.IsDiving) continue;
                // Reeling a dead diver does nothing: the body is kinematic.
                var health = diver.GetComponent<DiverHealth>();
                if (health != null && health.IsDead) continue;
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
            if (tether != null && tether.IsSpawned) tether.SetWinchRpc(!tether.IsReeling);
        }

        static void ShowBlip(Blip blip, Vector2 position, Color color, int crew)
        {
            blip.Used = true;
            blip.Dot.gameObject.SetActive(true);
            blip.Label.gameObject.SetActive(true);
            blip.Dot.anchoredPosition = position;
            blip.Image.color = color;
            blip.Label.rectTransform.anchoredPosition = position + new Vector2(0, 14);
            if (blip.Crew == crew) return;
            blip.Crew = crew;
            blip.Label.text = $"D{crew}";
        }

        /// <summary>Blips not updated this refresh (diver aboard, lost or gone) are hidden.</summary>
        static void HideUnused(Dictionary<DiverController, Blip> blips)
        {
            foreach (var pair in blips)
            {
                if (pair.Value.Used) continue;
                pair.Value.Dot.gameObject.SetActive(false);
                pair.Value.Label.gameObject.SetActive(false);
            }
        }

        /// <summary>Drops the blips of players who left the session (their DiverController is destroyed).</summary>
        void RemoveDestroyed(Dictionary<DiverController, Blip> blips)
        {
            stale.Clear();
            foreach (var pair in blips)
                if (pair.Key == null) stale.Add(pair.Key);
            foreach (var key in stale)
            {
                if (!blips.Remove(key, out Blip blip)) continue;
                if (blip.Dot != null) Destroy(blip.Dot.gameObject);
                if (blip.Label != null) Destroy(blip.Label.gameObject);
            }
        }

        Blip BlipFor(DiverController diver, Dictionary<DiverController, Blip> blips, RectTransform layer)
        {
            if (blips.TryGetValue(diver, out Blip existing) && existing.Dot != null) return existing;
            var dot = RetroUI.Panel("Blip", layer, Color.white);
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            dot.rectTransform.sizeDelta = new Vector2(10, 10);
            var label = RetroUI.Label("BlipLabel", layer, "", 13, RetroUI.Phosphor, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(40, 16);
            var blip = new Blip { Dot = dot.rectTransform, Image = dot, Label = label };
            blips[diver] = blip;
            return blip;
        }
    }

    /// <summary>Rotates a UI element continuously.</summary>
    public class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 90f;
        void Update() => transform.Rotate(0f, 0f, DegreesPerSecond * Time.deltaTime);
    }
}
