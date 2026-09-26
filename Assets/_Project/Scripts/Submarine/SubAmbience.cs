using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// Placeholder interior soundscape, synthesised in code (no audio files yet):
    /// a low machinery hum, random deep hull creaks/groans, and occasional water drips.
    /// Swap for real recorded sounds in the audio pass.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SubAmbience : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [SerializeField, Range(0f, 1f)] float humLevel = 0.35f;
        [SerializeField, Range(0f, 1f)] float creakLevel = 0.8f;
        [SerializeField, Range(0f, 1f)] float dripLevel = 0.25f;

        readonly System.Random rng = new(1234); // UnityEngine.Random isn't allowed on the audio thread
        int sampleRate;
        double time;

        // Hum
        float brown;
        // Creak: resonant filter swept over a noise burst
        double creakStart = 4, creakEnd;
        float creakFreqFrom, creakFreqTo;
        float bpLow, bpBand;
        // Drip: short falling "plink"
        double dripStart = 2, dripEnd;
        float dripPhase;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            if (!source.isPlaying) source.Play();
        }

        float Rand() => (float)rng.NextDouble();

        void OnAudioFilterRead(float[] data, int channels)
        {
            double dt = 1.0 / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                time += dt;
                float t = (float)time;

                // --- hum: mains-style buzz + rumbling brown noise
                brown = Mathf.Clamp(brown + (Rand() * 2f - 1f) * 0.02f, -1f, 1f) * 0.998f;
                float hum = 0.05f * Mathf.Sin(2f * Mathf.PI * 50f * t)
                            + 0.025f * Mathf.Sin(2f * Mathf.PI * 100f * t)
                            + 0.012f * Mathf.Sin(2f * Mathf.PI * 150f * t + 0.5f * Mathf.Sin(t * 0.3f))
                            + 0.25f * brown;
                hum *= humLevel * (0.85f + 0.15f * Mathf.Sin(t * 0.21f));

                // --- creak
                if (time > creakEnd && time > creakStart)
                {
                    creakEnd = time + 0.8 + Rand() * 1.8;
                    creakFreqFrom = 60f + Rand() * 120f;
                    creakFreqTo = creakFreqFrom * (0.6f + Rand() * 0.9f);
                }
                float creak = 0f;
                if (time < creakEnd)
                {
                    float duration = (float)(creakEnd - creakStart);
                    float p = Mathf.Clamp01((float)(time - creakStart) / Mathf.Max(duration, 0.01f));
                    float env = Mathf.Sin(p * Mathf.PI) * (0.6f + 0.4f * Mathf.PerlinNoise(t * 25f, 0f));
                    float freq = Mathf.Lerp(creakFreqFrom, creakFreqTo, p);
                    // State-variable band-pass with high resonance gives the "groaning metal" tone.
                    float f = 2f * Mathf.Sin(Mathf.PI * freq / sampleRate);
                    const float q = 0.04f;
                    float input = Rand() * 2f - 1f;
                    bpLow += f * bpBand;
                    float high = input - bpLow - q * bpBand;
                    bpBand += f * high;
                    creak = Mathf.Clamp(bpBand * 0.35f, -1f, 1f) * env * creakLevel;
                }
                else if (time >= creakEnd && creakStart < creakEnd)
                {
                    creakStart = time + 6 + Rand() * 12; // next groan in 6-18 s
                }

                // --- drip
                if (time > dripStart && time > dripEnd)
                {
                    dripEnd = time + 0.09;
                    dripPhase = 0f;
                    dripStart = time + 1.5 + Rand() * 4;
                }
                float drip = 0f;
                if (time < dripEnd)
                {
                    float p = 1f - (float)((dripEnd - time) / 0.09);
                    float freq = Mathf.Lerp(1800f, 700f, p);
                    dripPhase += 2f * Mathf.PI * freq / sampleRate;
                    drip = Mathf.Sin(dripPhase) * Mathf.Exp(-p * 5f) * 0.3f * dripLevel;
                }

                float sample = (hum + creak + drip) * volume;
                for (int c = 0; c < channels; c++) data[i + c] = sample;
            }
        }
    }
}
