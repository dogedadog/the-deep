using System;
using UnityEngine;

namespace TheDeep.Audio
{
    /// <summary>
    /// A tiny offline synthesiser for the placeholder sound effects (no audio files yet, like
    /// SubAmbience). Everything renders into mono float buffers at 22050 Hz on the main thread and
    /// then becomes an AudioClip. Noise comes from a seeded System.Random, so every machine renders
    /// the same sounds.
    /// </summary>
    public static class Synth
    {
        public const int Rate = 22050;
        const float TwoPi = Mathf.PI * 2f;

        public static float[] Buffer(float seconds) => new float[Mathf.Max(1, Mathf.CeilToInt(seconds * Rate))];
        public static int Index(float seconds) => Mathf.RoundToInt(seconds * Rate);
        public static float Seconds(int sample) => sample / (float)Rate;

        public static float Noise(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;
        public static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ------------------------------------------------------------------ oscillators (phase in cycles)

        public static float Sine(double cycles) => Mathf.Sin(TwoPi * (float)(cycles - Math.Floor(cycles)));
        public static float Square(double cycles) => cycles - Math.Floor(cycles) < 0.5 ? 1f : -1f;
        public static float Saw(double cycles) => (float)(2.0 * (cycles - Math.Floor(cycles)) - 1.0);

        // ------------------------------------------------------------------ filters

        /// <summary>One-pole low-pass (6 dB per octave).</summary>
        public struct LowPass
        {
            float y;

            public float Process(float x, float cutoff)
            {
                y += (1f - Mathf.Exp(-TwoPi * cutoff / Rate)) * (x - y);
                return y;
            }
        }

        /// <summary>One-pole high-pass (6 dB per octave).</summary>
        public struct HighPass
        {
            LowPass low;

            public float Process(float x, float cutoff) => x - low.Process(x, cutoff);
        }

        /// <summary>
        /// State-variable band-pass, the same maths as SubAmbience's creaks. <c>q</c> is the damping:
        /// smaller rings longer (0.04 groans like metal, 1 is a broad band).
        /// </summary>
        public struct BandPass
        {
            float low, band;

            public float Process(float x, float freq, float q)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(freq, Rate * 0.2f) / Rate);
                low += f * band;
                float high = x - low - q * band;
                band += f * high;
                return band;
            }
        }

        public static void LowPassAll(float[] buf, float cutoff, int poles = 1)
        {
            for (int p = 0; p < poles; p++)
            {
                var lp = new LowPass();
                for (int i = 0; i < buf.Length; i++) buf[i] = lp.Process(buf[i], cutoff);
            }
        }

        public static void HighPassAll(float[] buf, float cutoff, int poles = 1)
        {
            for (int p = 0; p < poles; p++)
            {
                var hp = new HighPass();
                for (int i = 0; i < buf.Length; i++) buf[i] = hp.Process(buf[i], cutoff);
            }
        }

        /// <summary>A small helmet or radio speaker: cuts the lows and the highs.</summary>
        public static void Speaker(float[] buf, float low = 400f, float high = 3000f)
        {
            HighPassAll(buf, low, 2);
            LowPassAll(buf, high, 2);
        }

        /// <summary>A copy of <paramref name="src"/> through a band-pass, normalised to a peak of 1.</summary>
        public static float[] BandPassed(float[] src, float freq, float q)
        {
            var bp = new BandPass();
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = bp.Process(src[i], freq, q);
            return Normalize(dst, 1f);
        }

        // ------------------------------------------------------------------ envelopes

        /// <summary>Linear attack to 1, then exponential decay with time constant <paramref name="tau"/> (seconds).</summary>
        public static float AttackDecay(float t, float attack, float tau) =>
            t < attack ? t / Mathf.Max(attack, 1e-5f) : Mathf.Exp(-(t - attack) / tau);

        /// <summary>1 inside a note of <paramref name="length"/> seconds, with linear ramps at both ends (no clicks).</summary>
        public static float Gate(float t, float length, float attack, float release) =>
            Mathf.Clamp01(Mathf.Min(t / Mathf.Max(attack, 1e-5f), (length - t) / Mathf.Max(release, 1e-5f)));

