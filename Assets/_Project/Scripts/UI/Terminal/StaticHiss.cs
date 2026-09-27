using System.Threading;
using UnityEngine;

namespace TheDeep.UI.Terminal
{
    /// <summary>
    /// The monitor speaker for the terminal's video apps, synthesised on the audio thread: thin
    /// white-noise hiss at a smoothed <see cref="Level"/> (with a mains buzz when the picture is
    /// nearly gone), relay clicks and a flatline tone. Give it a GameObject of its own: an audio
    /// filter must not share one with another AudioSource.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class StaticHiss : MonoBehaviour
    {
        const float HighPass = 0.85f;     // one-pole high-pass: keeps the hiss thin, like a small speaker
        const float Smoothing = 0.002f;   // per sample, so level changes never click
        const float Gain = 0.25f;
        const float BuzzFrom = 0.8f, BuzzGain = 0.3f, BuzzHz = 60f;
        const float ClickSeconds = 0.03f, ClickCutoff = 1000f, ClickGain = 1.2f;
        const float ToneHz = 1000f, ToneGain = 0.1f; // -20 dB
        const float ToneFade = 0.005f;

        readonly System.Random rng = new(); // UnityEngine.Random isn't allowed on the audio thread
        int sampleRate, clickSamples;
        float lowPassK;
        volatile float target;
        // Set by Click/Flatline on the main thread, counted down on the audio thread.
        int clickLeft, toneLeft;
        // Audio thread only.
        float level, hpIn, hpOut, lowPass, toneLevel;
        double buzzPhase, tonePhase;

        /// <summary>How much static to hiss, 0-1. Fine to set every frame: the sound eases towards it.</summary>
        public float Level { set => target = Mathf.Clamp01(value); }

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            clickSamples = Mathf.Max(1, Mathf.RoundToInt(ClickSeconds * sampleRate));
            lowPassK = 1f - Mathf.Exp(-2f * Mathf.PI * ClickCutoff / sampleRate);
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.7f;
            source.maxDistance = 6f;
            source.dopplerLevel = 0f;
            if (!source.isPlaying) source.Play();
        }

        /// <summary>A relay clicking over: a few milliseconds of dull noise.</summary>
        public void Click() => Interlocked.Exchange(ref clickLeft, clickSamples);

        /// <summary>A steady 1 kHz tone for <paramref name="seconds"/>, like a monitor losing its patient. 0 stops it.</summary>
        public void Flatline(float seconds) => Interlocked.Exchange(ref toneLeft, Mathf.Max(0, Mathf.RoundToInt(seconds * sampleRate)));

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        void OnAudioFilterRead(float[] data, int channels)
        {
            float goal = target;
            int clicks = Volatile.Read(ref clickLeft), tone = Volatile.Read(ref toneLeft);
            if (goal <= 0f && level < 0.0001f && clicks <= 0 && tone <= 0 && toneLevel < 0.0001f)
            {
                level = toneLevel = 0f;
                System.Array.Clear(data, 0, data.Length);
                return;
            }

            double buzzStep = BuzzHz / (double)sampleRate, toneStep = ToneHz / (double)sampleRate;
            int clicked = 0, toned = 0;
            for (int i = 0; i < data.Length; i += channels)
            {
                float white = Noise();
                hpOut = HighPass * (hpOut + white - hpIn);
                hpIn = white;
                level += (goal - level) * Smoothing;
                float s = hpOut * level;
                if (level > BuzzFrom)
                {
                    // Mains buzz when the picture is gone: a clipped 60 Hz sine, so its harmonics are audible.
                    float buzz = Mathf.Clamp(Mathf.Sin((float)buzzPhase * 2f * Mathf.PI) * 3f, -1f, 1f);
                    s += buzz * BuzzGain * (level - BuzzFrom) / (1f - BuzzFrom);
                }
                buzzPhase += buzzStep;
                if (buzzPhase >= 1.0) buzzPhase -= 1.0;
                s *= Gain;

                if (clicked < clicks)
                {
                    lowPass += (white - lowPass) * lowPassK;
                    float env = (float)(clicks - clicked) / clickSamples;
                    s += lowPass * env * env * ClickGain;
                    clicked++;
                }

                bool toneOn = toned < tone;
                if (toneOn) toned++;
                toneLevel += ((toneOn ? 1f : 0f) - toneLevel) * ToneFade;
                if (toneLevel > 0.0001f) s += Mathf.Sin((float)tonePhase * 2f * Mathf.PI) * ToneGain * toneLevel;
                tonePhase += toneStep;
                if (tonePhase >= 1.0) tonePhase -= 1.0;

                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
            // If the main thread queued something new meanwhile, keep its value instead.
            if (clicked > 0) Interlocked.CompareExchange(ref clickLeft, clicks - clicked, clicks);
            if (toned > 0) Interlocked.CompareExchange(ref toneLeft, tone - toned, tone);
        }
    }
}
