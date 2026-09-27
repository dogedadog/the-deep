using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal
{
    /// <summary>A draggable, closable program window on the terminal desktop.</summary>
    public class TerminalWindow : MonoBehaviour, IPointerDownHandler
    {
        const float TitleHeight = 30f;

        Image titleBar;

        public TerminalApp App { get; private set; }
        public RectTransform Content { get; private set; }
        public event Action<TerminalWindow> Focused;
        public event Action<TerminalWindow> CloseClicked;

        public static TerminalWindow Create(TerminalApp app, RectTransform layer, Vector2 position)
        {
            var frame = RetroUI.Panel(app.Title, layer, RetroUI.Face, raycast: true);
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = app.WindowSize;
            rt.anchoredPosition = position;
            RetroUI.Bevel(rt, raised: true);

            var window = frame.gameObject.AddComponent<TerminalWindow>();
            window.App = app;

            window.titleBar = RetroUI.Panel("TitleBar", rt, RetroUI.TitleBar, raycast: true);
            var title = window.titleBar.rectTransform;
            title.anchorMin = new Vector2(0, 1);
            title.anchorMax = new Vector2(1, 1);
            title.pivot = new Vector2(0.5f, 1);
            title.offsetMin = new Vector2(4, -4 - TitleHeight);
            title.offsetMax = new Vector2(-4, -4);
            window.titleBar.gameObject.AddComponent<WindowDragHandle>().Target = rt;

            var label = RetroUI.Label("Title", title, app.Title.ToUpperInvariant(), 17, Color.white, style: FontStyle.Bold);
            RetroUI.Stretch(label.rectTransform, 8, 0, 40, 0);

            var close = RetroUI.Button("Close", title, "X", () => window.CloseClicked?.Invoke(window), 16);
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = closeRt.anchorMax = closeRt.pivot = new Vector2(1, 0.5f);
            closeRt.anchoredPosition = new Vector2(-3, 0);
            closeRt.sizeDelta = new Vector2(26, 24);

            window.Content = RetroUI.Stretch(RetroUI.Rect("Content", rt), 8, TitleHeight + 10, 8, 8);
            app.BuildContent(window.Content);

            // Buttons swallow pointer-down, so each one also tells its window to come to the front.
            foreach (var s in rt.GetComponentsInChildren<Selectable>(true))
                s.gameObject.AddComponent<FocusForwarder>().Window = window;
            return window;
        }

        public void SetFocused(bool focused)
        {
            titleBar.color = focused ? RetroUI.TitleBar : RetroUI.TitleBarInactive;
            if (focused) transform.SetAsLastSibling();
        }

        /// <summary>Ask the desktop to bring this window to the front.</summary>
        public void RequestFocus() => Focused?.Invoke(this);

        public void OnPointerDown(PointerEventData eventData) => RequestFocus();
    }

    /// <summary>
    /// Sits next to a button inside a window. ExecuteEvents runs every pointer-down handler on the
    /// object, so the button still works and the window gets raised too.
    /// </summary>
    public class FocusForwarder : MonoBehaviour, IPointerDownHandler
    {
        public TerminalWindow Window;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Window != null) Window.RequestFocus();
        }
    }

    /// <summary>Lets the title bar drag its window around, kept inside the desktop area.</summary>
    public class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Target;
        Vector2 grabOffset;

        public void OnBeginDrag(PointerEventData e)
        {
            if (LocalPoint(e, out Vector2 p)) grabOffset = Target.anchoredPosition - p;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!LocalPoint(e, out Vector2 p)) return;
            var area = ((RectTransform)Target.parent).rect;
            Vector2 half = Target.rect.size * 0.5f;
            Vector2 pos = p + grabOffset;
            // Keep at least part of the window (and all of its title bar) on screen.
            pos.x = Mathf.Clamp(pos.x, area.xMin - half.x + 80, area.xMax + half.x - 80);
            pos.y = Mathf.Clamp(pos.y, area.yMin - half.y + 60, area.yMax - half.y);
            Target.anchoredPosition = pos;
        }

        bool LocalPoint(PointerEventData e, out Vector2 p) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Target.parent, e.position, e.pressEventCamera, out p);
    }
}