        // ------------------------------------------------------------------ generators (all add into the buffer)

        /// <summary>A square-wave note with soft edges.</summary>
        public static void AddSquare(float[] buf, float start, float freq, float length, float amp)
        {
            int from = Index(start), n = Index(length);
            float attack = Mathf.Min(0.002f, length * 0.2f), release = Mathf.Min(0.004f, length * 0.3f);
            for (int i = 0; i < n && from + i < buf.Length; i++)
            {
                float t = Seconds(i);
                buf[from + i] += Square((double)freq * i / Rate) * amp * Gate(t, length, attack, release);
            }
        }

        /// <summary>A sine that glides exponentially from <paramref name="f0"/> to <paramref name="f1"/> and decays (time constant <paramref name="tau"/>).</summary>
        public static void AddChirp(float[] buf, float start, float f0, float f1, float length, float amp, float tau)
        {
            int from = Index(start), n = Index(length);
            double phase = 0;
            for (int i = 0; i < n && from + i < buf.Length; i++)
            {
                float t = Seconds(i);
                phase += f0 * Mathf.Pow(f1 / f0, t / length) / Rate;
                buf[from + i] += Sine(phase) * amp * AttackDecay(t, 0.002f, tau) * Gate(t, length, 0.001f, 0.004f);
            }
        }

        /// <summary>A bubble: a short sine blip starting at 250-700 Hz and rising by x1.6-1.8 over about 40 ms.</summary>
        public static void AddBlip(float[] buf, float start, System.Random rng, float amp, float minHz = 250f, float maxHz = 700f)
        {
            float f0 = Range(rng, minHz, maxHz);
            float length = Range(rng, 0.035f, 0.05f);
            AddChirp(buf, start, f0, f0 * Range(rng, 1.6f, 1.8f), length, amp, length * 0.45f);
        }

        /// <summary>A modem burst: random bits as frequency-shift keying (300 baud, 1200/2200 Hz), phase-continuous.</summary>
        public static void AddFsk(float[] buf, float start, float length, float amp, System.Random rng,
            float baud = 300f, float low = 1200f, float high = 2200f)
        {
            int from = Index(start), n = Index(length);
            int perBit = Mathf.Max(1, Mathf.RoundToInt(Rate / baud));
            float freq = low;
            double phase = 0;
            for (int i = 0; i < n && from + i < buf.Length; i++)
            {
                if (i % perBit == 0) freq = rng.Next(2) == 0 ? low : high;
                phase += freq / (double)Rate;
                buf[from + i] += Sine(phase) * amp * Gate(Seconds(i), length, 0.005f, 0.01f);
            }
        }

        /// <summary>Adds <paramref name="src"/> into <paramref name="dst"/> at <paramref name="start"/> seconds.</summary>
        public static void Mix(float[] dst, float[] src, float gain, float start = 0f)
        {
            int from = Index(start);
            for (int i = 0; i < src.Length && from + i < dst.Length; i++) dst[from + i] += src[i] * gain;
        }

        /// <summary>Zeroes a stretch (radio dropouts).</summary>
        public static void Silence(float[] buf, float start, float length)
        {
            int from = Index(start), to = Mathf.Min(buf.Length, from + Index(length));
            for (int i = Mathf.Max(0, from); i < to; i++) buf[i] = 0f;
        }

        public static void FadeOut(float[] buf, float seconds)
        {
            int n = Mathf.Min(buf.Length, Index(seconds));
            for (int i = 0; i < n; i++) buf[buf.Length - 1 - i] *= i / (float)n;
        }

        /// <summary>Scales the buffer so its loudest sample is <paramref name="peak"/>.</summary>
        public static float[] Normalize(float[] buf, float peak)
        {
            float max = 0f;
            for (int i = 0; i < buf.Length; i++) max = Mathf.Max(max, Mathf.Abs(buf[i]));
            if (max < 1e-6f) return buf;
            float k = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
            return buf;
        }

        public static AudioClip ToClip(float[] buf, string name)
        {
            var clip = AudioClip.Create(name, buf.Length, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }
    }
}
