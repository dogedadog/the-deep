using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal
{
    /// <summary>Helpers for building the chunky, bevelled "old OS" look in code.</summary>
    public static class RetroUI
    {
        public static readonly Color Face = new(0.72f, 0.72f, 0.70f);
        public static readonly Color Highlight = new(0.95f, 0.95f, 0.93f);
        public static readonly Color Shadow = new(0.45f, 0.45f, 0.44f);
        public static readonly Color DarkShadow = new(0.08f, 0.08f, 0.08f);
        public static readonly Color TitleBar = new(0.06f, 0.16f, 0.36f);
        public static readonly Color TitleBarInactive = new(0.45f, 0.47f, 0.50f);
        public static readonly Color Desktop = new(0.03f, 0.22f, 0.24f);
        public static readonly Color Screen = new(0.02f, 0.04f, 0.03f);
        public static readonly Color Phosphor = new(0.35f, 1f, 0.45f);
        public static readonly Color PhosphorDim = new(0.15f, 0.45f, 0.2f);
        public static readonly Color Ink = new(0.05f, 0.05f, 0.05f);

        static Font font;
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Any RetroUI button was clicked (raised before the button's own action), e.g. for click sounds.</summary>
        public static event Action<Button> ButtonClicked;
        /// <summary>A greyed-out RetroUI button was clicked.</summary>
        public static event Action DisabledClicked;

        internal static void RaiseDisabledClicked() => DisabledClicked?.Invoke();

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Stretch to fill the parent with the given insets (left, top, right, bottom).</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchor to the parent's top-left corner at a pixel position/size (y grows downward).</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size = 18, Color? color = null,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? Ink;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Classic two-tone 3D edge. Raised = light top/left, dark bottom/right; sunken is the reverse.</summary>
        public static void Bevel(RectTransform rt, bool raised, float width = 2f)
        {
            Color topLeft = raised ? Highlight : Shadow;
            Color bottomRight = raised ? DarkShadow : Highlight;
            Edge(rt, "BevelTop", topLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -width), Vector2.zero);
            Edge(rt, "BevelLeft", topLeft, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(width, 0));
            Edge(rt, "BevelBottom", bottomRight, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, width));
            Edge(rt, "BevelRight", bottomRight, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-width, 0), Vector2.zero);
        }

        static void Edge(RectTransform parent, string name, Color c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = Panel(name, parent, c).rectTransform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
        }

        public static Button Button(string name, Transform parent, string text, UnityAction onClick, int fontSize = 18)
        {
            var img = Panel(name, parent, Face, raycast: true);
            Bevel(img.rectTransform, raised: true);
            var label = Label("Label", img.transform, text, fontSize, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 4, 2, 4, 2);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => ButtonClicked?.Invoke(button));
            if (onClick != null) button.onClick.AddListener(onClick);
            img.gameObject.AddComponent<RetroButtonState>().Init(button, label);
            return button;
        }

        /// <summary>A disabled button (its label greys out), used for features that aren't built yet.</summary>
        public static Button DisabledButton(string name, Transform parent, string text, int fontSize = 16)
        {
            var b = Button(name, parent, text, null, fontSize);
            b.interactable = false;
            return b;
        }

        /// <summary>Black "CRT" readout box with green text, sunken into its parent.</summary>
        public static Text Readout(string name, Transform parent, string text, int size = 16)
        {
            var box = Panel(name, parent, Screen);
            Bevel(box.rectTransform, raised: false);
            var t = Label("Text", box.transform, text, size, Phosphor, TextAnchor.UpperLeft);
            Stretch(t.rectTransform, 10, 8, 10, 8);
            return t;
        }

        public static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Added to every <see cref="RetroUI.Button"/> (runtime only, like TerminalOS's desktop helpers).
    /// Greys the label while the button can't be clicked and restores its colour afterwards, so
    /// custom label colours (the red keybind clashes) are left alone while it's clickable. Clicks on
    /// a greyed button don't reach Button.onClick, so they are reported here.
    /// </summary>
    public class RetroButtonState : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        Button button;
        Text label;
        Color enabledColor;
        bool wasInteractable = true;
        bool pressedDisabled;

        public void Init(Button target, Text text)
        {
            button = target;
            label = text;
        }

        void LateUpdate()
        {
            if (button == null || label == null) return;
            bool interactable = button.IsInteractable();
            if (interactable == wasInteractable) return;
            wasInteractable = interactable;
            if (!interactable)
            {
                enabledColor = label.color;
                label.color = RetroUI.Shadow;
            }
            else
            {
                label.color = enabledColor;
            }
        }

        // Judged by the state at the press: Button.onClick runs before this handler, and an action
        // that greys its own button (BUY's double-buy lock) isn't a click on a disabled button.
        public void OnPointerDown(PointerEventData e) => pressedDisabled = button != null && !button.IsInteractable();

        public void OnPointerClick(PointerEventData e)
        {
            if (button != null && e.button == PointerEventData.InputButton.Left && pressedDisabled && !button.IsInteractable())
                RetroUI.RaiseDisabledClicked();
        }
    }
}
