using TheDeep.Voice;
using UnityEngine;

namespace TheDeep.Footage
{
    /// <summary>
    /// The last recordings of Dive Team 7, generated the same way on every machine. Each diver
    /// drifts across the shaft floor toward the glyph stones, then something enormous comes out of the
    /// dark above them. The camera ends where the body lies. The sound is the suit itself: breathing,
    /// bubbles, the lamp, the impact.
    /// </summary>
    public static class LostDiverFootage
    {
        public const int Count = 4;

        /// <summary>Chip id of a Team 7 diver's camera (always negative: -1..-4).</summary>
        public static int ChipId(int diverIndex) => -(diverIndex + 1);
        public static int DiverIndex(int chipId) => -chipId - 1;
        public static bool IsLostDiverChip(int chipId) => chipId < 0 && chipId >= -Count;
        public static string Title(int diverIndex) => $"TEAM 7 / DIVER {diverIndex + 1} / HELMET CAM";

        static readonly Vector3 Bell = new(-12f, -1199f, 8f);
        static readonly Vector3 Stones = new(14f, -1199f, -6f);

        public static FootageClip Create(int diverIndex, Vector3 bodyPosition)
        {
            var rng = new System.Random(700 + diverIndex);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var clip = new FootageClip
            {
                Title = Title(diverIndex),
                Timestamp = $"2019-03-{14 + diverIndex / 2:00}  04:{12 + diverIndex * 7:00}",
                Diver = 700 + diverIndex + 1,
                EndsInDeath = true,
                Corrupted = true,
            };

            const float dt = 0.1f;
            float cruise = 22f + diverIndex * 3f;   // calm swim toward the stones
            float encounter = 7f;                     // the thing passes overhead
            float fall = 5f;                          // tumbling down
            float total = cruise + encounter + fall;

            Vector3 start = Bell + new Vector3(R(-2f, 2f), 7f + R(0f, 3f), R(-3f, 3f));
            Vector3 stoneView = Vector3.Lerp(Bell, Stones, 0.55f + diverIndex * 0.1f) + new Vector3(0f, 6f, R(-4f, 4f));
            Vector3 creatureFrom = stoneView + new Vector3(R(-30f, -20f), 22f, R(-18f, 18f));
            Vector3 creatureTo = stoneView + new Vector3(R(20f, 30f), 4f, R(-10f, 10f));

            for (float t = 0f; t <= total; t += dt)
            {
                var f = new FootageFrame { Time = t, Lamp = true };
                if (t < cruise)
                {
                    float k = t / cruise;
                    f.Position = Vector3.Lerp(start, stoneView, Mathf.SmoothStep(0f, 1f, k)) + Vector3.up * Mathf.Sin(t * 0.7f) * 0.3f;
                    // Looking ahead, glancing around, and increasingly down at the stones.
                    float yaw = 90f + Mathf.Sin(t * 0.35f + diverIndex) * 35f;
                    float pitch = 15f + k * 20f + Mathf.Sin(t * 0.5f) * 8f;
                    f.Rotation = Quaternion.Euler(pitch, yaw, 0f);
                }
                else if (t < cruise + encounter)
                {
                    float k = (t - cruise) / encounter;
                    f.Position = stoneView + Random3(rng) * 0.08f * k;
                    // Something moves overhead: the camera whips up to follow it.
                    Vector3 creature = Vector3.Lerp(creatureFrom, creatureTo, k);
                    f.Proxy = true;
                    f.ProxyPosition = creature;
                    f.ProxyRotation = Quaternion.LookRotation(creatureTo - creatureFrom);
                    Quaternion look = Quaternion.LookRotation(creature - f.Position);
                    f.Rotation = Quaternion.Slerp(Quaternion.Euler(30f, 90f, 0f), look, Mathf.SmoothStep(0f, 1f, k * 2.5f))
                                 * Quaternion.Euler(Random3(rng) * (2f + k * 10f));
                    f.Lamp = !(k > 0.6f && rng.NextDouble() < 0.35);
                }
                else
                {
                    float k = (t - cruise - encounter) / fall;
                    // Grabbed / struck: tumbling down onto the shaft floor where the body lies now.
                    f.Position = Vector3.Lerp(stoneView, bodyPosition + Vector3.up * 0.5f, k * k);
                    f.Rotation = Quaternion.Euler(40f + k * 60f + Mathf.Sin(t * 9f) * 20f, 90f + k * 540f, Mathf.Sin(t * 6f) * 40f);
                    f.Lamp = rng.NextDouble() < 0.5 - k * 0.4;
                }
                clip.Frames.Add(f);
            }
            AddSound(clip, diverIndex, cruise, encounter);
            return clip;
        }

