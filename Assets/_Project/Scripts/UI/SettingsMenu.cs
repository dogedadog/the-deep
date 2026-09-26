using System;
using TheDeep.Core;
using TheDeep.UI.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI
{
    /// <summary>
    /// Settings screen (controls, display, audio) in the same chunky style as the terminal.
    /// Opened from the title menu and the pause menu; every change applies and saves immediately.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        const float RowHeight = 40f;
        const float Width = 640f;
        const float Height = 820f;
        const float PageTop = 90f;

        readonly System.Collections.Generic.List<Action> refreshers = new();
        readonly System.Collections.Generic.Dictionary<GameAction, (Button button, Text text)> bindButtons = new();
        GameObject panel, generalPage, controlsPage;
        Button generalTab, controlsTab;
        Action onClose;
        GameAction? capturing;

        public bool IsOpen => panel != null && panel.activeSelf;
        /// <summary>True while waiting for a key to bind (Esc then cancels the rebind, not the menu).</summary>
        public bool IsCapturing => capturing.HasValue || cancelledFrame == Time.frameCount;
        int cancelledFrame = -1;

        /// <summary>Builds the (hidden) panel under <paramref name="parent"/>.</summary>
        public void Build(RectTransform parent)
        {
            var dim = RetroUI.Panel("Settings", parent, new Color(0, 0, 0, 0.6f), raycast: true);
            RetroUI.Stretch(dim.rectTransform);
            panel = dim.gameObject;

            var box = RetroUI.Panel("Box", dim.transform, RetroUI.Face, raycast: true);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Width, Height);
            RetroUI.Bevel(rt, raised: true, width: 3);
            var header = RetroUI.Panel("Header", box.transform, RetroUI.TitleBar);
            RetroUI.Place(header.rectTransform, 4, 4, Width - 8, 34);
            var title = RetroUI.Label("Text", header.transform, "SETTINGS", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Stretch(title.rectTransform, 12, 0, 0, 0);

            generalTab = RetroUI.Button("GeneralTab", box.transform, "GENERAL", () => ShowPage(false), 16);
            RetroUI.Place(generalTab.GetComponent<RectTransform>(), 20, 46, 160, 34);
            controlsTab = RetroUI.Button("ControlsTab", box.transform, "CONTROLS / KEYS", () => ShowPage(true), 16);
            RetroUI.Place(controlsTab.GetComponent<RectTransform>(), 186, 46, 200, 34);

            generalPage = RetroUI.Stretch(RetroUI.Rect("GeneralPage", box.transform)).gameObject;
            controlsPage = RetroUI.Stretch(RetroUI.Rect("ControlsPage", box.transform)).gameObject;
            BuildGeneral(generalPage.transform);
            BuildControls(controlsPage.transform);

            var back = RetroUI.Button("Back", box.transform, "BACK", Close, 18);
            RetroUI.Place(back.GetComponent<RectTransform>(), Width - 180, Height - 62, 160, 44);

            ShowPage(false);
            panel.SetActive(false);
        }

        void ShowPage(bool controls)
        {
            capturing = null;
            generalPage.SetActive(!controls);
            controlsPage.SetActive(controls);
            generalTab.targetGraphic.color = controls ? RetroUI.Face : new Color(0.85f, 0.9f, 1f);
            controlsTab.targetGraphic.color = controls ? new Color(0.85f, 0.9f, 1f) : RetroUI.Face;
            RefreshAll();
        }

        void BuildControls(Transform b)
        {
            float y = Section(b, "KEYBINDS  (click a key, then press the new key or mouse button)", PageTop);
            foreach (var action in Controls.All)
            {
                var label = RetroUI.Label("Label", b, Controls.Name(action), 16, RetroUI.Ink);
                RetroUI.Place(label.rectTransform, 30, y, 300, 32);
                var button = RetroUI.Button("Bind", b, "", () =>
                {
                    capturing = action;
                    RefreshAll();
                }, 15);
                RetroUI.Place(button.GetComponent<RectTransform>(), 340, y, 260, 32);
                bindButtons[action] = (button, button.GetComponentInChildren<Text>());
                y += 35;
            }
            var hint = RetroUI.Label("Hint", b, "Esc is always pause / back.  Red = the same key is used twice.", 13, RetroUI.Shadow);
            RetroUI.Place(hint.rectTransform, 30, y + 4, Width - 60, 20);
            var reset = RetroUI.Button("ResetKeys", b, "RESET KEYS", () =>
            {
                Controls.ResetAll();
                RefreshAll();
            }, 16);
            RetroUI.Place(reset.GetComponent<RectTransform>(), 20, Height - 62, 200, 44);

            refreshers.Add(() =>
            {
                foreach (var (action, (button, text)) in bindButtons)
                {
                    bool waiting = capturing == action;
                    bool clash = false;
                    foreach (var other in Controls.All)
                        if (other != action && Controls.Get(other).Equals(Controls.Get(action))) clash = true;
                    text.text = waiting ? "PRESS A KEY...  (ESC CANCELS)" : Controls.Label(action);
                    text.color = clash && !waiting ? new Color(0.8f, 0.1f, 0.05f) : RetroUI.Ink;
                    button.targetGraphic.color = waiting ? new Color(1f, 0.9f, 0.55f) : RetroUI.Face;
                }
            });
        }

        void Update()
        {
            if (!capturing.HasValue) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                cancelledFrame = Time.frameCount;
                capturing = null;
                RefreshAll();
                return;
            }
            if (Controls.TryCapture(out var binding))
            {
                Controls.Rebind(capturing.Value, binding);
                capturing = null;
                RefreshAll();
            }
        }

        void BuildGeneral(Transform b)
        {
            float y = PageTop;
            y = Section(b, "MOUSE", y);
            y = SliderRow(b, "Mouse sensitivity", y, 0.1f, 3f, () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = v, v => $"{v:0.00}x");
            y = ToggleRow(b, "Invert Y axis", y, () => GameSettings.InvertY, v => GameSettings.InvertY = v);

            y = Section(b, "DISPLAY", y + 6);
            y = CycleRow(b, "Window mode", y, () => GameSettings.WindowModeNames[GameSettings.WindowModeIndex], d => GameSettings.WindowModeIndex += d);
            y = CycleRow(b, "Resolution", y, () => GameSettings.ResolutionName, d => GameSettings.ResolutionIndex += d);
            y = ToggleRow(b, "VSync", y, () => GameSettings.VSync, v => GameSettings.VSync = v);
            y = CycleRow(b, "FPS limit (VSync off)", y, () => GameSettings.FpsName, d => GameSettings.FpsIndex += d);
            y = SliderRow(b, "Field of view", y, 55f, 100f, () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = Mathf.Round(v), v => $"{v:0}°");
            y = CycleRow(b, "Pixel size", y, () => GameSettings.PixelOptionNames[GameSettings.PixelIndex], d => GameSettings.PixelIndex += d);
            y = SliderRow(b, "Brightness", y, -1f, 1f, () => GameSettings.Brightness, v => GameSettings.Brightness = v, v => $"{v:+0.0;-0.0;0.0}");

            y = Section(b, "AUDIO", y + 6);
            y = SliderRow(b, "Master volume", y, 0f, 1f, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, v => $"{v * 100f:0}%");

#if UNITY_EDITOR
            var note = RetroUI.Label("Note", b, "Window mode and resolution only apply in the built game.", 13, RetroUI.Shadow);
            RetroUI.Place(note.rectTransform, 20, y + 2, Width - 40, 20);
#endif
            var reset = RetroUI.Button("Reset", b, "RESET DEFAULTS", () =>
            {
                GameSettings.ResetToDefaults();
                RefreshAll();
            }, 16);
            RetroUI.Place(reset.GetComponent<RectTransform>(), 20, Height - 62, 200, 44);
        }

        public void Open(Action closed)
        {
            onClose = closed;
            ShowPage(false);
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
        }

        public void Close()
        {
            capturing = null;
            panel.SetActive(false);
            onClose?.Invoke();
            onClose = null;
        }

        void RefreshAll()
        {
            foreach (var refresh in refreshers) refresh();
        }

        // ------------------------------------------------------------------ rows

        static float Section(Transform parent, string text, float y)
        {
            var label = RetroUI.Label("Section", parent, text, 16, RetroUI.TitleBar, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Place(label.rectTransform, 20, y, Width - 40, 26);
            var line = RetroUI.Panel("Line", parent, RetroUI.Shadow);
            RetroUI.Place(line.rectTransform, 20, y + 26, Width - 40, 2);
            return y + 32;
        }

        static void RowLabel(Transform parent, string text, float y)
        {
            var label = RetroUI.Label("Label", parent, text, 17, RetroUI.Ink);
            RetroUI.Place(label.rectTransform, 30, y, 250, RowHeight - 6);
        }

        float SliderRow(Transform parent, string label, float y, float min, float max, Func<float> get, Action<float> set, Func<float, string> format)
        {
            RowLabel(parent, label, y);
            var value = RetroUI.Label("Value", parent, "", 16, RetroUI.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            RetroUI.Place(value.rectTransform, Width - 110, y, 80, RowHeight - 6);

            var root = RetroUI.Rect("Slider", parent);
            RetroUI.Place(root, 290, y, 220, RowHeight - 6);
            var track = RetroUI.Panel("Track", root, RetroUI.Screen, raycast: true);
            var trackRt = track.rectTransform;
            trackRt.anchorMin = new Vector2(0, 0.5f);
            trackRt.anchorMax = new Vector2(1, 0.5f);
            trackRt.sizeDelta = new Vector2(0, 10);
            RetroUI.Bevel(trackRt, raised: false);
            var fillArea = RetroUI.Rect("FillArea", root);
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(-4, 6);
            var fill = RetroUI.Panel("Fill", fillArea, RetroUI.PhosphorDim);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = RetroUI.Stretch(RetroUI.Rect("HandleArea", root), 8, 0, 8, 0);
            var handle = RetroUI.Panel("Handle", handleArea, RetroUI.Face, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(16, 0);
            RetroUI.Bevel(handle.rectTransform, raised: true);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(get());
            value.text = format(get());
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                value.text = format(get());
            });
            refreshers.Add(() =>
            {
                slider.SetValueWithoutNotify(get());
                value.text = format(get());
            });
            return y + RowHeight;
        }

        float ToggleRow(Transform parent, string label, float y, Func<bool> get, Action<bool> set)
        {
            RowLabel(parent, label, y);
            var button = RetroUI.Button("Toggle", parent, "", null, 16);
            RetroUI.Place(button.GetComponent<RectTransform>(), 290, y, 120, RowHeight - 6);
            var text = button.GetComponentInChildren<Text>();
            void Show()
            {
                text.text = get() ? "ON" : "OFF";
                button.targetGraphic.color = get() ? new Color(0.75f, 0.9f, 0.75f) : RetroUI.Face;
            }
            button.onClick.AddListener(() =>
            {
                set(!get());
                Show();
            });
            Show();
            refreshers.Add(Show);
            return y + RowHeight;
        }

        float CycleRow(Transform parent, string label, float y, Func<string> get, Action<int> step)
        {
            RowLabel(parent, label, y);
            var value = RetroUI.Readout("Value", parent, "", 16);
            value.alignment = TextAnchor.MiddleCenter;
            RetroUI.Place((RectTransform)value.transform.parent, 330, y, 180, RowHeight - 6);
            var prev = RetroUI.Button("Prev", parent, "<", () => { step(-1); value.text = get(); }, 18);
            RetroUI.Place(prev.GetComponent<RectTransform>(), 290, y, 36, RowHeight - 6);
            var next = RetroUI.Button("Next", parent, ">", () => { step(1); value.text = get(); }, 18);
            RetroUI.Place(next.GetComponent<RectTransform>(), 514, y, 36, RowHeight - 6);
            value.text = get();
            refreshers.Add(() => value.text = get());
            return y + RowHeight;
        }
    }
}
