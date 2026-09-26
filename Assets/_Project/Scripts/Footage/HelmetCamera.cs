using System;
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
    /// </summary>
    public class HelmetCamera : NetworkBehaviour
    {
        public const float FilmSeconds = 120f;
        const float SampleInterval = 0.1f;

        [SerializeField] Transform head;
        [SerializeField] Light headlamp;

        DiverController diver;
        DiverHealth health;
        FootageClip clip;
        float recorded, sampleTimer;
        bool recording;
        Text recText;

        public float FilmLeft => FilmSeconds - recorded;
        public bool HasFootage => clip != null && clip.Frames.Count > 1;
        public bool Recording => recording;

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
        }

        public override void OnNetworkDespawn()
        {
            PlayerVoice.LocalVoiceFrame -= OnOwnVoice;
            PlayerVoice.RemoteVoiceHeard -= OnVoiceHeard;
        }

        // ------------------------------------------------------------------ sound

        void OnOwnVoice(float[] samples)
        {
            if (recording) AddAudio(samples, FootageAudio.OwnVoice, 1f);
        }

        void OnVoiceHeard(PlayerVoice speaker, float[] samples, bool radio, float signal)
        {
            if (!recording) return;
            if (radio)
            {
                float mine = SignalModel.Strength(transform.position);
                AddAudio(samples, FootageAudio.Radio, Mathf.Min(signal, mine));
            }
            else if (speaker.IsDiving && Vector3.Distance(speaker.transform.position, transform.position) < 20f)
            {
                AddAudio(samples, FootageAudio.Nearby, 1f);
            }
        }

        void AddAudio(float[] samples, byte channel, float signal)
        {
            var bytes = new byte[samples.Length];
            for (int i = 0; i < samples.Length; i++) bytes[i] = VoiceCodec.Encode(samples[i]);
            clip.Audio.Add(new FootageAudio { Time = recorded, Channel = channel, Signal = (byte)(Mathf.Clamp01(signal) * 255f), Samples = bytes });
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
            bool canFilm = diver.IsDiving && (health == null || !health.IsDead) && !GetComponent<FirstPersonController>().InputLocked;
            recording = canFilm && Controls.Held(GameAction.Record) && FilmLeft > 0f;
            if (recording)
            {
                if (clip == null) NewChip();
                recorded += Time.deltaTime;
                sampleTimer -= Time.deltaTime;
                if (sampleTimer <= 0f)
                {
                    sampleTimer = SampleInterval;
                    clip.Frames.Add(new FootageFrame { Time = recorded, Position = head.position, Rotation = head.rotation, Lamp = headlamp.enabled });
                }
            }
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
        }

        /// <summary>Owner: pull the chip out of the camera (for the reader, or into a body on death).</summary>
        public FootageClip TakeChip(bool endsInDeath)
        {
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
