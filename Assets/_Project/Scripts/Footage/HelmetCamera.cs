using System;
using System.Collections.Generic;
using System.Text;
using TheDeep.Core;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.Voice;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Footage
{
    /// <summary>
    /// The diver's helmet camera. Hold the record key while diving to film (limited film per chip).
    /// The chip stays in the camera until it's put in the sub's chip reader, or the diver dies with it.
    /// A black box also keeps the last 20 seconds of the dive, so a death always leaves footage.
    /// </summary>
    public class HelmetCamera : NetworkBehaviour
    {
        public const float FilmSeconds = 120f;
        const float SampleInterval = 0.1f;
        const float BoxSeconds = 20f;

        [SerializeField] Transform head;
        [SerializeField] Light headlamp;

        DiverController diver;
        DiverHealth health;
        FootageClip clip;
        float recorded, sampleTimer;
        bool recording, newTake;
        Text recText;

        // Where each sound source's audio ends (in samples), so its next piece follows on without overlapping.
        readonly Dictionary<int, int> writeHead = new();

        // Black box: the last BoxSeconds of the dive, kept whether or not the record key is held.
        readonly List<FootageFrame> box = new();
        readonly List<FootageAudio> boxAudio = new();
        readonly Dictionary<int, int> boxWriteHead = new();
        float boxClock, boxTimer, lastManualBoxClock = -1f;

        public float FilmLeft => FilmSeconds - recorded;
        public bool HasFootage => clip != null && clip.Frames.Count > 1;
        public bool Recording => recording;

        /// <summary>The black box runs while this diver is in the water and alive.</summary>
        bool BoxLive => diver.IsDiving && (health == null || !health.IsDead);

        void Awake()
        {
            diver = GetComponent<DiverController>();
            health = GetComponent<DiverHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            diver.HudLines.Add(HudLine);
            BuildOverlay();
            PlayerVoice.LocalVoiceFrame += OnOwnVoice;
            PlayerVoice.RemoteVoiceHeard += OnVoiceHeard;
            diver.DivingChanged += OnDivingChanged;
        }

        public override void OnNetworkDespawn()
        {
            PlayerVoice.LocalVoiceFrame -= OnOwnVoice;
            PlayerVoice.RemoteVoiceHeard -= OnVoiceHeard;
            diver.DivingChanged -= OnDivingChanged;
        }

        void OnDivingChanged(bool inWater)
        {
            if (!inWater) ClearBox();
        }

        // ------------------------------------------------------------------ sound

        void OnOwnVoice(float[] samples) => AddAudio(-1, samples, FootageAudio.OwnVoice, 1f);

        void OnVoiceHeard(PlayerVoice speaker, float[] samples, bool radio, float signal)
        {
            if (!recording && !BoxLive) return;
            // One write head per speaker and channel: the same packet can arrive nearby and over the radio.
            int source = (int)speaker.OwnerClientId * 4;
            if (radio)
            {
                float mine = SignalModel.Strength(transform.position);
                AddAudio(source + FootageAudio.Radio, samples, FootageAudio.Radio, Mathf.Min(signal, mine));
            }
            else if (speaker.IsDiving && Vector3.Distance(speaker.transform.position, transform.position) < 20f)
            {
                AddAudio(source + FootageAudio.Nearby, samples, FootageAudio.Nearby, 1f);
            }
        }

        /// <summary>
        /// Encodes a piece of sound at once (the caller may reuse <paramref name="samples"/>) and adds it
        /// to the chip while recording, and to the black box while diving.
        /// </summary>
        void AddAudio(int source, float[] samples, byte channel, float signal)
        {
            bool toClip = recording && clip != null;
            bool toBox = BoxLive;
            if (!toClip && !toBox) return;
            var bytes = new byte[samples.Length];
            for (int i = 0; i < samples.Length; i++) bytes[i] = VoiceCodec.Encode(samples[i]);
            var audio = new FootageAudio { Channel = channel, Signal = (byte)(Mathf.Clamp01(signal) * 255f), Samples = bytes };
            if (toClip) clip.Audio.Add(Place(audio, writeHead, source, recorded));
            if (toBox) boxAudio.Add(Place(audio, boxWriteHead, source, boxClock));
        }

        /// <summary>
        /// Places a piece of sound so it ends at <paramref name="clock"/>, but never over the end of the same
        /// source's previous piece: pieces that arrive together (one frame, one packet) play one after another.
        /// </summary>
        static FootageAudio Place(FootageAudio audio, Dictionary<int, int> heads, int source, float clock)
        {
            int offset = Mathf.Max(0, Mathf.RoundToInt(clock * VoiceCodec.SampleRate) - audio.Samples.Length);
            if (heads.TryGetValue(source, out int head)) offset = Mathf.Max(offset, head);
            heads[source] = offset + audio.Samples.Length;
            audio.Offset = offset;
            return audio;
        }

        void Update()
        {
            if (!IsOwner) return;
            // Test shortcut: F4 sends what you've filmed straight to the chip reader.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame && HasFootage && FootageArchive.Instance != null)
            {
                var archive = FootageArchive.Instance;
                archive.Submit(archive.NewChipId(), TakeChip(endsInDeath: false), ChipStatus.Inserted);
                ExpeditionAnnounceLocal("TEST: YOUR FOOTAGE IS IN THE CHIP READER - open the Footage app");
            }
            bool boxLive = BoxLive;
            if (boxLive) boxClock += Time.deltaTime;
            bool canFilm = diver.IsDiving && (health == null || !health.IsDead) && !GetComponent<FirstPersonController>().InputLocked;
            bool wasRecording = recording;
            recording = canFilm && Controls.Held(GameAction.Record) && FilmLeft > 0f;
            if (recording)
            {
                if (clip == null) NewChip();
                if (!wasRecording)
                {
                    // Starting again after a pause: the next frame begins a new take.
                    newTake = clip.Frames.Count > 0;
                    sampleTimer = 0f;
                }
                recorded += Time.deltaTime;
                sampleTimer -= Time.deltaTime;
                if (sampleTimer <= 0f)
                {
                    sampleTimer = SampleInterval;
                    clip.Frames.Add(new FootageFrame { Time = recorded, Position = head.position, Rotation = head.rotation, Lamp = headlamp.enabled, Cut = newTake });
                    newTake = false;
                    lastManualBoxClock = boxClock; // the black box up to here is already on the chip
                }
            }
            if (boxLive) RecordBox();
            bool flashing = Time.time < flashUntil;
            recText.gameObject.SetActive(recording || flashing || (diver.IsDiving && FilmLeft <= 0f));
            recText.text = recording
                ? $"<color=#ff3020>●</color> REC  {TimeSpan.FromSeconds(recorded):mm\\:ss}   FILM {FilmLeft:0}s"
                : flashing ? flashText
                : "FILM FULL - put the chip in the sub's reader";
        }

        float flashUntil;
        string flashText;

        void ExpeditionAnnounceLocal(string message)
        {
            flashText = message;
            flashUntil = Time.time + 4f;
        }

        // ------------------------------------------------------------------ black box

        /// <summary>Samples the view like a recording does, and forgets anything older than BoxSeconds.</summary>
        void RecordBox()
        {
            boxTimer -= Time.deltaTime;
            if (boxTimer <= 0f)
            {
                boxTimer = SampleInterval;
                box.Add(new FootageFrame { Time = boxClock, Position = head.position, Rotation = head.rotation, Lamp = headlamp.enabled });
            }
            float cutoff = boxClock - BoxSeconds;
            int old = 0;
            while (old < box.Count && box[old].Time < cutoff) old++;
            if (old > 0) box.RemoveRange(0, old);
            int cutoffSample = Mathf.FloorToInt(cutoff * VoiceCodec.SampleRate);
            old = 0;
            while (old < boxAudio.Count && boxAudio[old].Offset + boxAudio[old].Samples.Length < cutoffSample) old++;
            if (old > 0) boxAudio.RemoveRange(0, old);
        }

        void ClearBox()
        {
            box.Clear();
            boxAudio.Clear();
            boxWriteHead.Clear();
            boxClock = boxTimer = 0f;
            lastManualBoxClock = -1f;
        }

        /// <summary>
        /// On death: adds the black box's last seconds to the chip (a new one if the camera has none),
        /// after a cut, leaving out the moments that were already filmed onto it.
        /// </summary>
        void AppendBlackBox()
        {
            if (box.Count > 1)
            {
                if (clip == null)
                {
                    NewChip();
                    clip.Title += " / AUTO";
                }
                int first = 0;
                while (first < box.Count && box[first].Time <= lastManualBoxClock) first++;
                if (box.Count - first >= 2)
                {
                    bool hadFrames = clip.Frames.Count > 0;
                    float start = hadFrames ? clip.Duration + SampleInterval : 0f;
                    float offset = start - box[first].Time;
                    for (int i = first; i < box.Count; i++)
                    {
                        var f = box[i];
                        f.Time += offset;
                        f.Cut = i == first && hadFrames;
                        clip.Frames.Add(f);
                    }
                    int shift = Mathf.RoundToInt(offset * VoiceCodec.SampleRate);
                    int firstSample = Mathf.RoundToInt(box[first].Time * VoiceCodec.SampleRate);
                    int startSample = Mathf.RoundToInt(start * VoiceCodec.SampleRate);
                    foreach (var a in boxAudio)
                    {
                        if (a.Offset + a.Samples.Length <= firstSample) continue; // already on the chip
                        var moved = a;
                        moved.Offset = Mathf.Max(startSample, a.Offset + shift);
                        clip.Audio.Add(moved);
                    }
                }
            }
            ClearBox();
        }

        void NewChip()
        {
            clip = new FootageClip
            {
                Diver = GetComponent<PlayerNetwork>().CrewNumber,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm"),
            };
            int expedition = CrewProgress.Instance != null ? CrewProgress.Instance.Expedition : 0;
            clip.Title = $"D{clip.Diver} HELMET CAM / EXP #{expedition}";
            recorded = 0f;
            sampleTimer = 0f;
            writeHead.Clear();
            lastManualBoxClock = -1f;
        }

        /// <summary>
        /// Owner: pull the chip out of the camera (for the reader, or into a body on death).
        /// On death the black box's last seconds are added first, so there is almost always a chip.
        /// </summary>
        public FootageClip TakeChip(bool endsInDeath)
        {
            if (endsInDeath) AppendBlackBox();
            lastManualBoxClock = -1f;
            if (!HasFootage) return null;
            var taken = clip;
            taken.EndsInDeath = endsInDeath;
            clip = null;
            recorded = 0f;
            return taken;
        }

        string HudLine()
        {
            var sb = new StringBuilder($"CAMERA  FILM {FilmLeft:0}s  (hold {Controls.Label(GameAction.Record)} to record)");
            var archive = FootageArchive.Instance;
            if (archive != null && archive.IsSpawned)
            {
                int carried = 0;
                foreach (var c in archive.Chips)
                    if (c.Status == ChipStatus.Carried && c.Carrier == OwnerClientId) carried++;
                if (carried > 0) sb.Append($"    <color=#ffd060>CARRYING {carried} CHIP(S)</color>");
            }
            return sb.ToString();
        }

        void BuildOverlay()
        {
            var canvasGo = new GameObject("RecHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;
            var go = new GameObject("Rec", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(28f, -24f);
            rt.sizeDelta = new Vector2(700f, 40f);
            recText = go.GetComponent<Text>();
            recText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            recText.fontSize = 26;
            recText.fontStyle = FontStyle.Bold;
            recText.color = Color.white;
            recText.raycastTarget = false;
            recText.gameObject.SetActive(false);
        }
    }
}
