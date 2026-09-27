using System;
using System.Collections.Generic;
using System.Text;
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
    /// A dead diver is never heard from their body, and keeps hearing the radio at full strength.
    /// </summary>
    public class PlayerVoice : NetworkBehaviour
    {
        const int FrameSamples = VoiceCodec.SampleRate / 50;   // 20 ms
        const int FramesPerPacket = 2;                          // send every 40 ms
        const float VoiceHoldSeconds = 0.45f;
        const byte FlagProximity = 1, FlagRadio = 2;
        // Design decision pending: can the dead still talk on the radio? false makes them listen-only.
        const bool DeadCanUseRadio = true;
        const float CueVolume = 0.15f;

        [SerializeField] Transform head;
        [SerializeField] Renderer radioLight;
        [SerializeField] ParticleSystem bubbles;

        readonly NetworkVariable<bool> radioTx = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> talking = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public static readonly List<PlayerVoice> All = new();
        public static readonly List<RadioLogEntry> RadioLog = new();
        public static bool MicMuted { get; private set; }

        /// <summary>
        /// Local player's own voice, as 20 ms frames of 8 kHz audio that were sent (for the helmet camera).
        /// The array is reused for the next frame: handlers must copy what they need immediately.
        /// </summary>
        public static event Action<float[]> LocalVoiceFrame;
        /// <summary>
        /// Another player's voice arrived here: (speaker, samples, isRadio, sender signal).
        /// The samples array is reused for the next packet: handlers must copy what they need immediately.
        /// </summary>
        public static event Action<PlayerVoice, float[], bool, float> RemoteVoiceHeard;

        PlayerNetwork net;
        DiverController diver;
        DiverHealth health;
        VoiceOutput proximityOut, radioOut;

        // Remote-side state.
        float lastRadioHeard = -10f, radioStarted = -1f, heardSignal = 1f;
        float[] decoded = Array.Empty<float>();
        public float ReceivedLevel { get; private set; }

        // Owner-side capture state.
        string micDevice;
        AudioClip micClip;
        int micRate, lastMicPos;
        float resamplePhase, micLevel, voiceHold, txStarted = -1f;
        readonly List<float> pending = new();
        readonly float[] frame = new float[FrameSamples];
        readonly byte[] outgoing = new byte[FrameSamples * FramesPerPacket];
        int outgoingCount;
        float[] readBuffer = new float[4096];
        Text hud;
        readonly StringBuilder hudText = new();

        // Push-to-talk cues, heard only by the talker.
        static AudioClip talkPermitClip, rogerClip;
        AudioSource cueSource;

        public bool RadioTransmitting => radioTx.Value;
        public bool Talking => talking.Value;
        public int CrewNumber => net != null ? net.CrewNumber : (int)OwnerClientId + 1;
        public bool IsDiving => diver != null && diver.IsDiving;
        /// <summary>This player's radio is live, as heard here (their packets are arriving).</summary>
        public bool RadioHeardRecently => IsOwner ? radioTx.Value : Time.time - lastRadioHeard < 0.35f;

        bool Dead => health != null && health.IsDead;

        void Awake()
        {
            net = GetComponent<PlayerNetwork>();
            diver = GetComponent<DiverController>();
            health = GetComponent<DiverHealth>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner)
            {
                // A new session: the last one's transmissions don't belong in this log.
                RadioLog.Clear();
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
                emission.rateOverTime = talking.Value && IsDiving && !Dead ? 18f : 0f;
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
            // The dead spectate their body, which can be far behind the sub, but the radio stays clear.
            bool listenerDead = local != null && local.TryGetComponent<DiverHealth>(out var listenerHealth) && listenerHealth.IsDead;
            // Proximity: only if you're both in the water or both in the sub.
            proximityOut.Configure(listenerDiving == IsDiving ? volume : 0f, 1f, IsDiving);
            // Radio: the worse of the two ends' signal.
            float listenerSignal = listenerDiving && !listenerDead ? SignalModel.Strength(local.transform.position) : 1f;
            radioOut.Configure(volume, Mathf.Min(heardSignal, listenerSignal), false);
            // While they're keyed, a late packet is a gap in the over, not its end (no squelch mid-sentence).
            radioOut.SetKeyed(radioTx.Value || Time.time - lastRadioHeard < 0.25f);
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        void VoiceRpc(byte[] encoded, byte flags, byte senderSignal)
        {
            if (proximityOut == null) return;
            // Never heard from a dead speaker's body (covers packets sent just before they died).
            bool dead = Dead;
            bool toProximity = (flags & FlagProximity) != 0 && !dead;
            bool toRadio = (flags & FlagRadio) != 0 && (!dead || DeadCanUseRadio);
            if (!toProximity && !toRadio) return;

            if (decoded.Length != encoded.Length) decoded = new float[encoded.Length];
            float peak = 0f;
            for (int i = 0; i < encoded.Length; i++)
            {
                decoded[i] = VoiceCodec.Decode(encoded[i]);
                peak = Mathf.Max(peak, Mathf.Abs(decoded[i]));
            }
            ReceivedLevel = Mathf.Max(ReceivedLevel, Mathf.Clamp01(peak * 3f));
            if (toProximity)
            {
                proximityOut.Push(decoded);
                RemoteVoiceHeard?.Invoke(this, decoded, false, 1f);
            }
            if (toRadio)
            {
                radioOut.Push(decoded);
                RemoteVoiceHeard?.Invoke(this, decoded, true, senderSignal / 255f);
                heardSignal = senderSignal / 255f;
                if (radioStarted < 0f) radioStarted = Time.time;
                lastRadioHeard = Time.time;
            }
        }

        // ------------------------------------------------------------------ talking

        void UpdateOwner()
        {
            bool dead = Dead;
            bool radioAllowed = !dead || DeadCanUseRadio;
            if (Controls.Pressed(GameAction.MuteMic)) MicMuted = !MicMuted;
            // Dev test (-testtone): a beeping 600 Hz tone over the radio instead of the microphone.
            bool testTone = TestTone && radioAllowed;
            bool radioHeld = radioAllowed && (testTone || Controls.Held(GameAction.Radio));
            if (radioTx.Value != radioHeld)
            {
                radioTx.Value = radioHeld;
                if (radioHeld) txStarted = Time.time;
                else if (txStarted >= 0f) AddLog(CrewNumber, Time.time - txStarted, OwnSignal());
                PlayCue(radioHeld ? talkPermitClip : rogerClip);
            }

            if (testTone)
            {
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

                bool proximity = !MicMuted && voiceHold > 0f && !dead;
                bool radio = radioHeld; // push-to-talk works even when muted
                if (!proximity && !radio) { outgoingCount = 0; continue; }
                anyVoice = true;
                LocalVoiceFrame?.Invoke(frame);

                for (int i = 0; i < FrameSamples; i++) outgoing[outgoingCount++] = VoiceCodec.Encode(Mathf.Clamp(frame[i] * 1.4f, -1f, 1f));
                if (outgoingCount >= outgoing.Length)
                {
                    // Radio is also heard next to a living talker, but never from a dead one's body.
                    byte flags = (byte)((proximity || (radio && !dead) ? FlagProximity : 0) | (radio ? FlagRadio : 0));
                    // The RPC serialises the array during the call, so reusing it is safe.
                    VoiceRpc(outgoing, flags, (byte)(OwnSignal() * 255f));
                    outgoingCount = 0;
                }
            }
            voiceHold -= Time.deltaTime;
            micLevel = Mathf.MoveTowards(micLevel, 0f, Time.deltaTime);
            bool nowTalking = !dead && (anyVoice || voiceHold > 0f && !MicMuted);
            if (talking.Value != nowTalking) talking.Value = nowTalking;
            UpdateHud(radioHeld, dead);
        }

        // The dead transmit clear, like they listen: their body can be far behind the sub.
        float OwnSignal() => IsDiving && !Dead ? SignalModel.Strength(transform.position) : 1f;

        void OnSettingsChanged()
        {
            if (GameSettings.MicDeviceName != micDevice) StartMicrophone();
        }

        static readonly bool TestTone = Array.IndexOf(Environment.GetCommandLineArgs(), "-testtone") >= 0;
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

        // ------------------------------------------------------------------ push-to-talk cues

        void PlayCue(AudioClip cue)
        {
            if (cueSource != null && cue != null) cueSource.PlayOneShot(cue, CueVolume * GameSettings.EffectsVolume);
        }

        /// <summary>A short square-wave beep (optionally two tones), softened and faded so it doesn't click.</summary>
        static AudioClip MakeBeep(string name, float firstHz, float firstSeconds, float secondHz, float secondSeconds)
        {
            const int Rate = 22050;
            int firstCount = Mathf.RoundToInt(firstSeconds * Rate);
            var data = new float[firstCount + Mathf.RoundToInt(secondSeconds * Rate)];
            int fade = Rate / 500; // 2 ms
            float phase = 0f, smooth = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                phase += (i < firstCount ? firstHz : secondHz) / Rate;
                if (phase >= 1f) phase -= 1f;
                float square = phase < 0.5f ? 1f : -1f;
                smooth += 0.45f * (square - smooth); // takes the harsh edge off, like a small speaker
                float envelope = Mathf.Clamp01(Mathf.Min(i, data.Length - 1 - i) / (float)fade);
                data[i] = smooth * envelope * 0.8f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
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

            // Talk-permit chirp on key-down, roger beep on key-up: local only, never sent to anyone.
            if (talkPermitClip == null) talkPermitClip = MakeBeep("RadioTalkPermit", 1000f, 0.04f, 1500f, 0.04f);
            if (rogerClip == null) rogerClip = MakeBeep("RadioRoger", 1200f, 0.06f, 0f, 0f);
            cueSource = canvasGo.AddComponent<AudioSource>();
            cueSource.playOnAwake = false;
            cueSource.spatialBlend = 0f;

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

        void UpdateHud(bool radioHeld, bool dead)
        {
            if (hud == null) return;
            var sb = hudText.Clear();
            foreach (var other in All)
            {
                if (other == this || !other.RadioHeardRecently) continue;
                sb.Append($"<color=#7fe0ff>RX  D{other.CrewNumber}  {SignalModel.Bars(other.heardSignal)}  {other.heardSignal * 100f:0}%</color>\n");
            }
            if (radioHeld)
                sb.Append($"<color=#ff5040>● RADIO TX</color>   SIG {SignalModel.Bars(OwnSignal())} {OwnSignal() * 100f:0}%\n");
            else if (!dead || DeadCanUseRadio)
                sb.Append($"<size=15>RADIO: hold {Controls.Label(GameAction.Radio)}</size>\n");

            if (dead) sb.Append(DeadCanUseRadio ? "<color=#ff8060>NO VITALS - RADIO ONLY</color>" : "<color=#ff8060>NO VITALS - LISTENING ONLY</color>");
            else if (micClip == null) sb.Append("<color=#ff8060>NO MICROPHONE</color>");
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
