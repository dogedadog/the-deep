using System;
using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Data;
using TheDeep.Player;
using TheDeep.UI.Terminal.Apps;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal
{
    /// <summary>
    /// The retro desktop shown on the sub's computer screen (a world-space canvas).
    /// Builds the desktop, icons, taskbar and start menu, and opens a window per TerminalApp.
    /// Tray lights and icon badges show crew alerts even with every window closed.
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public class TerminalOS : MonoBehaviour
    {
        const float TaskbarHeight = 40f;
        const float PollInterval = 0.25f;
        const float LowAir = 0.25f;
        public const string OsName = "ABYSSAL-DOS 3.1";

        static readonly Color LedOff = new(0.2f, 0.24f, 0.21f);
        static readonly Color Amber = new(1f, 0.72f, 0.15f);
        static readonly Color Alarm = new(1f, 0.22f, 0.15f);

        readonly Dictionary<TerminalApp, TerminalWindow> windows = new();
        readonly Dictionary<TerminalWindow, Button> taskButtons = new();
        readonly Dictionary<TerminalApp, Text> captions = new();
        readonly Dictionary<TerminalApp, Image> iconArt = new();

        Canvas canvas;
        GraphicRaycaster raycaster;
        TerminalApp[] apps;
        RectTransform windowLayer;
        RectTransform taskList;
        GameObject startMenu;
        Text clock;
        TerminalWindow focused;
        int cascade;

        // Crew alerts, polled from replicated state.
        TerminalApp commsApp, mapApp;
        Image dataLed, airLed, sigLed;
        DiverController[] divers = Array.Empty<DiverController>();
        float nextPoll, nextScan;
        bool blink;
        int commsBadge, mapBadge;

        public bool IsInteractive { get; private set; }
        public event Action LogOffRequested;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            raycaster = GetComponent<GraphicRaycaster>();
            apps = GetComponents<TerminalApp>();
            foreach (var app in apps)
            {
                if (app is CommsApp) commsApp = app;
                else if (app is DiverMapApp) mapApp = app;
            }
            Build();
            SetInteractive(false);
        }

        void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            clock.text = DateTime.Now.ToString("HH:mm");
            blink = !blink; // flips at 4 Hz, so alerts blink at 2 Hz
            PollCrew();
        }

        public void SetEventCamera(Camera cam) => canvas.worldCamera = cam;

        /// <summary>Only accept mouse clicks while a player is actually seated at the terminal.</summary>
        public void SetInteractive(bool interactive)
        {
            IsInteractive = interactive;
            raycaster.enabled = interactive;
            if (!interactive) startMenu.SetActive(false);
        }

        public void Open(TerminalApp app)
        {
            startMenu.SetActive(false);
            bool created = false;
            if (!windows.TryGetValue(app, out TerminalWindow window))
            {
                created = true;
                var offset = new Vector2(-60 + cascade * 30, 40 - cascade * 30);
                cascade = (cascade + 1) % 5;
                window = TerminalWindow.Create(app, windowLayer, offset);
                KeepInside((RectTransform)window.transform, windowLayer);
                window.Focused += Focus;
                window.CloseClicked += Close;
                windows[app] = window;
            }

            if (created || !window.gameObject.activeSelf)
            {
                window.gameObject.SetActive(true);
                app.OnOpened();
            }
            if (!taskButtons.ContainsKey(window))
            {
                var button = RetroUI.Button(app.Title, taskList, TaskTitle(app), () => Focus(window), 14);
                var le = button.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 170;
                // Long titles shrink instead of spilling out of the bar when many windows are open.
                var label = button.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 14;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                taskButtons[window] = button;
            }
            Focus(window);
        }

        static void KeepInside(RectTransform window, RectTransform area)
        {
            Rect a = area.rect;
            Vector2 half = window.rect.size * 0.5f;
            Vector2 p = window.anchoredPosition;
            p.x = Mathf.Clamp(p.x, a.xMin + half.x, a.xMax - half.x);
            p.y = Mathf.Clamp(p.y, a.yMin + half.y, a.yMax - half.y);
            window.anchoredPosition = p;
        }

        void Close(TerminalWindow window)
        {
            window.gameObject.SetActive(false);
            window.App.OnClosed();
            if (taskButtons.Remove(window, out Button button)) Destroy(button.gameObject);

            // The topmost window still open takes over focus.
            for (int i = windowLayer.childCount - 1; i >= 0; i--)
            {
                var child = windowLayer.GetChild(i);
                if (child.gameObject.activeSelf && child.TryGetComponent(out TerminalWindow next))
                {
                    Focus(next);
                    return;
                }
            }
            if (focused != null) focused.SetFocused(false);
            focused = null;
            UpdateTaskButtons();
        }

        void Focus(TerminalWindow window)
        {
            startMenu.SetActive(false);
            if (focused != null && focused != window) focused.SetFocused(false);
            focused = window;
            window.SetFocused(true);
            UpdateTaskButtons();
        }

        /// <summary>The focused window's task button looks pressed in.</summary>
        void UpdateTaskButtons()
        {
            foreach (var pair in taskButtons)
                pair.Value.targetGraphic.color = pair.Key == focused ? RetroUI.Shadow : RetroUI.Face;
        }

        /// <summary>Tray lights and icon badges, read from replicated state only (works with no network).</summary>
        void PollCrew()
        {
            int pending = 0, corrupted = 0, logged = 0;
            var state = ExpeditionState.Instance;
            if (state != null && state.IsSpawned)
            {
                var packets = state.Packets;
                for (int i = 0; i < packets.Count; i++)
                {
                    var status = packets[i].Status;
                    if (status == PacketStatus.Pending) pending++;
                    else if (status == PacketStatus.Corrupted) corrupted++;
                    else if (status == PacketStatus.Logged) logged++;
                }
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                divers = FindObjectsByType<DiverController>(FindObjectsSortMode.None);
            }
            int inTrouble = 0;
            bool weakSignal = false;
            var subPos = TheDeep.Submarine.SubNavigation.SubPosition;
            foreach (var diver in divers)
            {
                // A body left at an earlier station no longer holds the AIR light (same rule as the Diver Map).
                if (diver == null || !diver.IsSpawned || !diver.IsDiving || DiverMapApp.IsLost(diver, subPos)) continue;
                var health = diver.GetComponent<DiverHealth>();
                bool dead = health != null && health.IsDead;
                if (dead || (health != null && health.Air01 < LowAir)) inTrouble++;
                if (!dead && SignalModel.Strength(diver.transform.position) < SignalModel.CorruptionThreshold) weakSignal = true;
            }

            dataLed.color = !blink ? LedOff : corrupted > 0 ? Alarm : pending > 0 ? Amber : LedOff;
            airLed.color = inTrouble > 0 ? Alarm : LedOff;
            sigLed.color = weakSignal ? Amber : LedOff;
            ShowBadge(commsApp, pending + corrupted + logged, corrupted > 0, ref commsBadge);
            ShowBadge(mapApp, inTrouble, true, ref mapBadge);
        }

        /// <summary>Adds "(n)" to an app's icon caption and task button, and blinks its icon while n > 0.</summary>
        void ShowBadge(TerminalApp app, int count, bool urgent, ref int shown)
        {
            if (app == null) return;
            if (count != shown)
            {
                shown = count;
                string title = TaskTitle(app);
                if (captions.TryGetValue(app, out Text caption)) caption.text = title;
                if (windows.TryGetValue(app, out TerminalWindow window) && taskButtons.TryGetValue(window, out Button button))
                    button.GetComponentInChildren<Text>().text = title;
            }
            if (iconArt.TryGetValue(app, out Image art))
                art.color = count > 0 && blink ? (urgent ? Alarm : Amber) : app.IconColor;
        }

        string TaskTitle(TerminalApp app)
        {
            int badge = app == commsApp ? commsBadge : app == mapApp ? mapBadge : 0;
            return badge > 0 ? $"{app.Title} ({badge})" : app.Title;
        }

        void Build()
        {
            var root = (RectTransform)transform;

            // Desktop wallpaper + faint logo.
            var desktop = RetroUI.Panel("Desktop", root, RetroUI.Desktop, raycast: true);
            RetroUI.Stretch(desktop.rectTransform);
            desktop.gameObject.AddComponent<DesktopBackground>().Clicked = () => startMenu.SetActive(false);
            var logo = RetroUI.Label("Logo", desktop.transform, "ABYSSAL SYSTEMS\nDEEP SURVEY DIVISION", 34,
                new Color(1, 1, 1, 0.07f), TextAnchor.LowerRight, FontStyle.Bold);
            RetroUI.Stretch(logo.rectTransform, 260, 0, 24, TaskbarHeight + 16); // bottom right, clear of the icon columns

            // Icons in columns of five down the left side.
            for (int i = 0; i < apps.Length; i++)
                CreateIcon(apps[i], desktop.transform, 20 + i / 5 * 120, 16 + i % 5 * 106);

            windowLayer = RetroUI.Stretch(RetroUI.Rect("Windows", root), 0, 0, 0, TaskbarHeight);
            windowLayer.gameObject.AddComponent<RectMask2D>(); // dragged windows get cut off at the screen edge

            BuildTaskbar(root);
            BuildStartMenu(root);
        }

        void CreateIcon(TerminalApp app, Transform parent, float x, float y)
        {
            var slot = RetroUI.Panel("Icon_" + app.Title, parent, new Color(0, 0, 0, 0), raycast: true);
            RetroUI.Place(slot.rectTransform, x, y, 110, 100);

            var art = RetroUI.Panel("Art", slot.transform, app.IconColor);
            var artRt = art.rectTransform;
            artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 1);
            artRt.pivot = new Vector2(0.5f, 1);
            artRt.anchoredPosition = new Vector2(0, -4);
            artRt.sizeDelta = new Vector2(52, 52);
            RetroUI.Bevel(artRt, raised: true);
            var glyph = RetroUI.Label("Glyph", art.transform, app.IconGlyph, 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            RetroUI.Stretch(glyph.rectTransform);

            var caption = RetroUI.Label("Caption", slot.transform, app.Title, 14, Color.white, TextAnchor.UpperCenter);
            var capRt = caption.rectTransform;
            capRt.anchorMin = new Vector2(0, 0);
            capRt.anchorMax = new Vector2(1, 0);
            capRt.pivot = new Vector2(0.5f, 0);
            capRt.sizeDelta = new Vector2(0, 40);
            capRt.anchoredPosition = Vector2.zero;

            iconArt[app] = art;
            captions[app] = caption;

            var icon = slot.gameObject.AddComponent<DesktopIcon>();
            icon.Highlight = slot;
            icon.DoubleClicked = () => Open(app);
        }

        void BuildTaskbar(RectTransform root)
        {
            var bar = RetroUI.Panel("Taskbar", root, RetroUI.Face, raycast: true);
            var barRt = bar.rectTransform;
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(1, 0);
            barRt.pivot = new Vector2(0.5f, 0);
            barRt.sizeDelta = new Vector2(0, TaskbarHeight);
            RetroUI.Bevel(barRt, raised: true);

            var start = RetroUI.Button("Start", barRt, "START", () => startMenu.SetActive(!startMenu.activeSelf), 16);
            RetroUI.Place(start.GetComponent<RectTransform>(), 4, 5, 90, 30);

            taskList = RetroUI.Rect("Tasks", barRt);
            RetroUI.Stretch(taskList, 104, 5, 160, 5);
            var layout = taskList.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var tray = RetroUI.Panel("Tray", barRt, RetroUI.Face);
            var trayRt = tray.rectTransform;
            trayRt.anchorMin = trayRt.anchorMax = trayRt.pivot = new Vector2(1, 0.5f);
            trayRt.anchoredPosition = new Vector2(-4, 0);
            trayRt.sizeDelta = new Vector2(150, 30);
            RetroUI.Bevel(trayRt, raised: false);
            // Status lights: new data, a diver low on air or dead, a diver out of clean signal range.
            dataLed = TrayLed(trayRt, "D", 5);
            airLed = TrayLed(trayRt, "AIR", 29);
            sigLed = TrayLed(trayRt, "SIG", 64);
            clock = RetroUI.Label("Clock", trayRt, "00:00", 16, RetroUI.Ink, TextAnchor.MiddleCenter);
            RetroUI.Stretch(clock.rectTransform, 100, 0, 0, 0);
        }

        static Image TrayLed(RectTransform tray, string label, float x)
        {
            var led = RetroUI.Panel("Led" + label, tray, LedOff);
            RetroUI.Place(led.rectTransform, x, 10, 10, 10);
            RetroUI.Bevel(led.rectTransform, raised: false, 1f);
            var text = RetroUI.Label("Led" + label + "Text", tray, label, 11, RetroUI.Ink);
            RetroUI.Place(text.rectTransform, x + 12, 0, 22, 30);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return led;
        }

        void BuildStartMenu(RectTransform root)
        {
            const float itemHeight = 34f;
            int items = apps.Length + 1;
            var menu = RetroUI.Panel("StartMenu", root, RetroUI.Face, raycast: true);
            var rt = menu.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(4, TaskbarHeight - 2);
            rt.sizeDelta = new Vector2(260, items * itemHeight + 20);
            RetroUI.Bevel(rt, raised: true);

            // Vertical banner with the OS name, like the old side-stripe start menus.
            var banner = RetroUI.Panel("Banner", rt, RetroUI.TitleBar);
            var bRt = banner.rectTransform;
            bRt.anchorMin = new Vector2(0, 0);
            bRt.anchorMax = new Vector2(0, 1);
            bRt.pivot = new Vector2(0, 0.5f);
            bRt.offsetMin = new Vector2(3, 3);
            bRt.offsetMax = new Vector2(33, -3);
            var bText = RetroUI.Label("Text", bRt, OsName, 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            bText.rectTransform.sizeDelta = new Vector2(rt.sizeDelta.y - 6, 30);
            bText.rectTransform.localRotation = Quaternion.Euler(0, 0, 90);

            float y = 8;
            foreach (TerminalApp app in apps)
            {
                var b = RetroUI.Button(app.Title, rt, app.Title, () => Open(app), 16);
                RetroUI.Place(b.GetComponent<RectTransform>(), 40, y, 212, itemHeight - 4);
                y += itemHeight;
            }
            y += 4;
            var logOff = RetroUI.Button("LogOff", rt, "Log Off  [Esc]", () =>
            {
                startMenu.SetActive(false);
                LogOffRequested?.Invoke();
            }, 16);
            RetroUI.Place(logOff.GetComponent<RectTransform>(), 40, y, 212, itemHeight - 4);

            startMenu = menu.gameObject;
            startMenu.SetActive(false);
        }
    }

    /// <summary>Desktop icon: single click selects, double click opens.</summary>
    public class DesktopIcon : MonoBehaviour, IPointerClickHandler
    {
        const float DoubleClickTime = 0.4f;
        static DesktopIcon selected;

        public Image Highlight;
        public Action DoubleClicked;
        float lastClick = -1f;

        public void OnPointerClick(PointerEventData e)
        {
            if (selected != null && selected != this) selected.Highlight.color = new Color(0, 0, 0, 0);
            selected = this;
            Highlight.color = new Color(0.2f, 0.35f, 0.8f, 0.45f);

            if (Time.unscaledTime - lastClick < DoubleClickTime)
            {
                lastClick = -1f;
                DoubleClicked?.Invoke();
            }
            else
            {
                lastClick = Time.unscaledTime;
            }
        }
    }

    /// <summary>Clicking empty desktop closes the start menu.</summary>
    public class DesktopBackground : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;
        public void OnPointerClick(PointerEventData e) => Clicked?.Invoke();
    }
}
