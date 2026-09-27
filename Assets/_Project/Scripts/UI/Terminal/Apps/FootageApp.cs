using System;
using System.Collections.Generic;
using TheDeep.Footage;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// Watch camera chips that were put in the chip reader: the list on the left (newest first,
    /// a page at a time), a CCTV-style player on the right. Footage from a diver who died ends in
    /// static and SIGNAL LOST. It only plays to someone seated at the terminal.
    /// </summary>
    public class FootageApp : TerminalApp
    {
        const int ListRows = 7;
        const float ViewW = 460f, ViewH = 288f;
        const int None = int.MinValue; // nothing selected (Team 7's chip ids are negative, so not -1)
        const float IdleHiss = 0.1f, FlatlineSeconds = 2f, NewChipSeconds = 3f;

        [SerializeField] Material feedMaterial;

        readonly Button[] listButtons = new Button[ListRows];
        readonly Text[] listTexts = new Text[ListRows];
        readonly List<ChipInfo> listed = new();
        readonly HashSet<int> wasListed = new();
        RenderTexture feed;
        Material material;
        FootagePlayback playback;
        TerminalOS os;
        StaticHiss hiss;
        Text title, timecode, stamp, status, lost, pageLabel, playLabel;
        Button playButton, prevPage, nextPage;
        RectTransform bar;
        Image progress;
        int selected = None, page;
        bool open, primed, resumeOnSit, wasSeated, wasLost;
        float newChipUntil, lostAt;

        public override string Title => "Footage";
        public override string IconGlyph => "TAPE";
        public override Color IconColor => new(0.25f, 0.25f, 0.3f);
        public override Vector2 WindowSize => new(720, 470);

        public override void BuildContent(RectTransform content)
        {
            playback = FindFirstObjectByType<FootagePlayback>();
            os = GetComponent<TerminalOS>();
            feed = new RenderTexture(320, 200, 24) { filterMode = FilterMode.Point, name = "FootageFeed" };

            // The monitor's speaker (its own object: the other apps share this one).
            var speaker = new GameObject("FootageSpeaker");
            speaker.transform.SetParent(transform, false);
            hiss = speaker.AddComponent<StaticHiss>();

            var listHeader = RetroUI.Label("ChipsHeader", content, "CHIPS IN READER", 14, style: FontStyle.Bold);
            RetroUI.Place(listHeader.rectTransform, 0, 0, 220, 20);
            for (int i = 0; i < ListRows; i++)
            {
                int row = i;
                listButtons[i] = RetroUI.Button("Chip" + i, content, "", () => Select(row), 12);
                RetroUI.Place(listButtons[i].GetComponent<RectTransform>(), 0, 24 + i * 44, 220, 40);
                listTexts[i] = listButtons[i].GetComponentInChildren<Text>();
                listTexts[i].alignment = TextAnchor.MiddleLeft;
            }
            prevPage = RetroUI.Button("PrevPage", content, "PREV", () => TurnPage(-1), 14);
            RetroUI.Place(prevPage.GetComponent<RectTransform>(), 0, 332, 60, 40);
            pageLabel = RetroUI.Label("Page", content, "", 13, anchor: TextAnchor.MiddleCenter, style: FontStyle.Bold);
            RetroUI.Place(pageLabel.rectTransform, 60, 332, 100, 40);
            nextPage = RetroUI.Button("NextPage", content, "NEXT", () => TurnPage(1), 14);
            RetroUI.Place(nextPage.GetComponent<RectTransform>(), 160, 332, 60, 40);

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

            // Progress bar; click it to jump there.
            var barImage = RetroUI.Panel("Bar", content, RetroUI.Screen, raycast: true);
            bar = barImage.rectTransform;
            RetroUI.Place(bar, 232, ViewH + 6, ViewW, 12);
            var seek = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            seek.callback.AddListener(SeekTo);
            barImage.gameObject.AddComponent<EventTrigger>().triggers.Add(seek);
            progress = RetroUI.Panel("Progress", bar, new Color(1f, 0.3f, 0.2f));
            RetroUI.Place(progress.rectTransform, 0, 0, 0, 12);

            playButton = RetroUI.Button("Play", content, "PLAY", TogglePlay, 15);
            RetroUI.Place(playButton.GetComponent<RectTransform>(), 232, ViewH + 24, 110, 36);
            playLabel = playButton.GetComponentInChildren<Text>();
            var back = RetroUI.Button("Back10", content, "<< 10s", () => Skip(-10f), 15);
            RetroUI.Place(back.GetComponent<RectTransform>(), 348, ViewH + 24, 100, 36);
            var fwd = RetroUI.Button("Fwd10", content, "10s >>", () => Skip(10f), 15);
            RetroUI.Place(fwd.GetComponent<RectTransform>(), 454, ViewH + 24, 100, 36);
            var restart = RetroUI.Button("Restart", content, "RESTART", () => Skip(-9999f), 15);
            RetroUI.Place(restart.GetComponent<RectTransform>(), 560, ViewH + 24, 132, 36);
            status = RetroUI.Label("Status", content, "", 13);
            RetroUI.Place(status.rectTransform, 232, ViewH + 66, ViewW, 40);
            selected = None;
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
            // Chips inserted while the window was shut aren't news when it reopens.
            primed = resumeOnSit = false;
            playback?.Stop();
            SetHiss(0f);
            if (hiss != null) hiss.Flatline(0f);
        }

        /// <summary>A row on the current page was clicked.</summary>
        void Select(int row)
        {
            int index = page * ListRows + row;
            if (index < listed.Count) SelectChip(listed[index].Id);
        }

        void SelectChip(int id)
        {
            selected = id;
            resumeOnSit = false;
            var clip = FootageArchive.Instance?.GetClip(id);
            if (clip != null) playback?.Load(clip, feed);
            else playback?.Stop();
        }

        int PageCount => Mathf.Max(1, (listed.Count + ListRows - 1) / ListRows);

        void TurnPage(int delta) => page = Mathf.Clamp(page + delta, 0, PageCount - 1);

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

        void SeekTo(BaseEventData data)
        {
            if (playback?.Clip == null || !(data is PointerEventData e)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(bar, e.position, e.pressEventCamera, out Vector2 local)) return;
            float x = local.x + bar.rect.width * bar.pivot.x; // pivot-relative -> 0..ViewW
            playback.Seek(Mathf.Clamp01(x / ViewW) * playback.Clip.Duration);
        }

        void Update()
        {
            if (!open || playback == null) return;
            var archive = FootageArchive.Instance;
            bool online = archive != null && archive.IsSpawned;
            if (!online && (selected != None || primed))
            {
                // Left the session: its chips can't be watched any more.
                selected = None;
                primed = resumeOnSit = false;
                playback.Stop();
            }
            RefreshList(archive, online);
            if (online && selected != None && !IsListed(selected))
            {
                // Picked in an earlier session, or a Team 7 chip that went back to its body: not in the reader any more.
                selected = None;
                resumeOnSit = false;
                playback.Stop();
            }
            PickNewest();
            if (online) primed = true;

            // Footage still downloading: keep asking until it arrives.
            if (selected != None && playback.Clip == null && online)
            {
                var clip = archive.GetClip(selected);
                if (clip != null) playback.Load(clip, feed);
            }

            bool seated = os != null && os.IsInteractive;
            HoldWhileAway(seated);
            string news = Time.time < newChipUntil ? "NEW CHIP INSERTED" : null;

            var c = playback.Clip;
            if (c == null)
            {
                title.text = selected == None ? "INSERT A CHIP, THEN PICK IT ON THE LEFT" : "RETRIEVING FOOTAGE...";
                stamp.text = timecode.text = "";
                lost.gameObject.SetActive(false);
                if (material != null) material.SetFloat("_Static", 1f);
                progress.rectTransform.sizeDelta = new Vector2(0, 12);
                status.text = news ?? "";
                wasLost = false;
                SetHiss(seated ? IdleHiss : 0f);
                return;
            }

            float t = playback.Time, d = Mathf.Max(c.Duration, 0.01f);
            title.text = $"{c.Title}";
            stamp.text = c.Timestamp;
            timecode.text = $"{TimeSpan.FromSeconds(t):mm\\:ss} / {TimeSpan.FromSeconds(d):mm\\:ss}";
            progress.rectTransform.sizeDelta = new Vector2(ViewW * t / d, 12);
            playLabel.text = playback.Playing ? "PAUSE" : "PLAY";

            // Static: bursts on damaged chips, and a final collapse if the diver died.
            float noise = c.Corrupted && UnityEngine.Random.value < 0.04f ? UnityEngine.Random.Range(0.3f, 0.8f) : 0f;
            bool ending = c.EndsInDeath && t > d - 1.5f;
            if (ending) noise = Mathf.Max(noise, Mathf.InverseLerp(d - 1.5f, d, t));
            // A burst of static where the diver stopped and started recording again.
            const float CutStaticSeconds = 0.4f;
            float sinceCut = c.SinceCut(t);
            if (sinceCut < CutStaticSeconds) noise = Mathf.Max(noise, 1f - sinceCut / CutStaticSeconds * 0.7f);
            if (material != null) material.SetFloat("_Static", noise);
            bool signalLost = c.EndsInDeath && t >= d - 0.05f;
            lost.gameObject.SetActive(signalLost);
            if (signalLost && !wasLost)
            {
                lostAt = Time.time;
                if (seated && hiss != null) hiss.Flatline(FlatlineSeconds);
            }
            wasLost = signalLost;
            // The speaker follows the picture's static; after the flatline a dead feed settles to a low hiss.
            float hissLevel = signalLost && Time.time > lostAt + FlatlineSeconds ? IdleHiss : noise;
            SetHiss(seated ? hissLevel : 0f);
            status.text = news ?? (c.EndsInDeath ? "This camera stopped recording when its diver died." : "Recorded by a living diver.");
        }

        /// <summary>
        /// Start the newest chip when nothing is picked, and jump to a chip the moment it's inserted
        /// unless something is playing (or waiting for its viewer to sit back down).
        /// </summary>
        void PickNewest()
        {
            if (listed.Count == 0) return;
            int top = listed[0].Id;
            bool inserted = primed && !wasListed.Contains(top);
            if (inserted && (selected == None || (!playback.Playing && !resumeOnSit)))
            {
                page = 0;
                SelectChip(top);
                newChipUntil = Time.time + NewChipSeconds;
            }
            else if (selected == None)
            {
                page = 0;
                SelectChip(top);
            }
        }

        /// <summary>Pause while nobody is seated (the footage audio isn't positional), and carry on when they sit back down.</summary>
        void HoldWhileAway(bool seated)
        {
            if (!seated)
            {
                if (playback.Playing)
                {
                    playback.Playing = false;
                    resumeOnSit = true;
                    playback.Seek(playback.Time); // drops audio that was already queued
                }
                if (wasSeated && hiss != null) hiss.Flatline(0f);
            }
            else if (resumeOnSit)
            {
                resumeOnSit = false;
                if (playback.Clip != null) playback.Playing = true;
            }
            wasSeated = seated;
        }

        void SetHiss(float level)
        {
            if (hiss != null) hiss.Level = level;
        }

        bool IsListed(int id)
        {
            foreach (var chip in listed)
                if (chip.Id == id) return true;
            return false;
        }

        void RefreshList(FootageArchive archive, bool online)
        {
            wasListed.Clear();
            foreach (var chip in listed) wasListed.Add(chip.Id);
            listed.Clear();
            if (online)
            {
                // Newest first: the archive keeps chips in the order they went into the reader.
                var chips = archive.Chips;
                for (int i = chips.Count - 1; i >= 0; i--)
                    if (chips[i].Status == ChipStatus.Inserted) listed.Add(chips[i]);
            }

            int pages = PageCount;
            page = Mathf.Clamp(page, 0, pages - 1);
            pageLabel.text = $"PAGE {page + 1}/{pages}";
            prevPage.interactable = page > 0;
            nextPage.interactable = page < pages - 1;
            for (int i = 0; i < ListRows; i++)
            {
                int index = page * ListRows + i;
                bool show = index < listed.Count;
                listButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                var info = listed[index];
                listTexts[i].text = $"{info.Title}\n{info.Seconds:0}s{(info.Death ? "  -  LAST RECORDING" : "")}";
                listButtons[i].targetGraphic.color = info.Id == selected ? new Color(0.85f, 0.9f, 1f) : RetroUI.Face;
            }
        }

        void OnDestroy()
        {
            if (feed != null)
            {
                feed.Release();
                Destroy(feed);
            }
            if (material != null) Destroy(material);
        }
    }
}
