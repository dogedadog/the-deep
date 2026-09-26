using System;
using TheDeep.Progression;
using TheDeep.UI.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI
{
    /// <summary>
    /// Pick one of the 4 save slots before hosting: CONTINUE a saved expedition log, start NEW
    /// (overwriting needs a second click), or DELETE (also two clicks).
    /// </summary>
    public class SaveSlotMenu : MonoBehaviour
    {
        const float Width = 700f;
        const float SlotHeight = 104f;

        readonly Slot[] slots = new Slot[SaveSystem.SlotCount];
        GameObject panel;
        Text title;
        Action<int, bool> chosen;
        Action back;
        int confirmSlot = -1;
        string confirmAction;
        float confirmUntil;

        public bool IsOpen => panel != null && panel.activeSelf;

        class Slot
        {
            public Text Info;
            public Button Continue, New, Delete;
        }

        public void Build(RectTransform parent)
        {
            var dim = RetroUI.Panel("SaveSlots", parent, new Color(0, 0, 0, 0.6f), raycast: true);
            RetroUI.Stretch(dim.rectTransform);
            panel = dim.gameObject;

            float height = 60 + SaveSystem.SlotCount * SlotHeight + 70;
            var box = RetroUI.Panel("Box", dim.transform, RetroUI.Face, raycast: true);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Width, height);
            RetroUI.Bevel(rt, raised: true, width: 3);
            var header = RetroUI.Panel("Header", box.transform, RetroUI.TitleBar);
            RetroUI.Place(header.rectTransform, 4, 4, Width - 8, 34);
            title = RetroUI.Label("Text", header.transform, "", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Stretch(title.rectTransform, 12, 0, 0, 0);

            for (int i = 0; i < SaveSystem.SlotCount; i++) slots[i] = BuildSlot(box.transform, i, 50 + i * SlotHeight);

            var backButton = RetroUI.Button("Back", box.transform, "BACK", () =>
            {
                panel.SetActive(false);
                back?.Invoke();
            }, 18);
            RetroUI.Place(backButton.GetComponent<RectTransform>(), Width - 180, height - 60, 160, 44);
            var hint = RetroUI.Label("Hint", box.transform,
                "Upgrades and case files are saved. Credits reset every expedition.", 14, RetroUI.Shadow);
            RetroUI.Place(hint.rectTransform, 20, height - 60, Width - 220, 44);
            panel.SetActive(false);
        }

        Slot BuildSlot(Transform parent, int index, float y)
        {
            var slot = new Slot();
            var name = RetroUI.Label("Name", parent, $"SLOT {index + 1}", 20, RetroUI.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Place(name.rectTransform, 20, y, 100, SlotHeight - 12);
            slot.Info = RetroUI.Readout("Info", parent, "", 14);
            RetroUI.Place((RectTransform)slot.Info.transform.parent, 120, y, 330, SlotHeight - 12);

            slot.Continue = RetroUI.Button("Continue", parent, "CONTINUE", () => Choose(index, false), 16);
            RetroUI.Place(slot.Continue.GetComponent<RectTransform>(), 462, y, 218, 42);
            slot.New = RetroUI.Button("New", parent, "NEW", () => Confirmed(index, "new", () => Choose(index, true)), 15);
            RetroUI.Place(slot.New.GetComponent<RectTransform>(), 462, y + 48, 106, 40);
            slot.Delete = RetroUI.Button("Delete", parent, "DELETE", () => Confirmed(index, "delete", () =>
            {
                SaveSystem.Delete(index);
                Refresh();
            }), 15);
            RetroUI.Place(slot.Delete.GetComponent<RectTransform>(), 574, y + 48, 106, 40);
            return slot;
        }

        public void Open(string heading, Action<int, bool> onChosen, Action onBack)
        {
            title.text = heading;
            chosen = onChosen;
            back = onBack;
            confirmSlot = -1;
            Refresh();
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
        }

        public void Close() => panel.SetActive(false);

        void Update()
        {
            if (IsOpen && confirmSlot >= 0 && Time.time > confirmUntil)
            {
                confirmSlot = -1;
                Refresh();
            }
        }

        void Choose(int slot, bool startNew)
        {
            panel.SetActive(false);
            chosen?.Invoke(slot, startNew);
        }

        /// <summary>Runs <paramref name="action"/> only on a second click within a few seconds (only needed on used slots).</summary>
        void Confirmed(int slot, string action, Action run)
        {
            if (!SaveSystem.Exists(slot) || (confirmSlot == slot && confirmAction == action && Time.time <= confirmUntil))
            {
                confirmSlot = -1;
                run();
                return;
            }
            confirmSlot = slot;
            confirmAction = action;
            confirmUntil = Time.time + 4f;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                var data = SaveSystem.Peek(i);
                var slot = slots[i];
                bool used = data != null;
                slot.Info.text = used ? SaveSystem.Describe(data) : "EMPTY\nStart a new expedition log here.";
                slot.Continue.gameObject.SetActive(used);
                slot.Delete.gameObject.SetActive(used);
                bool confirming = confirmSlot == i;
                slot.New.GetComponentInChildren<Text>().text = !used ? "NEW GAME"
                    : confirming && confirmAction == "new" ? "OVERWRITE?" : "NEW";
                slot.Delete.GetComponentInChildren<Text>().text = confirming && confirmAction == "delete" ? "SURE?" : "DELETE";
                // An empty slot's NEW button takes the full width.
                var newRt = slot.New.GetComponent<RectTransform>();
                newRt.sizeDelta = new Vector2(used ? 106 : 218, used ? 40 : 88);
                newRt.anchoredPosition = new Vector2(462, -(50 + i * SlotHeight + (used ? 48 : 0)));
            }
        }
    }
}
