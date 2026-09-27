using TheDeep.Core;
using TheDeep.Player;
using UnityEngine;

namespace TheDeep.Audio
{
    /// <summary>
    /// Plays sound effects from a fixed pool of 16 AudioSources. Not scene content: it creates itself
    /// (with <see cref="SfxDirector"/>) after the first scene loads and survives scene changes.
    /// 2D sounds are the helmet and UI and stay dry; 3D sounds are muffled while the local player is
    /// underwater. Levels are deliberately low: loud effects through speakers open voice mics.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        const int VoiceCount = 16;
        const float BaseGain = 0.6f;
        /// <summary>The Effects volume slider's PlayerPrefs key (written by GameSettings).</summary>
        const string VolumeKey = "set.sfxVolume";
        const float DryCutoff = 22000f, UnderwaterCutoff = 1400f;

        class Voice
        {
            public Transform Transform;
            public AudioSource Source;
            public AudioLowPassFilter LowPass;
            public bool Spatial;
            public float Started;
            public SfxLoop Loop;
        }

        static SfxPlayer instance;

        readonly Voice[] voices = new Voice[VoiceCount];
        float gain = BaseGain;
        bool underwater;
        PlayerNetwork localPlayer;
        DiverController localDiver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("SFX");
            DontDestroyOnLoad(go);
            go.AddComponent<SfxPlayer>();
            go.AddComponent<SfxDirector>();
        }

        void Awake()
        {
            instance = this;
            for (int i = 0; i < VoiceCount; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                var lowPass = go.AddComponent<AudioLowPassFilter>();
                lowPass.cutoffFrequency = DryCutoff;
                voices[i] = new Voice { Transform = go.transform, Source = source, LowPass = lowPass, Started = -1f };
            }
            ReadVolume();
            GameSettings.Changed += ReadVolume;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ReadVolume;
            if (instance == this) instance = null;
        }

        void ReadVolume() => gain = BaseGain * Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));

        /// <summary>A 2D (helmet or UI) sound. A variant below 0 picks one of the sound's variants at random.</summary>
        public static void Play(Sfx id, float vol = 1f, float pitch = 1f, int variant = -1)
        {
            if (instance != null) instance.Fire(id, variant, vol, pitch, false, Vector3.zero, 0f);
        }

        /// <summary>A 3D sound at a point in the world, fading out linearly to silence at <paramref name="maxDist"/> metres.</summary>
        public static void PlayAt(Sfx id, Vector3 pos, float vol = 1f, float maxDist = 15f, float pitch = 1f, int variant = -1)
        {
            if (instance != null) instance.Fire(id, variant, vol, pitch, true, pos, maxDist);
        }

        /// <summary>Starts a 2D loop. Keep the handle to change its Volume and Pitch, and Stop it to free the voice.</summary>
        public static SfxLoop Loop(Sfx id, float vol = 1f)
        {
            var loop = new SfxLoop(vol);
            if (instance != null) instance.StartLoop(id, loop);
            return loop;
        }

        void Fire(Sfx id, int variant, float vol, float pitch, bool spatial, Vector3 pos, float maxDist)
        {
            if (variant < 0) variant = Random.Range(0, SfxBank.Variants(id));
            var clip = SfxBank.Get(id, variant);
            var voice = Take();
            if (clip == null || voice == null) return;
            var s = voice.Source;
            s.Stop();
            s.clip = clip;
            s.loop = false;
            s.volume = Mathf.Clamp01(vol) * gain;
            s.pitch = pitch;
            s.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                voice.Transform.position = pos;
                s.maxDistance = Mathf.Max(maxDist, s.minDistance + 0.1f);
            }
            voice.Spatial = spatial;
            voice.LowPass.cutoffFrequency = spatial && underwater ? UnderwaterCutoff : DryCutoff;
            voice.Started = Time.unscaledTime;
            s.Play();
        }

        void StartLoop(Sfx id, SfxLoop loop)
        {
            var clip = SfxBank.Get(id);
            var voice = Take();
            if (clip == null || voice == null) return;
            var s = voice.Source;
            s.Stop();
            s.clip = clip;
            s.loop = true;
            s.volume = Mathf.Clamp01(loop.Volume) * gain;
            s.pitch = loop.Pitch;
            s.spatialBlend = 0f;
            voice.Spatial = false;
            voice.LowPass.cutoffFrequency = DryCutoff;
            voice.Started = Time.unscaledTime;
            voice.Loop = loop;
            loop.HasVoice = true;
            s.Play();
        }

        /// <summary>An idle voice, or else the oldest one-shot (loops are never stolen).</summary>
        Voice Take()
        {
            Voice oldest = null;
            foreach (var v in voices)
            {
                if (v.Loop != null) continue;
                if (!v.Source.isPlaying) return v;
                if (oldest == null || v.Started < oldest.Started) oldest = v;
            }
            return oldest;
        }

        void Update()
        {
            var local = PlayerNetwork.Local;
            if (local != localPlayer)
            {
                localPlayer = local;
                localDiver = local != null ? local.GetComponent<DiverController>() : null;
            }
            underwater = localDiver != null && localDiver.IsDiving;
            float cutoff = underwater ? UnderwaterCutoff : DryCutoff;
            float dt = Time.unscaledDeltaTime;

            foreach (var v in voices)
            {
                if (v.Spatial) v.LowPass.cutoffFrequency = cutoff;
                var loop = v.Loop;
                if (loop == null) continue;
                if (loop.Stopping)
                {
                    loop.Fade = loop.FadeSeconds > 0f ? loop.Fade - dt / loop.FadeSeconds : 0f;
                    if (loop.Fade <= 0f)
                    {
                        v.Source.Stop();
                        v.Loop = null;
                        loop.HasVoice = false;
                        continue;
                    }
                }
                v.Source.volume = Mathf.Clamp01(loop.Volume) * loop.Fade * gain;
                v.Source.pitch = loop.Pitch;
            }
        }
    }

    /// <summary>A looping effect started by <see cref="SfxPlayer.Loop"/>.</summary>
    public class SfxLoop
    {
        public float Volume { get; set; }
        public float Pitch { get; set; } = 1f;
        /// <summary>False once it has faded out (or if no voice was free to play it).</summary>
        public bool IsPlaying => HasVoice;

        internal bool HasVoice, Stopping;
        internal float Fade = 1f, FadeSeconds;

        internal SfxLoop(float volume) => Volume = volume;

        /// <summary>Fades out over <paramref name="fadeSeconds"/> (0 cuts it now), then frees the voice.</summary>
        public void Stop(float fadeSeconds = 0f)
        {
            fadeSeconds = Mathf.Max(0f, fadeSeconds);
            if (Stopping && fadeSeconds >= FadeSeconds) return;
            Stopping = true;
            FadeSeconds = fadeSeconds;
        }
    }
}