        static Vector3 Random3(System.Random rng) =>
            new((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

        // ------------------------------------------------------------------ sound

        const int Rate = VoiceCodec.SampleRate;
        const int EntrySamples = Rate / 4;   // 0.25 s per FootageAudio entry

        /// <summary>
        /// The suit's own sounds, on the Helmet channel: slow regulator breaths on the swim, fast loud ones
        /// when the thing passes, a buzz each time the lamp cuts in or out, then the hit, the visor cracking
        /// and a rush of bubbles on the way down. It has its own seed, so the sound never changes the
        /// picture, and uses only plain arithmetic, so every machine makes the same bytes.
        /// Everything stays below about 3.5 kHz (the track is 8 kHz).
        /// </summary>
        static void AddSound(FootageClip clip, int diverIndex, float cruise, float encounter)
        {
            var rng = new System.Random(900 + diverIndex);
            var track = new float[Mathf.CeilToInt(clip.Duration * Rate) + 1];

            // The swim: a breath every ~4 s, bubbles out of the exhaust on each exhale.
            for (float t = Range(rng, 0.4f, 1.2f); t < cruise - 2.5f; t += Range(rng, 3.6f, 4.4f))
                Breath(track, rng, t, Range(rng, 0.8f, 1f), 10, 1.3f, 0.8f);
            // The thing overhead: panting, louder.
            for (float t = cruise + 0.15f; t < cruise + encounter - 0.6f; t += 1.2f)
                Breath(track, rng, t, 0.45f, 5, 0.5f, 1.2f);
            // The lamp's power buzzing on every frame where it cuts in or out.
            for (int i = 1; i < clip.Frames.Count; i++)
                if (clip.Frames[i].Lamp != clip.Frames[i - 1].Lamp) Buzz(track, clip.Frames[i].Time, 0.06f, 0.16f);
            // The fall: struck, the visor cracks, the air pours out.
            float fall = cruise + encounter;
            Impact(track, rng, fall);
            Crack(track, rng, fall + 0.03f);
            Torrent(track, rng, fall + 0.05f, clip.Duration);

            var lowA = Biquad.LowPass(3200f, 0.707f);
            var lowB = Biquad.LowPass(3200f, 0.707f);
            for (int i = 0; i < track.Length; i++) track[i] = SoftClip(lowB.Process(lowA.Process(track[i])));

            for (int start = 0; start < track.Length; start += EntrySamples)
            {
                var bytes = new byte[Mathf.Min(EntrySamples, track.Length - start)];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = VoiceCodec.Encode(track[start + i]);
                clip.Audio.Add(new FootageAudio { Offset = start, Channel = FootageAudio.Helmet, Signal = 255, Samples = bytes });
            }
        }

        /// <summary>Air hissing in through the regulator, then a soft rumble and a string of bubbles out.</summary>
        static void Breath(float[] track, System.Random rng, float at, float inhale, int bubbles, float exhale, float gain)
        {
            var hiss = Biquad.BandPass(1300f, 1.1f);
            var body = Biquad.BandPass(500f, 0.9f);
            int start = SampleAt(at), n = SampleAt(inhale);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float env = u < 0.3f ? Smooth(u / 0.3f) : Smooth(1f - (u - 0.3f) / 0.7f);
                float x = Noise(rng);
                Add(track, start + i, (hiss.Process(x) * 0.35f + body.Process(x) * 0.2f) * env * gain);
            }

            float out0 = at + inhale + 0.15f;
            var rumble = Biquad.BandPass(280f, 0.8f);
            start = SampleAt(out0);
            n = SampleAt(exhale);
            for (int i = 0; i < n; i++)
                Add(track, start + i, rumble.Process(Noise(rng)) * 0.25f * (float)Sine(i / (float)n * 0.5f) * gain);
            for (int b = 0; b < bubbles; b++)
            {
                float t = out0 + (b + (float)rng.NextDouble() * 0.8f) * exhale / bubbles;
                Bubble(track, t, Range(rng, 350f, 900f), Range(rng, 0.025f, 0.06f), Range(rng, 0.1f, 0.18f) * gain);
            }
        }

        /// <summary>One bubble: a short tone that rises in pitch and dies away fast.</summary>
        static void Bubble(float[] track, float at, float freq, float seconds, float amp)
        {
            int start = SampleAt(at), n = SampleAt(seconds);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                phase += freq * (1f + 0.6f * u) / Rate;
                float env = Mathf.Min(1f, i / 16f) * (1f - u) * (1f - u);
                Add(track, start + i, (float)Sine(phase) * env * amp);
            }
        }

        /// <summary>Electrical buzz: a 100 Hz square wave built only from harmonics below 3.3 kHz.</summary>
        static void Buzz(float[] track, float at, float seconds, float amp)
        {
            int start = SampleAt(at), n = SampleAt(seconds);
            for (int i = 0; i < n; i++)
            {
                double cycles = 100.0 * i / Rate;
                double x = 0.0;
                for (int h = 1; h * 100 <= 3300; h += 2) x += Sine(cycles * h) / h;
                float env = Mathf.Min(1f, Mathf.Min(i, n - i) / 40f); // 5 ms fades, no clicks
                Add(track, start + i, (float)x * env * amp);
            }
        }

