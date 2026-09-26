using System;
using System.Collections.Generic;
using TheDeep.Footage;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Watch camera chips that were put in the chip reader: the list on the left, a CCTV-style
    /// player on the right. Footage from a diver who died ends in static and SIGNAL LOST.
    /// </summary>
    public class FootageApp : TerminalApp
    {
        const int ListRows = 8;
        const float ViewW = 460f, ViewH = 288f;

        [SerializeField] Material feedMaterial;

        readonly Button[] listButtons = new Button[ListRows];
        readonly List<int> listed = new();
        RenderTexture feed;
        Material material;
        FootagePlayback playback;
        Text title, timecode, stamp, status, lost;
        Button playButton;
        Image progress;
        int selected;
        bool open;

        public override string Title => "Footage";
        public override string IconGlyph => "TAPE";
        public override Color IconColor => new(0.25f, 0.25f, 0.3f);
        public override Vector2 WindowSize => new(720, 470);

        public override void BuildContent(RectTransform content)
        {
            playback = FindFirstObjectByType<FootagePlayback>();
            feed = new RenderTexture(320, 200, 24) { filterMode = FilterMode.Point, name = "FootageFeed" };

            var listHeader = RetroUI.Label("ChipsHeader", content, "CHIPS IN READER", 14, style: FontStyle.Bold);
            RetroUI.Place(listHeader.rectTransform, 0, 0, 220, 20);
            for (int i = 0; i < ListRows; i++)
            {
                int row = i;
                listButtons[i] = RetroUI.Button("Chip" + i, content, "", () => Select(row), 12);
                RetroUI.Place(listButtons[i].GetComponent<RectTransform>(), 0, 24 + i * 44, 220, 40);
                listButtons[i].GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            var frame = RetroUI.Panel("Screen", content, Color.black);
            RetroUI.Place(frame.rectTransform, 232, 0, ViewW, ViewH);
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
            title = Overlay(screen, "", 15, TextAnchor.UpperLeft);
            stamp = Overlay(screen, "", 14, TextAnchor.LowerLeft);
            timecode = Overlay(screen, "", 16, TextAnchor.LowerRight);
            lost = Overlay(screen, "SIGNAL LOST", 36, TextAnchor.MiddleCenter);
            lost.gameObject.SetActive(false);

            var bar = RetroUI.Panel("Bar", content, RetroUI.Screen);
            RetroUI.Place(bar.rectTransform, 232, ViewH + 6, ViewW, 12);
            progress = RetroUI.Panel("Progress", bar.transform, new Color(1f, 0.3f, 0.2f));
            RetroUI.Place(progress.rectTransform, 0, 0, 0, 12);

            playButton = RetroUI.Button("Play", content, "PLAY", TogglePlay, 15);
            RetroUI.Place(playButton.GetComponent<RectTransform>(), 232, ViewH + 24, 110, 36);
            var back = RetroUI.Button("Back10", content, "<< 10s", () => Skip(-10f), 15);
            RetroUI.Place(back.GetComponent<RectTransform>(), 348, ViewH + 24, 100, 36);
            var fwd = RetroUI.Button("Fwd10", content, "10s >>", () => Skip(10f), 15);
            RetroUI.Place(fwd.GetComponent<RectTransform>(), 454, ViewH + 24, 100, 36);
            var restart = RetroUI.Button("Restart", content, "RESTART", () => Skip(-9999f), 15);
            RetroUI.Place(restart.GetComponent<RectTransform>(), 560, ViewH + 24, 132, 36);
            status = RetroUI.Label("Status", content, "", 13);
            RetroUI.Place(status.rectTransform, 232, ViewH + 66, ViewW, 40);
            selected = -1;
        }

        Text Overlay(RectTransform parent, string text, int size, TextAnchor anchor)
        {
            var label = RetroUI.Label("Overlay", parent, text, size, new Color(0.95f, 0.97f, 0.92f, 0.9f), anchor, FontStyle.Bold);
            RetroUI.Stretch(label.rectTransform, 12, 10, 12, 10);
            label.gameObject.AddComponent<Shadow>();
            return label;
        }

        public override void OnOpened() => open = true;

        public override void OnClosed()
        {
            open = false;
            playback?.Stop();
        }

        void Select(int row)
        {
            if (row >= listed.Count) return;
            selected = listed[row];
            var clip = FootageArchive.Instance?.GetClip(selected);
            if (clip != null) playback?.Load(clip, feed);
            else playback?.Stop();
        }

        void TogglePlay()
        {
            if (playback == null || playback.Clip == null) return;
            if (!playback.Playing && playback.Time >= playback.Clip.Duration) playback.Seek(0f);
            playback.Playing = !playback.Playing;
        }

        void Skip(float seconds)
        {
            if (playback?.Clip != null) playback.Seek(playback.Time + seconds);
        }

        void Update()
        {
            if (!open || playback == null) return;
            var archive = FootageArchive.Instance;
            RefreshList(archive);

            // Footage still downloading: keep asking until it arrives.
            if (selected != -1 && playback.Clip == null && archive != null)
            {
                var clip = archive.GetClip(selected);
                if (clip != null) playback.Load(clip, feed);
            }

            var c = playback.Clip;
            if (c == null)
            {
                title.text = selected == -1 ? "INSERT A CHIP, THEN PICK IT ON THE LEFT" : "RETRIEVING FOOTAGE...";
                stamp.text = timecode.text = "";
                lost.gameObject.SetActive(false);
                if (material != null) material.SetFloat("_Static", 1f);
                progress.rectTransform.sizeDelta = new Vector2(0, 12);
                status.text = "";
                return;
            }

            float t = playback.Time, d = Mathf.Max(c.Duration, 0.01f);
            title.text = $"{c.Title}";
            stamp.text = c.Timestamp;
            timecode.text = $"{TimeSpan.FromSeconds(t):mm\\:ss} / {TimeSpan.FromSeconds(d):mm\\:ss}";
            progress.rectTransform.sizeDelta = new Vector2(ViewW * t / d, 12);
            playButton.GetComponentInChildren<Text>().text = playback.Playing ? "PAUSE" : "PLAY";

            // Static: bursts on damaged chips, and a final collapse if the diver died.
            float noise = c.Corrupted && UnityEngine.Random.value < 0.04f ? UnityEngine.Random.Range(0.3f, 0.8f) : 0f;
            bool ending = c.EndsInDeath && t > d - 1.5f;
            if (ending) noise = Mathf.Max(noise, Mathf.InverseLerp(d - 1.5f, d, t));
            if (material != null) material.SetFloat("_Static", noise);
            lost.gameObject.SetActive(c.EndsInDeath && t >= d - 0.05f);
            status.text = c.EndsInDeath ? "This camera stopped recording when its diver died." : "Recorded by a living diver.";
        }

        void RefreshList(FootageArchive archive)
        {
            listed.Clear();
            if (archive != null && archive.IsSpawned)
                foreach (var chip in archive.Chips)
                    if (chip.Status == ChipStatus.Inserted) listed.Add(chip.Id);
            for (int i = 0; i < ListRows; i++)
            {
                bool show = i < listed.Count;
                listButtons[i].gameObject.SetActive(show);
                if (!show || !archive.TryGetInfo(listed[i], out var info)) continue;
                listButtons[i].GetComponentInChildren<Text>().text = $"{info.Title}\n{info.Seconds:0}s{(info.Death ? "  -  LAST RECORDING" : "")}";
                listButtons[i].targetGraphic.color = listed[i] == selected ? new Color(0.85f, 0.9f, 1f) : RetroUI.Face;
            }
        }

        void OnDestroy()
        {
            if (feed != null) feed.Release();
            if (material != null) Destroy(material);
        }
    }
}
