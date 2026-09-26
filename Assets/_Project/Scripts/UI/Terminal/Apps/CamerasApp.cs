using System;
using System.Linq;
using TheDeep.Submarine;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Live feed from the hull cameras, dressed up as a subsea CCTV monitor
    /// (REC light, timestamp, depth/heading readout, viewfinder brackets). Only the selected
    /// camera renders, and only while this window is open.
    /// </summary>
    public class CamerasApp : TerminalApp
    {
        const int FeedWidth = 384, FeedHeight = 240; // deliberately low-res
        const float FeedW = 624f, FeedH = 390f;
        const float SwitchStatic = 0.35f;

        [SerializeField] Material feedMaterial;
        [SerializeField, Tooltip("Depth of the sub's origin below the surface, for the readout.")]
        float surfaceDepth = 1280f;

        SubCamera[] cameras = Array.Empty<SubCamera>();
        Button[] numberButtons = Array.Empty<Button>();
        RenderTexture feed;
        Material material;
        Text cameraLabel, timeLabel, dataLabel, signalLabel;
        GameObject recDot, noSignal;
        int current;
        bool open;
        float staticUntil;

        public override string Title => "Ext. Cameras";
        public override string IconGlyph => "CAM";
        public override Color IconColor => new(0.5f, 0.12f, 0.1f);
        public override Vector2 WindowSize => new(640, 500);

        public override void BuildContent(RectTransform content)
        {
            cameras = FindObjectsByType<SubCamera>(FindObjectsSortMode.None).OrderBy(c => c.Number).ToArray();
            feed = new RenderTexture(FeedWidth, FeedHeight, 24) { filterMode = FilterMode.Point, name = "CCTVFeed" };

            var frame = RetroUI.Panel("FeedFrame", content, Color.black);
            RetroUI.Place(frame.rectTransform, 0, 0, FeedW, FeedH);
            RetroUI.Bevel(frame.rectTransform, raised: false, width: 3);

            var screen = RetroUI.Stretch(RetroUI.Rect("Feed", frame.transform), 3, 3, 3, 3);
            var image = screen.gameObject.AddComponent<RawImage>();
            image.texture = feed;
            image.raycastTarget = false;
            if (feedMaterial != null)
            {
                material = new Material(feedMaterial);
                image.material = material;
            }

            BuildOverlay(screen);
            BuildControls(content);
        }

        void BuildOverlay(RectTransform screen)
        {
            var white = new Color(0.95f, 0.97f, 0.92f, 0.9f);

            cameraLabel = OverlayText("Camera", screen, "", 20, TextAnchor.UpperLeft, 30, 16);
            timeLabel = OverlayText("Time", screen, "", 16, TextAnchor.LowerLeft, 30, 16);
            dataLabel = OverlayText("Data", screen, "", 16, TextAnchor.LowerRight, 30, 16);
            signalLabel = OverlayText("Signal", screen, "", 14, TextAnchor.UpperRight, 30, 42);

            // Blinking REC indicator, top right.
            var rec = RetroUI.Rect("Rec", screen);
            rec.anchorMin = rec.anchorMax = rec.pivot = new Vector2(1, 1);
            rec.anchoredPosition = new Vector2(-30, -16);
            rec.sizeDelta = new Vector2(80, 22);
            recDot = RetroUI.Panel("Dot", rec, new Color(1f, 0.1f, 0.08f)).gameObject;
            RetroUI.Place((RectTransform)recDot.transform, 4, 5, 12, 12);
            var recText = RetroUI.Label("Text", rec, "REC", 18, white, TextAnchor.MiddleRight, FontStyle.Bold);
            RetroUI.Stretch(recText.rectTransform);
            recText.gameObject.AddComponent<Shadow>();

            // Viewfinder corner brackets and centre cross.
            const float inset = 12f, length = 30f, thick = 2f;
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
            {
                Vector2 dir = new(corner.x == 0 ? 1 : -1, corner.y == 0 ? 1 : -1);
                Vector2 origin = new(dir.x * inset, dir.y * inset);
                Line(screen, corner, origin + new Vector2(dir.x * length * 0.5f, 0), new Vector2(length, thick), white);
                Line(screen, corner, origin + new Vector2(0, dir.y * length * 0.5f), new Vector2(thick, length), white);
            }
            var middle = new Vector2(0.5f, 0.5f);
            Line(screen, middle, Vector2.zero, new Vector2(18, thick), white * new Color(1, 1, 1, 0.6f));
            Line(screen, middle, Vector2.zero, new Vector2(thick, 18), white * new Color(1, 1, 1, 0.6f));

            // Shown when there are no cameras at all.
            noSignal = OverlayText("NoSignal", screen, "NO SIGNAL", 34, TextAnchor.MiddleCenter, 0, 0).gameObject;
            noSignal.SetActive(false);
        }

        void BuildControls(RectTransform content)
        {
            const float y = FeedH + 10, h = 36;
            var prev = RetroUI.Button("Prev", content, "<  PREV", () => Select(current - 1), 16);
            RetroUI.Place(prev.GetComponent<RectTransform>(), 0, y, 100, h);
            var next = RetroUI.Button("Next", content, "NEXT  >", () => Select(current + 1), 16);
            RetroUI.Place(next.GetComponent<RectTransform>(), FeedW - 100, y, 100, h);

            numberButtons = new Button[cameras.Length];
            float width = Mathf.Min(56f, (FeedW - 230f) / Mathf.Max(1, cameras.Length) - 6f);
            float start = (FeedW - cameras.Length * (width + 6f) + 6f) * 0.5f;
            for (int i = 0; i < cameras.Length; i++)
            {
                int index = i;
                numberButtons[i] = RetroUI.Button("Cam" + (i + 1), content, cameras[i].Number.ToString(), () => Select(index), 18);
                RetroUI.Place(numberButtons[i].GetComponent<RectTransform>(), start + i * (width + 6f), y, width, h);
            }
        }

        Text OverlayText(string name, RectTransform parent, string text, int size, TextAnchor anchor, float padX, float padY)
        {
            var label = RetroUI.Label(name, parent, text, size, new Color(0.95f, 0.97f, 0.92f, 0.9f), anchor, FontStyle.Bold);
            RetroUI.Stretch(label.rectTransform, padX, padY, padX, padY);
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            return label;
        }

        static void Line(RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rt = RetroUI.Panel("Line", parent, color).rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public override void OnOpened()
        {
            open = true;
            Select(current);
        }

        public override void OnClosed()
        {
            open = false;
            foreach (var cam in cameras) cam.SetLive(false, null);
        }

        void Select(int index)
        {
            noSignal.SetActive(cameras.Length == 0);
            if (cameras.Length == 0) return;

            current = (index % cameras.Length + cameras.Length) % cameras.Length;
            for (int i = 0; i < cameras.Length; i++)
            {
                bool live = open && i == current;
                cameras[i].SetLive(live, live ? feed : null);
                // Selected button looks pressed in.
                numberButtons[i].targetGraphic.color = i == current ? RetroUI.Shadow : RetroUI.Face;
            }
            staticUntil = Time.time + SwitchStatic;
        }

        void Update()
        {
            if (!open || cameras.Length == 0) return;

            if (material != null)
                material.SetFloat("_Static", Mathf.Clamp01((staticUntil - Time.time) / SwitchStatic));
            recDot.SetActive(Time.time % 1.2f < 0.7f);

            SubCamera cam = cameras[current];
            Transform tr = cam.transform;
            float depth = surfaceDepth - tr.position.y;
            float heading = Mathf.Repeat(Mathf.Atan2(tr.forward.x, tr.forward.z) * Mathf.Rad2Deg, 360f);
            float tilt = Mathf.Asin(Mathf.Clamp(tr.forward.y, -1f, 1f)) * Mathf.Rad2Deg; // negative = looking down

            cameraLabel.text = $"CAM {cam.Number:00}   {cam.Label}";
            timeLabel.text = DateTime.Now.ToString("yyyy-MM-dd   HH:mm:ss");
            dataLabel.text = $"DEPTH {depth:0000.0} M\nHDG {heading:000}°  TILT {tilt:+00;-00}°\nWATER 2.1°C";
            // Signal flickers a little to feel alive.
            int bars = Time.time % 3.7f < 0.15f ? 3 : 4;
            signalLabel.text = "SIG " + new string('|', bars) + new string('.', 4 - bars) + "   " + (1.0f + 0.02f * Mathf.Sin(Time.time * 3f)).ToString("0.00") + " V";
        }

        void OnDestroy()
        {
            if (feed != null) feed.Release();
            if (material != null) Destroy(material);
        }
    }
}
