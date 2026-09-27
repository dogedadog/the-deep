using System;
using TheDeep.Core;
using TheDeep.Submarine;
using TheDeep.Voice;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDeep.Footage
{
    /// <summary>
    /// Re-films recorded footage: a hidden camera flies the recorded path and renders into a texture
    /// for the Footage app. Its lamp is switched on only while this camera renders, so replaying
    /// footage never lights up the real world for players. The same goes for the water: it renders
    /// with the fog and light of the depth the footage was filmed at, not of where the sub is now.
    /// Things only seen in authored footage (the creature over Team 7) live on the FootageProxy layer,
    /// which only this camera draws. The sound is mixed once per clip and fed out with the picture.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FootagePlayback : MonoBehaviour
    {
        const int Rate = VoiceCodec.SampleRate;
        const int Block = Rate / 50;         // 20 ms: the radio's signal is kept per block
        const int MaxSamples = Rate * 150;   // longest sound track played

        static readonly float[] Decoded = BuildDecodeTable();

        [SerializeField] Light lamp;
        [SerializeField] Transform proxy;

        Camera cam;
        FootageClip clip;
        VoiceOutput voiceOut, helmetOut, radioOut;
        int renderFrames;
        bool lampWanted;

        // The loaded clip's sound, mixed into one track per speaker (arrays are reused between clips).
        float[] voiceTrack, helmetTrack, radioTrack;
        bool[] radioActive;
        byte[] radioSignal;
        bool hasVoice, hasHelmet, hasRadio;
        int trackLength, cursor;
        readonly float[][] slices = new float[Block + 1][];

        // The world's water, set aside while this camera renders in the footage's own.
        Light downwelling;
        bool downwellingSearched, waterSwapped;
        Color savedFogColor;
        float savedFogDensity, savedDownwelling;

        public float Time { get; private set; }
        public bool Playing { get; set; }
        public FootageClip Clip => clip;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.enabled = false;
            lamp.enabled = false;
            if (proxy != null) proxy.gameObject.SetActive(false);
            voiceOut = MakeOutput("FootageVoice", VoiceOutput.Mode.Proximity);
            helmetOut = MakeOutput("FootageHelmet", VoiceOutput.Mode.Proximity);
            radioOut = MakeOutput("FootageRadio", VoiceOutput.Mode.Radio);
        }

        VoiceOutput MakeOutput(string name, VoiceOutput.Mode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var output = go.AddComponent<VoiceOutput>();
            output.Init(mode, spatial: false);
            return output;
        }

        // ------------------------------------------------------------------ sound

        static float[] BuildDecodeTable()
        {
            var table = new float[256];
            for (int i = 0; i < table.Length; i++) table[i] = VoiceCodec.Decode((byte)i);
            return table;
        }

        /// <summary>
        /// Mixes the clip's sound into tracks once, so people talking over each other play together:
        /// voices (heard through water), the suit's own sounds, and the radio with its signal per block.
        /// </summary>
        void BuildTracks()
        {
            hasVoice = hasHelmet = hasRadio = false;
            trackLength = 0;
            if (clip == null) return;
            int length = Mathf.CeilToInt(clip.Duration * Rate);
            foreach (var a in clip.Audio)
            {
                if (a.Samples == null || a.Samples.Length == 0) continue;
                length = Mathf.Max(length, a.Offset + a.Samples.Length);
                if (a.Channel == FootageAudio.Radio) hasRadio = true;
                else if (a.Channel == FootageAudio.Helmet) hasHelmet = true;
                else hasVoice = true;
            }
            trackLength = Mathf.Min(length, MaxSamples);
            if (hasVoice) Prepare(ref voiceTrack, trackLength);
            if (hasHelmet) Prepare(ref helmetTrack, trackLength);
            if (hasRadio)
            {
                Prepare(ref radioTrack, trackLength);
                int blocks = (trackLength + Block - 1) / Block;
                if (radioActive == null || radioActive.Length < blocks)
                {
                    radioActive = new bool[blocks];
                    radioSignal = new byte[blocks];
                }
                else Array.Clear(radioActive, 0, blocks);
            }

            foreach (var a in clip.Audio)
            {
                if (a.Samples == null) continue;
                var track = a.Channel == FootageAudio.Radio ? radioTrack : a.Channel == FootageAudio.Helmet ? helmetTrack : voiceTrack;
                int from = Mathf.Max(0, a.Offset), to = Mathf.Min(trackLength, a.Offset + a.Samples.Length);
                for (int i = from; i < to; i++) track[i] += Decoded[a.Samples[i - a.Offset]];
                if (a.Channel != FootageAudio.Radio || to <= from) continue;
                // The radio is open over these blocks, as bad as the worst signal heard in them.
                for (int b = from / Block; b <= (to - 1) / Block; b++)
                {
                    radioSignal[b] = radioActive[b] ? Math.Min(radioSignal[b], a.Signal) : a.Signal;
                    radioActive[b] = true;
                }
            }
            if (hasVoice) Limit(voiceTrack, trackLength);
            if (hasHelmet) Limit(helmetTrack, trackLength);
            if (hasRadio) Limit(radioTrack, trackLength);
        }

        static void Prepare(ref float[] track, int length)
        {
            if (track == null || track.Length < length) track = new float[length];
            else Array.Clear(track, 0, length);
        }

        /// <summary>Soft knee above 0.9: voices that add up past full scale round off instead of clipping.</summary>
        static void Limit(float[] track, int length)
        {
            for (int i = 0; i < length; i++)
            {
                float x = track[i], m = Mathf.Abs(x);
                if (m > 0.9f) track[i] = Mathf.Sign(x) * (0.9f + 0.1f * (float)Math.Tanh((m - 0.9f) / 0.1f));
            }
        }

        /// <summary>Feeds the speakers the sound up to sample <paramref name="end"/>, from where the last update stopped.</summary>
        void PlayAudioUpTo(int end)
        {
            end = Mathf.Min(end, trackLength);
            if (end <= cursor) return;
            float volume = GameSettings.VoiceVolume;
            // People the helmet mic heard were underwater; the suit's own sounds are right there.
            if (hasVoice) voiceOut.Configure(volume, 1f, true);
            if (hasHelmet) helmetOut.Configure(volume, 1f, false);
            int signal = -1;
            while (cursor < end)
            {
                int block = cursor / Block;
                int count = Mathf.Min(end, (block + 1) * Block) - cursor;
                if (hasVoice) voiceOut.Push(Slice(voiceTrack, cursor, count));
                if (hasHelmet) helmetOut.Push(Slice(helmetTrack, cursor, count));
                // The radio only gets sound while someone was transmitting, so it opens and closes like the real one.
                if (hasRadio && radioActive[block])
                {
                    if (radioSignal[block] != signal)
                    {
                        signal = radioSignal[block];
                        radioOut.Configure(volume, signal / 255f, false);
                    }
                    radioOut.Push(Slice(radioTrack, cursor, count));
                }
                cursor += count;
            }
        }

        /// <summary>Part of a track, copied into an array kept for that length (at most one block).</summary>
        float[] Slice(float[] track, int start, int count)
        {
            var slice = slices[count] ??= new float[count];
            Array.Copy(track, start, slice, 0, count);
            return slice;
        }

        void ResetAudio()
        {
            voiceOut.Clear();
            helmetOut.Clear();
            radioOut.Clear();
            cursor = Mathf.FloorToInt(Time * Rate);
        }

        // ------------------------------------------------------------------ playback

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
            RestoreWater();
        }

        public void Load(FootageClip footage, RenderTexture target)
        {
            clip = footage;
            cam.targetTexture = target;
            Time = 0f;
            Playing = true;
            renderFrames = 2;
            BuildTracks();
            ResetAudio();
        }

        public void Stop()
        {
            Playing = false;
            clip = null;
            cam.enabled = false;
            trackLength = 0;
            ResetAudio();
        }

        public void Seek(float t)
        {
            Time = clip == null ? 0f : Mathf.Clamp(t, 0f, clip.Duration);
            renderFrames = 2;
            ResetAudio();
        }

        void Update()
        {
            if (clip == null)
            {
                cam.enabled = false;
                return;
            }
            if (Playing)
            {
                Time += UnityEngine.Time.deltaTime;
                if (Time >= clip.Duration)
                {
                    Time = clip.Duration;
                    Playing = false;
                    renderFrames = Mathf.Max(renderFrames, 1); // still draw the final frame
                }
                PlayAudioUpTo(Mathf.FloorToInt(Time * Rate));
            }
            // Paused or finished: stop rendering. The texture keeps showing the last frame.
            cam.enabled = Playing || renderFrames > 0;
            if (renderFrames > 0) renderFrames--;
            if (clip.Frames.Count == 0) return;
            var frame = clip.Sample(Time);
            transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            if (proxy != null)
            {
                proxy.gameObject.SetActive(frame.Proxy);
                if (frame.Proxy) proxy.SetPositionAndRotation(frame.ProxyPosition, frame.ProxyRotation);
            }
            lampWanted = frame.Lamp;
        }

        // ------------------------------------------------------------------ rendering

        void BeforeRender(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering != cam) return;
            lamp.enabled = lampWanted;
            UseRecordedWater();
        }

        void AfterRender(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering != cam) return;
            lamp.enabled = false;
            RestoreWater();
        }

        /// <summary>
        /// Footage is re-rendered in the water it was filmed in: the fog and downwelling light of the
        /// dive station nearest the camera's depth (so Team 7's floor stays black when the sub is high up).
        /// </summary>
        void UseRecordedWater()
        {
            RestoreWater();
            var station = StationNearest(transform.position.y);
            if (station == null)
            {
                cam.backgroundColor = RenderSettings.fogColor;
                return;
            }
            var light = Downwelling();
            savedFogColor = RenderSettings.fogColor;
            savedFogDensity = RenderSettings.fogDensity;
            savedDownwelling = light != null ? light.intensity : 0f;
            waterSwapped = true;
            RenderSettings.fogColor = station.fogColor;
            RenderSettings.fogDensity = station.fogDensity;
            if (light != null) light.intensity = station.downwelling;
            cam.backgroundColor = station.fogColor;
        }

        void RestoreWater()
        {
            if (!waterSwapped) return;
            waterSwapped = false;
            RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogDensity = savedFogDensity;
            if (downwelling != null) downwelling.intensity = savedDownwelling;
        }

        static SubNavigation.Station StationNearest(float y)
        {
            var nav = SubNavigation.Instance;
            if (nav == null || nav.Stations == null) return null;
            SubNavigation.Station nearest = null;
            float best = float.MaxValue;
            foreach (var s in nav.Stations)
            {
                if (s == null || s.point == null) continue;
                float d = Mathf.Abs(s.point.position.y - y);
                if (d < best)
                {
                    best = d;
                    nearest = s;
                }
            }
            return nearest;
        }

        /// <summary>The faint light from above (found once): the DownwellingLight, else the first directional light.</summary>
        Light Downwelling()
        {
            if (downwelling != null || downwellingSearched) return downwelling;
            downwellingSearched = true;
            var go = GameObject.Find("DownwellingLight");
            if (go != null) downwelling = go.GetComponent<Light>();
            if (downwelling == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional)
                    {
                        downwelling = l;
                        break;
                    }
            return downwelling;
        }
    }
}
