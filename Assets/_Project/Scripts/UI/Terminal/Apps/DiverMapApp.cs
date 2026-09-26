using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>Placeholder sonar-style map. Diver blips, rope length and signal come in step 2.</summary>
    public class DiverMapApp : TerminalApp
    {
        public override string Title => "Diver Map";
        public override string IconGlyph => "MAP";
        public override Color IconColor => new(0.15f, 0.45f, 0.25f);
        public override Vector2 WindowSize => new(640, 460);

        public override void BuildContent(RectTransform content)
        {
            const float mapSize = 400f;
            var map = RetroUI.Panel("Map", content, RetroUI.Screen);
            RetroUI.Place(map.rectTransform, 0, 0, mapSize, mapSize);
            RetroUI.Bevel(map.rectTransform, raised: false);

            // Grid lines.
            for (int i = 1; i < 8; i++)
            {
                float p = i / 8f;
                var h = RetroUI.Panel("GridH", map.transform, RetroUI.PhosphorDim * new Color(1, 1, 1, 0.5f)).rectTransform;
                h.anchorMin = new Vector2(0, p);
                h.anchorMax = new Vector2(1, p);
                h.sizeDelta = new Vector2(0, 1);
                var v = RetroUI.Panel("GridV", map.transform, RetroUI.PhosphorDim * new Color(1, 1, 1, 0.5f)).rectTransform;
                v.anchorMin = new Vector2(p, 0);
                v.anchorMax = new Vector2(p, 1);
                v.sizeDelta = new Vector2(1, 0);
            }

            // Rotating sonar sweep line from the centre.
            var sweep = RetroUI.Panel("Sweep", map.transform, RetroUI.Phosphor * new Color(1, 1, 1, 0.6f)).rectTransform;
            sweep.anchorMin = sweep.anchorMax = new Vector2(0.5f, 0.5f);
            sweep.pivot = new Vector2(0f, 0.5f);
            sweep.sizeDelta = new Vector2(mapSize * 0.5f - 6, 2);
            sweep.gameObject.AddComponent<Spin>().DegreesPerSecond = -60f;

            var sub = RetroUI.Panel("SubMarker", map.transform, new Color(1f, 0.85f, 0.2f)).rectTransform;
            sub.anchorMin = sub.anchorMax = new Vector2(0.5f, 0.5f);
            sub.sizeDelta = new Vector2(12, 12);
            var subLabel = RetroUI.Label("SubLabel", map.transform, "SUB", 13, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
            subLabel.rectTransform.anchorMin = subLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            subLabel.rectTransform.anchoredPosition = new Vector2(0, -18);
            subLabel.rectTransform.sizeDelta = new Vector2(60, 20);

            var info = RetroUI.Readout("Info", content,
                "DIVERS DEPLOYED: 0\n\n" +
                "SUB DEPTH:  0 m\n" +
                "HEADING:    000\n\n" +
                "-- DIVER STATUS --\n" +
                "(none)\n\n" +
                "ROPE / SIGNAL\n" +
                "TELEMETRY OFFLINE", 14);
            var infoRt = (RectTransform)info.transform.parent;
            RetroUI.Place(infoRt, mapSize + 10, 0, WindowSize.x - 16 - mapSize - 10, mapSize);
        }
    }

    /// <summary>Rotates a UI element continuously.</summary>
    public class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 90f;
        void Update() => transform.Rotate(0f, 0f, DegreesPerSecond * Time.deltaTime);
    }
}
