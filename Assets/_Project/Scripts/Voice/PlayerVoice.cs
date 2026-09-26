using System;
using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Voice
{
    /// <summary>One entry of the radio channel's history, for the sub's Radio app.</summary>
    public struct RadioLogEntry
    {
        public string Time;
        public int Crew;
        public float Seconds;
        public float Signal;
    }

    /// <summary>
    /// Voice for one player. The owner captures the microphone and sends it in small mu-law packets:
    ///  - proximity voice (voice-activated, M mutes): heard nearby, muffled underwater, never through the hull
    ///  - radio (hold the radio key): everyone hears it through their walkie-talkie, degraded by signal.
    /// Everyone else plays it back through two <see cref="VoiceOutput"/>s on this player's head.
    /// </summary>
    public class PlayerVoice : NetworkBehaviour
    {
        const int FrameSamples = VoiceCodec.SampleRate / 50;   // 20 ms
        const int FramesPerPacket = 2;                          // send every 40 ms
        const float VoiceHoldSeconds = 0.45f;
        const byte FlagProximity = 1, FlagRadio = 2;

        [SerializeField] Transform head;
        [SerializeField] Renderer radioLight;
        [SerializeField] ParticleSystem bubbles;

        readonly NetworkVariable<bool> radioTx = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> talking = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public static readonly List<PlayerVoice> All = new();
        public static readonly List<RadioLogEntry> RadioLog = new();
        public static bool MicMuted { get; private set; }

        PlayerNetwork net;
        DiverController diver;
        VoiceOutput proximityOut, radioOut;

        // Remote-side state.
        float lastRadioHeard = -10f, radioStarted = -1f, heardSignal = 1f;
        public float ReceivedLevel { get; private set; }

        // Owner-side capture state.
        string micDevice;
        AudioClip micClip;
        int micRate, lastMicPos;
        float resamplePhase, micLevel, voiceHold, txStarted = -1f;
        readonly List<float> pending = new();
        readonly List<byte> outgoing = new();
        float[] readBuffer = new float[4096];
        Text hud;

        public bool RadioTransmitting => radioTx.Value;
        public bool Talking => talking.Value;
        public int CrewNumber => net != null ? net.CrewNumber : (int)OwnerClientId + 1;
        public bool IsDiving => diver != null && diver.IsDiving;
        /// <summary>This player's radio is live, as heard here (their packets are arriving).</summary>
        public bool RadioHeardRecently => IsOwner ? radioTx.Value : Time.time - lastRadioHeard < 0.35f;

        void Awake()
        {
            net = GetComponent<PlayerNetwork>();
            diver = GetComponent<DiverController>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner)
            {
                StartMicrophone();
                GameSettings.Changed += OnSettingsChanged;
                BuildHud();
            }
            else
            {
                proximityOut = MakeOutput("ProximityVoice", VoiceOutput.Mode.Proximity);
                radioOut = MakeOutput("RadioVoice", VoiceOutput.Mode.Radio);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsOwner)
            {
                GameSettings.Changed -= OnSettingsChanged;
                StopMicrophone();
            }
        }

        VoiceOutput MakeOutput(string name, VoiceOutput.Mode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(head, false);
            var output = go.AddComponent<VoiceOutput>();
            output.Init(mode);
            return output;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (radioLight != null) radioLight.enabled = radioTx.Value;
            if (bubbles != null)
            {
                var emission = bubbles.emission;
                emission.rateOverTime = talking.Value && IsDiving ? 18f : 0f;
            }

            if (IsOwner) UpdateOwner();
            else UpdateRemote();
        }

        // ------------------------------------------------------------------ listening

        void UpdateRemote()
        {
            ReceivedLevel = Mathf.MoveTowards(ReceivedLevel, 0f, Time.deltaTime * 2f);

            // A radio transmission from this player just ended: log it.
            if (radioStarted >= 0f && Time.time - lastRadioHeard > 0.4f)
            {
                AddLog(CrewNumber, lastRadioHeard - radioStarted, heardSignal);
                radioStarted = -1f;
            }

            var local = PlayerNetwork.Local;
            float volume = local == null ? 0f : GameSettings.VoiceVolume;
            bool listenerDiving = local != null && local.GetComponent<DiverController>().IsDiving;
            // Proximity: only if you're both in the water or both in the sub.
            proximityOut.Configure(listenerDiving == IsDiving ? volume : 0f, 1f, IsDiving);
            // Radio: the worse of the two ends' signal.
            float listenerSignal = listenerDiving ? SignalModel.Strength(local.transform.position) : 1f;
            radioOut.Configure(volume, Mathf.Min(heardSignal, listenerSignal), false);
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        void VoiceRpc(byte[] encoded, byte flags, byte senderSignal)
        {
            if (proximityOut == null) return;
            var samples = new float[encoded.Length];
            float peak = 0f;
            for (int i = 0; i < encoded.Length; i++)
            {
                samples[i] = VoiceCodec.Decode(encoded[i]);
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            ReceivedLevel = Mathf.Max(ReceivedLevel, Mathf.Clamp01(peak * 3f));
            if ((flags & FlagProximity) != 0) proximityOut.Push(samples);
            if ((flags & FlagRadio) != 0)
            {
                radioOut.Push(samples);
                heardSignal = senderSignal / 255f;
                if (radioStarted < 0f) radioStarted = Time.time;
                lastRadioHeard = Time.time;
            }
        }

        // ------------------------------------------------------------------ talking

        void UpdateOwner()
        {
            if (Controls.Pressed(GameAction.MuteMic)) MicMuted = !MicMuted;
            bool radioHeld = Controls.Held(GameAction.Radio);
            if (radioTx.Value != radioHeld)
            {
                radioTx.Value = radioHeld;
                if (radioHeld) txStarted = Time.time;
                else if (txStarted >= 0f) AddLog(CrewNumber, Time.time - txStarted, OwnSignal());
            }

            if (TestTone)
            {
                // Dev test (-testtone): a beeping 600 Hz tone over the radio instead of the microphone.
                radioHeld = true;
                if (!radioTx.Value) radioTx.Value = true;
                int needed = Mathf.RoundToInt(Time.deltaTime * VoiceCodec.SampleRate);
                for (int i = 0; i < needed; i++)
                {
                    toneSample++;
                    bool on = toneSample % 8000 < 4000;
                    pending.Add(on ? Mathf.Sin(toneSample * 2f * Mathf.PI * 600f / VoiceCodec.SampleRate) * 0.4f : 0f);
                }
            }
            else ReadMicrophone();
            float threshold = Mathf.Lerp(0.08f, 0.006f, GameSettings.MicSensitivity);
            var frame = new float[FrameSamples];
            bool anyVoice = false;
            while (pending.Count >= FrameSamples)
            {
                pending.CopyTo(0, frame, 0, FrameSamples);
                pending.RemoveRange(0, FrameSamples);
                float sum = 0f;
                foreach (float s in frame) sum += s * s;
                float rms = Mathf.Sqrt(sum / FrameSamples);
                micLevel = Mathf.Max(rms * 6f, micLevel * 0.9f);
                if (rms > threshold) voiceHold = VoiceHoldSeconds;

                bool proximity = !MicMuted && voiceHold > 0f;
                bool radio = radioHeld; // push-to-talk works even when muted
                if (!proximity && !radio) { outgoing.Clear(); continue; }
                anyVoice = true;

                foreach (float s in frame) outgoing.Add(VoiceCodec.Encode(Mathf.Clamp(s * 1.4f, -1f, 1f)));
                if (outgoing.Count >= FrameSamples * FramesPerPacket)
                {
                    byte flags = (byte)((proximity || radio ? FlagProximity : 0) | (radio ? FlagRadio : 0));
                    VoiceRpc(outgoing.ToArray(), flags, (byte)(OwnSignal() * 255f));
                    outgoing.Clear();
                }
            }
            voiceHold -= Time.deltaTime;
            micLevel = Mathf.MoveTowards(micLevel, 0f, Time.deltaTime);
            bool nowTalking = anyVoice || voiceHold > 0f && !MicMuted;
            if (talking.Value != nowTalking) talking.Value = nowTalking;
            UpdateHud(radioHeld);
        }

        float OwnSignal() => IsDiving ? SignalModel.Strength(transform.position) : 1f;

        void OnSettingsChanged()
        {
            if (GameSettings.MicDeviceName != micDevice) StartMicrophone();
        }

        static bool TestTone => Array.IndexOf(Environment.GetCommandLineArgs(), "-testtone") >= 0;
        int toneSample;

        void StartMicrophone()
        {
            StopMicrophone();
            if (TestTone || Microphone.devices.Length == 0) return;
            micDevice = GameSettings.MicDeviceName;
            Microphone.GetDeviceCaps(micDevice, out int minRate, out int maxRate);
            micRate = maxRate == 0 ? 16000 : Mathf.Clamp(16000, minRate, maxRate);
            micClip = Microphone.Start(micDevice, true, 1, micRate);
            micRate = micClip != null ? micClip.frequency : micRate;
            lastMicPos = 0;
            pending.Clear();
        }

        void StopMicrophone()
        {
            if (micClip != null) Microphone.End(micDevice);
            micClip = null;
        }

        /// <summary>Pulls new microphone samples and resamples them to 8 kHz into <see cref="pending"/>.</summary>
        void ReadMicrophone()
        {
            if (micClip == null) return;
            int pos = Microphone.GetPosition(micDevice);
            if (pos < 0) return;
            int total = micClip.samples;
            int available = (pos - lastMicPos + total) % total;
            if (available == 0) return;
            if (readBuffer.Length < available) readBuffer = new float[available];

            int firstPart = Mathf.Min(available, total - lastMicPos);
            var first = new float[firstPart];
            micClip.GetData(first, lastMicPos);
            Array.Copy(first, readBuffer, firstPart);
            if (available > firstPart)
            {
                var second = new float[available - firstPart];
                micClip.GetData(second, 0);
                Array.Copy(second, 0, readBuffer, firstPart, second.Length);
            }
            lastMicPos = pos;

            // Downsample with a simple averaging window.
            float step = micRate / (float)VoiceCodec.SampleRate;
            float acc = 0f;
            int n = 0;
            for (int i = 0; i < available; i++)
            {
                acc += readBuffer[i];
                n++;
                resamplePhase += 1f;
                if (resamplePhase >= step)
                {
                    resamplePhase -= step;
                    pending.Add(acc / n);
                    acc = 0f;
                    n = 0;
                }
            }
            // Never let a stalled frame loop build up a backlog.
            if (pending.Count > VoiceCodec.SampleRate / 2) pending.RemoveRange(0, pending.Count - VoiceCodec.SampleRate / 4);
        }

        static void AddLog(int crew, float seconds, float signal)
        {
            if (seconds < 0.25f) return;
            RadioLog.Add(new RadioLogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Crew = crew, Seconds = seconds, Signal = signal });
            if (RadioLog.Count > 40) RadioLog.RemoveAt(0);
        }

        // ------------------------------------------------------------------ HUD

        void BuildHud()
        {
            var canvasGo = new GameObject("VoiceHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Readout", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-28f, 24f);
            rt.sizeDelta = new Vector2(520f, 200f);
            hud = go.GetComponent<Text>();
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.fontSize = 20;
            hud.fontStyle = FontStyle.Bold;
            hud.alignment = TextAnchor.LowerRight;
            hud.color = new Color(0.75f, 0.95f, 0.85f, 0.9f);
            hud.raycastTarget = false;
        }

        void UpdateHud(bool radioHeld)
        {
            if (hud == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var other in All)
            {
                if (other == this || !other.RadioHeardRecently) continue;
                sb.Append($"<color=#7fe0ff>RX  D{other.CrewNumber}  {SignalModel.Bars(other.heardSignal)}  {other.heardSignal * 100f:0}%</color>\n");
            }
            if (radioHeld)
                sb.Append($"<color=#ff5040>● RADIO TX</color>   SIG {SignalModel.Bars(OwnSignal())} {OwnSignal() * 100f:0}%\n");
            else
                sb.Append($"<size=15>RADIO: hold {Controls.Label(GameAction.Radio)}</size>\n");

            if (micClip == null) sb.Append("<color=#ff8060>NO MICROPHONE</color>");
            else if (MicMuted) sb.Append($"<color=#ff8060>MIC MUTED</color> <size=15>({Controls.Label(GameAction.MuteMic)})</size>");
            else
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(micLevel * 10f), 0, 10);
                sb.Append($"MIC [{new string('|', bars)}{new string('.', 10 - bars)}] {(talking.Value ? "VOICE" : "")}");
            }
            hud.text = sb.ToString();
        }
    }
}