        /// <summary>The hit: a knock, a low thud of noise under 150 Hz and a 60 Hz thump, over 0.4 s.</summary>
        static void Impact(float[] track, System.Random rng, float at)
        {
            var thud = Biquad.LowPass(150f, 0.8f);
            var knock = Biquad.LowPass(900f, 0.7f);
            int start = SampleAt(at), n = SampleAt(0.4f);
            float thudEnv = 1f, thumpEnv = 1f, knockEnv = 1f;
            float thudStep = DecayStep(0.12f), thumpStep = DecayStep(0.15f), knockStep = DecayStep(0.015f);
            for (int i = 0; i < n; i++)
            {
                float x = Noise(rng);
                float v = thud.Process(x) * 2.4f * thudEnv + (float)Sine(60.0 * i / Rate) * 0.4f * thumpEnv + knock.Process(x) * 0.7f * knockEnv;
                Add(track, start + i, v * Mathf.Min(1f, i / 8f));
                thudEnv *= thudStep;
                thumpEnv *= thumpStep;
                knockEnv *= knockStep;
            }
        }

        /// <summary>The visor cracking: a few sharp ticks of noise around 2.4 kHz.</summary>
        static void Crack(float[] track, System.Random rng, float at)
        {
            var band = Biquad.BandPass(2400f, 1.6f);
            float step = DecayStep(0.004f);
            for (int c = 0; c < 4; c++)
            {
                int start = SampleAt(at), n = SampleAt(Range(rng, 0.008f, 0.018f));
                float env = c == 0 ? 0.9f : 0.5f;
                for (int i = 0; i < n; i++)
                {
                    Add(track, start + i, band.Process(Noise(rng)) * env);
                    env *= step;
                }
                at += Range(rng, 0.012f, 0.03f);
            }
        }

        /// <summary>Air pouring out of the suit while it tumbles: dense bubbles over a rush that swells with each roll.</summary>
        static void Torrent(float[] track, System.Random rng, float from, float to)
        {
            for (float t = from; t < to; t += Range(rng, 0.005f, 0.035f))
                Bubble(track, t, Range(rng, 250f, 1300f), Range(rng, 0.012f, 0.045f), Range(rng, 0.05f, 0.13f));
            var rush = Biquad.LowPass(400f, 0.7f);
            int start = SampleAt(from), end = Mathf.Min(SampleAt(to), track.Length);
            for (int i = start; i < end; i++)
            {
                // Same roll as the picture: Mathf.Sin(t * 6f) in the frames above.
                double roll = System.Math.Abs(Sine(i * 6.0 / Rate / (2.0 * System.Math.PI)));
                float fade = Mathf.Min(1f, (i - start) / (0.25f * Rate));
                Add(track, i, rush.Process(Noise(rng)) * 0.5f * (0.35f + 0.65f * (float)roll) * fade);
            }
        }

        static int SampleAt(float seconds) => Mathf.RoundToInt(seconds * Rate);

        static void Add(float[] track, int i, float value)
        {
            if (i >= 0 && i < track.Length) track[i] += value;
        }

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>Per-sample factor for a decay that falls to about 37% in <paramref name="seconds"/>.</summary>
        static float DecayStep(float seconds) => 1f - 1f / (seconds * Rate);

        /// <summary>Rounds peaks off above 0.8 instead of clipping (approaches 1).</summary>
        static float SoftClip(float x)
        {
            float m = Mathf.Abs(x);
            if (m <= 0.8f) return x;
            float over = m - 0.8f;
            return Mathf.Sign(x) * (0.8f + 0.2f * over / (0.2f + over));
        }

        /// <summary>sin(2π · <paramref name="cycles"/>) from a polynomial, so it is bit-identical on every machine.</summary>
        static double Sine(double cycles)
        {
            double p = cycles - System.Math.Floor(cycles + 0.5);
            if (p > 0.25) p = 0.5 - p;
            else if (p < -0.25) p = -0.5 - p;
            double x = p * 2.0 * System.Math.PI, x2 = x * x;
            return x * (1.0 - x2 / 6.0 * (1.0 - x2 / 20.0 * (1.0 - x2 / 42.0 * (1.0 - x2 / 72.0))));
        }

        /// <summary>A two-pole filter (RBJ cookbook) at the footage's 8 kHz.</summary>
        sealed class Biquad
        {
            readonly double b0, b1, b2, a1, a2;
            double x1, x2, y1, y2;

            Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                this.b0 = b0 / a0;
                this.b1 = b1 / a0;
                this.b2 = b2 / a0;
                this.a1 = a1 / a0;
                this.a2 = a2 / a0;
            }

            public static Biquad LowPass(float freq, float q)
            {
                double cos = Sine(freq / (double)Rate + 0.25), alpha = Sine(freq / (double)Rate) / (2.0 * q);
                return new Biquad((1.0 - cos) / 2.0, 1.0 - cos, (1.0 - cos) / 2.0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
            }

            /// <summary>Band-pass with 0 dB at <paramref name="freq"/>.</summary>
            public static Biquad BandPass(float freq, float q)
            {
                double cos = Sine(freq / (double)Rate + 0.25), alpha = Sine(freq / (double)Rate) / (2.0 * q);
                return new Biquad(alpha, 0.0, -alpha, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
            }

            public float Process(float x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
                return (float)y;
            }
        }
    }
}
