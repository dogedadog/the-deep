using TheDeep.Player;
using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// Placeholder interior soundscape, synthesised in code (no audio files yet):
    /// a low machinery hum, random deep hull creaks/groans, and occasional water drips.
    /// Out in the water the drips stop and the hum and creaks turn quiet and muffled, heard
    /// through the hull (<see cref="OceanAmbience"/> takes over). Swap for real recorded sounds in the audio pass.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SubAmbience : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [SerializeField, Range(0f, 1f)] float humLevel = 0.35f;
        [SerializeField, Range(0f, 1f)] float creakLevel = 0.8f;
        [SerializeField, Range(0f, 1f)] float dripLevel = 0.25f;

        const float TwoPi = 2f * Mathf.PI;
        // Slow wobble of the 150 Hz overtone and swell of the whole hum, in cycles per second.
        const double WobbleHz = 0.3 / (2.0 * System.Math.PI);
        const double SwellHz = 0.21 / (2.0 * System.Math.PI);

        readonly System.Random rng = new(1234); // UnityEngine.Random isn't allowed on the audio thread
        int sampleRate;
        double time; // only for scheduling: as a float it would lose precision within the hour

        // Written on the main thread (Update), read on the audio thread.
        volatile float interiorTarget = 1f;
        volatile bool travelling;
        PlayerNetwork localPlayer;
        DiverController localDiver;

        // 1 in the cabin, 0 out in the water.
        float interiorGain = 1f;
        // Hum: wrapped phases (0..1), so the sines stay clean however long the session runs
        double humPhase, wobblePhase, swellPhase;
        float brown, humMuffled;
        // Creak: resonant filter swept over a noise burst
        double creakStart = 4, creakEnd;
        float creakFreqFrom, creakFreqTo;
        float bpLow, bpBand, creakMuffled;
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

        void Update()
        {
            // Re-fetch when the local player changes (join, leave, rehost).
            var local = PlayerNetwork.Local;
            if (local != localPlayer)
            {
                localPlayer = local;
                localDiver = local != null ? local.GetComponent<DiverController>() : null;
            }
            // Dead divers stay 'diving' until the expedition ends, so spectators hear the ocean too.
            interiorTarget = localDiver != null && localDiver.IsDiving ? 0f : 1f;
            var nav = SubNavigation.Instance;
            travelling = nav != null && nav.Travelling;
        }

        float Rand() => (float)rng.NextDouble();

        void OnAudioFilterRead(float[] data, int channels)
        {
            double dt = 1.0 / sampleRate;
            float target = interiorTarget;
            bool inTransit = travelling;
            for (int i = 0; i < data.Length; i += channels)
            {
                time += dt;
                interiorGain += (target - interiorGain) * 0.00003f; // about 0.7 s
                float outside = 1f - interiorGain;

                // --- hum: mains-style buzz + rumbling brown noise
                humPhase += 50.0 * dt;
                if (humPhase >= 1.0) humPhase -= 1.0;
                wobblePhase += WobbleHz * dt;
                if (wobblePhase >= 1.0) wobblePhase -= 1.0;
                swellPhase += SwellHz * dt;
                if (swellPhase >= 1.0) swellPhase -= 1.0;
                brown = Mathf.Clamp(brown + (Rand() * 2f - 1f) * 0.02f, -1f, 1f) * 0.998f;
                float hum = 0.05f * Mathf.Sin(TwoPi * (float)humPhase)
                            + 0.025f * Mathf.Sin(TwoPi * (float)((2.0 * humPhase) % 1.0))
                            + 0.012f * Mathf.Sin(TwoPi * (float)((3.0 * humPhase) % 1.0) + 0.5f * Mathf.Sin(TwoPi * (float)wobblePhase))
                            + 0.25f * brown;
                hum *= humLevel * (0.85f + 0.15f * Mathf.Sin(TwoPi * (float)swellPhase));
                // From outside it comes through the hull: quieter and muffled.
                humMuffled += (hum - humMuffled) * 0.05f;
                hum = Mathf.Lerp(hum, humMuffled, outside) * Mathf.Lerp(1f, 0.25f, outside);

                // --- creak
                if (creakEnd < creakStart) // waiting for the next one
                {
                    // In transit the hull groans every few seconds.
                    if (inTransit && creakStart - time > 3.0) creakStart = time + 1.0 + Rand() * 2.0;
                    if (time > creakStart)
                    {
                        creakStart = time;
                        creakEnd = time + 0.8 + Rand() * 1.8;
                        creakFreqFrom = 60f + Rand() * 120f;
                        creakFreqTo = creakFreqFrom * (0.6f + Rand() * 0.9f);
                    }
                }
                float creak = 0f;
                if (time < creakEnd)
                {
                    float duration = (float)(creakEnd - creakStart);
                    float p = Mathf.Clamp01((float)(time - creakStart) / Mathf.Max(duration, 0.01f));
                    float env = Mathf.Sin(p * Mathf.PI) * (0.6f + 0.4f * Mathf.PerlinNoise((float)(time % 512.0) * 25f, 0f));
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
                else if (creakStart < creakEnd) // just finished
                {
                    creakStart = time + (inTransit ? 1 + Rand() * 2 : 6 + Rand() * 12); // next groan in 6-18 s (1-3 s in transit)
                }
                // From outside: distant hull groans through the water.
                creakMuffled += (creak - creakMuffled) * 0.05f;
                creak = Mathf.Lerp(creak, creakMuffled, outside) * Mathf.Lerp(1f, 0.6f, outside);

                // --- drip (cabin only)
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
                    drip = Mathf.Sin(dripPhase) * Mathf.Exp(-p * 5f) * 0.3f * dripLevel * interiorGain;
                }

                float sample = (hum + creak + drip) * volume;
                for (int c = 0; c < channels; c++) data[i + c] = sample;
            }
        }
    }
}
