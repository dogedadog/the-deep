using System;
using UnityEngine;

namespace TheDeep.Audio
{
    public enum Sfx
    {
        Click, Bonk, Splash, Drain, Clank, Breath, Exhale, Step, ScanDone, TransmitClean, TransmitBroken,
        ErrorBuzz, AirBeep, Klaxon, Crack, Implosion, BubbleTorrent, NoVitals, DataIn, DataInCorrupt,
        KaChunk, Purchase, Foghorn, BallastFlood, BallastBlow, Motor, SonarPing, ChipInsert,
    }

    /// <summary>
    /// Every sound effect, synthesised on first use (main thread) and cached. Placeholders until the
    /// audio pass: <see cref="Register"/> swaps in a recording without touching any call site.
    /// Peaks stay at or below 0.5; nobody on the team can listen in the editor, so the user auditions.
    /// </summary>
    public static class SfxBank
    {
        const int MaxVariants = 2;
        static readonly int Count = Enum.GetValues(typeof(Sfx)).Length;
        static readonly AudioClip[] rendered = new AudioClip[Count * MaxVariants];
        static readonly AudioClip[] registered = new AudioClip[Count];

        /// <summary>How many versions of a sound exist (steps, exhales and cracks vary a little).</summary>
        public static int Variants(Sfx id) => id == Sfx.Step || id == Sfx.Exhale || id == Sfx.Crack ? 2 : 1;

        public static AudioClip Get(Sfx id, int variant = 0)
        {
            int index = (int)id;
            if (index < 0 || index >= Count) return null;
            if (registered[index] != null) return registered[index];
            int n = Variants(id);
            variant = (variant % n + n) % n;
            int slot = index * MaxVariants + variant;
            if (rendered[slot] == null) rendered[slot] = Render(id, variant);
            return rendered[slot];
        }

        /// <summary>Use a real clip for this sound from now on (null goes back to the synthesised one).</summary>
        public static void Register(Sfx id, AudioClip clip)
        {
            int index = (int)id;
            if (index >= 0 && index < Count) registered[index] = clip;
        }

        static AudioClip Render(Sfx id, int variant)
        {
            var rng = new System.Random(((int)id << 4) | variant);
            float[] buf = id switch
            {
                Sfx.Click => Click(rng),
                Sfx.Bonk => Bonk(),
                Sfx.Splash => Splash(rng),
                Sfx.Drain => Drain(rng),
                Sfx.Clank => Clank(rng),
                Sfx.Breath => Breath(rng),
                Sfx.Exhale => Exhale(rng),
                Sfx.Step => Step(rng),
                Sfx.ScanDone => Notes(0.35f, 3500f, (880f, 0.06f), (1175f, 0.06f), (1568f, 0.06f)),
                Sfx.TransmitClean => Modem(rng, 0.6f, broken: false),
                Sfx.TransmitBroken => Modem(rng, 0.6f, broken: true),
                Sfx.ErrorBuzz => ErrorBuzz(),
                Sfx.AirBeep => AirBeep(),
                Sfx.Klaxon => Notes(0.3f, 2500f, (440f, 0.25f)),
                Sfx.Crack => Crack(rng),
                Sfx.Implosion => Implosion(rng),
                Sfx.BubbleTorrent => BubbleTorrent(rng),
                Sfx.NoVitals => NoVitals(),
                Sfx.DataIn => Modem(rng, 0.4f, broken: false),
                Sfx.DataInCorrupt => Modem(rng, 0.4f, broken: true),
                Sfx.KaChunk => KaChunk(rng),
                Sfx.Purchase => Notes(0.3f, 3000f, (523f, 0.07f), (659f, 0.07f), (784f, 0.07f), (1047f, 0.07f)),
                Sfx.Foghorn => Foghorn(),
                Sfx.BallastFlood => BallastFlood(rng),
                Sfx.BallastBlow => BallastBlow(rng),
                Sfx.Motor => Motor(),
                Sfx.SonarPing => SonarPing(),
                Sfx.ChipInsert => ChipInsert(rng),
                _ => Synth.Buffer(0.01f),
            };
            return Synth.ToClip(buf, variant == 0 ? "Sfx" + id : "Sfx" + id + variant);
        }

        // ------------------------------------------------------------------ recipes

        /// <summary>Square-wave notes back to back, softened by a low-pass.</summary>
        static float[] Notes(float peak, float cutoff, params (float hz, float seconds)[] notes)
        {
            float total = 0f;
            foreach (var n in notes) total += n.seconds;
            var b = Synth.Buffer(total + 0.02f);
            float at = 0f;
            foreach (var n in notes)
            {
                Synth.AddSquare(b, at, n.hz, n.seconds, 1f);
                at += n.seconds;
            }
            Synth.LowPassAll(b, cutoff);
            return Synth.Normalize(b, peak);
        }

        /// <summary>1 ms of 2 kHz square and 8 ms of high-passed noise: a soft relay tick.</summary>
        static float[] Click(System.Random rng)
        {
            var b = Synth.Buffer(0.012f);
            Synth.AddSquare(b, 0f, 2000f, 0.001f, 0.6f);
            var hp = new Synth.HighPass();
            int n = Synth.Index(0.008f);
            for (int i = 0; i < n && i < b.Length; i++)
                b[i] += hp.Process(Synth.Noise(rng), 3000f) * Synth.AttackDecay(Synth.Seconds(i), 0.0003f, 0.002f);
            return Synth.Normalize(b, 0.4f);
        }

        /// <summary>A DOS-style "can't do that": 150 ms of 110 Hz square, decaying.</summary>
        static float[] Bonk()
        {
            var b = Synth.Buffer(0.16f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = Synth.Seconds(i);
                b[i] = Synth.Square(110.0 * i / Synth.Rate) * Synth.AttackDecay(t, 0.003f, 0.05f) * Synth.Gate(t, 0.16f, 0.001f, 0.02f);
            }
            Synth.LowPassAll(b, 1200f, 2);
            return Synth.Normalize(b, 0.45f);
        }

        /// <summary>Into the water: noise with a low-pass sweeping 4 kHz down to 300 Hz, and bubbles.</summary>
        static float[] Splash(System.Random rng)
        {
            var b = Synth.Buffer(1f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            int n = Synth.Index(0.9f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                float cutoff = 4000f * Mathf.Pow(300f / 4000f, t / 0.9f);
                float x = lp2.Process(lp1.Process(Synth.Noise(rng), cutoff), cutoff);
                b[i] = x * Synth.AttackDecay(t, 0.005f, 0.25f) * Synth.Gate(t, 0.9f, 0.001f, 0.1f);
            }
            Synth.Normalize(b, 1f);
            for (int k = 0; k < 12; k++) Synth.AddBlip(b, Synth.Range(rng, 0.08f, 0.85f), rng, Synth.Range(rng, 0.25f, 0.6f));
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>Climbing out: a gurgle, bubbles thinning out over a second, then three drips.</summary>
        static float[] Drain(System.Random rng)
        {
            var b = Synth.Buffer(1.25f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            int n = Synth.Index(1f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                b[i] = lp2.Process(lp1.Process(Synth.Noise(rng), 500f), 500f) * Synth.Gate(t, 1f, 0.03f, 0.7f);
            }
            Synth.Normalize(b, 0.4f);
            for (int k = 0; k < 22; k++)
            {
                float u = (float)rng.NextDouble();
                Synth.AddBlip(b, u * u, rng, Synth.Range(rng, 0.3f, 0.7f) * (1f - u * 0.6f));
            }
            // Drips, like SubAmbience's: 1800 falling to 700 Hz over 90 ms.
            Synth.AddChirp(b, Synth.Range(rng, 0.62f, 0.7f), 1800f, 700f, 0.09f, 0.5f, 0.018f);
            Synth.AddChirp(b, Synth.Range(rng, 0.86f, 0.94f), 1800f, 700f, 0.09f, 0.4f, 0.018f);
            Synth.AddChirp(b, Synth.Range(rng, 1.08f, 1.14f), 1800f, 700f, 0.09f, 0.3f, 0.018f);
            return Synth.Normalize(b, 0.45f);
        }

        /// <summary>The hatch latch: two knocks 40 ms apart ringing at 900 Hz and 1.4 kHz.</summary>
        static float[] Clank(System.Random rng)
        {
            var knocks = Synth.Buffer(0.3f);
            int second = Synth.Index(0.04f);
            for (int i = 0; i < 20; i++)
            {
                knocks[i] = Synth.Noise(rng);
                knocks[second + i] = Synth.Noise(rng) * 0.7f;
            }
            var b = Synth.Buffer(0.3f);
            Synth.Mix(b, Synth.BandPassed(knocks, 900f, 0.008f), 1f);
            Synth.Mix(b, Synth.BandPassed(knocks, 1400f, 0.006f), 0.7f);
            Synth.Mix(b, knocks, 0.1f);
            Synth.FadeOut(b, 0.06f);
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>Demand-regulator breath: a hissing inhale (1.2-2.8 kHz), then exhaled bubbles.</summary>
        static float[] Breath(System.Random rng)
        {
            var b = Synth.Buffer(2.1f);
            var hp1 = new Synth.HighPass();
            var hp2 = new Synth.HighPass();
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            int n = Synth.Index(0.9f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                float x = hp2.Process(hp1.Process(Synth.Noise(rng), 1200f), 1200f);
                x = lp2.Process(lp1.Process(x, 2800f), 2800f);
                float env = t < 0.3f ? Mathf.Sin(t / 0.3f * Mathf.PI * 0.5f) : Mathf.Cos((t - 0.3f) / 0.6f * Mathf.PI * 0.5f);
                float flutter = 0.8f + 0.2f * Synth.Sine(45.0 * i / Synth.Rate);
                b[i] = x * env * flutter;
            }
            Synth.Normalize(b, 0.6f);
            int bubbles = rng.Next(8, 15);
            for (int k = 0; k < bubbles; k++) Synth.AddBlip(b, Synth.Range(rng, 0.95f, 1.95f), rng, Synth.Range(rng, 0.3f, 0.8f));
            return Synth.Normalize(b, 0.45f);
        }

        /// <summary>A crewmate breathing out: 8-14 bubbles over 1.1 s.</summary>
        static float[] Exhale(System.Random rng)
        {
            var b = Synth.Buffer(1.2f);
            int bubbles = rng.Next(8, 15);
            for (int k = 0; k < bubbles; k++) Synth.AddBlip(b, Synth.Range(rng, 0f, 1.1f), rng, Synth.Range(rng, 0.35f, 1f));
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>A boot on the steel deck: a 350 Hz thud with a faint 1.8 kHz ping.</summary>
        static float[] Step(System.Random rng)
        {
            var hit = Synth.Buffer(0.09f);
            int n = Synth.Index(0.02f);
            for (int i = 0; i < n; i++) hit[i] = Synth.Noise(rng) * Synth.AttackDecay(Synth.Seconds(i), 0.001f, 0.006f);
            var b = Synth.Buffer(0.09f);
            Synth.Mix(b, Synth.BandPassed(hit, 350f * Synth.Range(rng, 0.9f, 1.1f), 0.1f), 1f);
            Synth.Mix(b, Synth.BandPassed(hit, 1800f * Synth.Range(rng, 0.92f, 1.08f), 0.03f), 0.3f);
            Synth.FadeOut(b, 0.02f);
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>
        /// A modem burst then two 1.6 kHz beeps. Broken: static, random 30-80 ms dropouts and three
        /// descending beeps instead (1.2, 0.9, 0.6 kHz).
        /// </summary>
        static float[] Modem(System.Random rng, float burst, bool broken)
        {
            var b = Synth.Buffer(burst + (broken ? 0.55f : 0.35f));
            Synth.AddFsk(b, 0f, burst, 0.8f, rng);
            if (broken)
            {
                for (float t = Synth.Range(rng, 0.03f, 0.1f); t < burst - 0.05f; t += Synth.Range(rng, 0.07f, 0.18f))
                    Synth.Silence(b, t, Synth.Range(rng, 0.03f, 0.08f));
                var lp = new Synth.LowPass();
                int n = Synth.Index(burst + 0.05f);
                for (int i = 0; i < n && i < b.Length; i++) b[i] += lp.Process(Synth.Noise(rng), 2500f) * 0.5f;
            }
            float at = burst + 0.08f;
            if (broken)
            {
                Synth.AddSquare(b, at, 1200f, 0.09f, 0.6f);
                Synth.AddSquare(b, at + 0.13f, 900f, 0.09f, 0.6f);
                Synth.AddSquare(b, at + 0.26f, 600f, 0.12f, 0.6f);
            }
            else
            {
                Synth.AddSquare(b, at, 1600f, 0.07f, 0.6f);
                Synth.AddSquare(b, at + 0.12f, 1600f, 0.07f, 0.6f);
            }
            Synth.Speaker(b, 300f, 3200f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>NO DATA / NO SIGNAL: 250 ms of 150 Hz and 100 Hz square together.</summary>
        static float[] ErrorBuzz()
        {
            var b = Synth.Buffer(0.26f);
            int n = Synth.Index(0.25f);
            for (int i = 0; i < n; i++)
            {
                double c = (double)i / Synth.Rate;
                b[i] = (Synth.Square(150.0 * c) + Synth.Square(100.0 * c)) * 0.5f * Synth.Gate(Synth.Seconds(i), 0.25f, 0.003f, 0.01f);
            }
            Synth.LowPassAll(b, 2000f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>The helmet's low-air beep: 880 then 660 Hz through a tiny speaker.</summary>
        static float[] AirBeep()
        {
            var b = Synth.Buffer(0.2f);
            Synth.AddSquare(b, 0f, 880f, 0.09f, 1f);
            Synth.AddSquare(b, 0.1f, 660f, 0.09f, 1f);
            Synth.Speaker(b, 500f, 3000f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>Hull stress: a click ringing briefly (about 8 ms) somewhere between 2.5 and 4 kHz.</summary>
        static float[] Crack(System.Random rng)
        {
            var hit = Synth.Buffer(0.03f);
            int n = Synth.Index(0.0015f);
            for (int i = 0; i < n; i++) hit[i] = Synth.Noise(rng);
            var b = Synth.Buffer(0.03f);
            Synth.Mix(b, Synth.BandPassed(hit, Synth.Range(rng, 2500f, 4000f), 0.05f), 1f);
            Synth.Mix(b, hit, 0.3f);
            Synth.FadeOut(b, 0.01f);
            return Synth.Normalize(b, 0.45f);
        }

        /// <summary>Crushed: a low noise slam, a sharp crack and a 35 Hz rumble fading over 1.5 s.</summary>
        static float[] Implosion(System.Random rng)
        {
            var b = Synth.Buffer(1.6f);
            var slam = Synth.Buffer(0.5f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            for (int i = 0; i < slam.Length; i++)
            {
                float t = Synth.Seconds(i);
                slam[i] = lp2.Process(lp1.Process(Synth.Noise(rng), 180f), 180f) * Synth.AttackDecay(t, 0.002f, 0.15f) * Synth.Gate(t, 0.5f, 0.001f, 0.1f);
            }
            Synth.Mix(b, Synth.Normalize(slam, 1f), 1f);
            var crack = Synth.Buffer(0.03f);
            var hp = new Synth.HighPass();
            int n = Synth.Index(0.025f);
            for (int i = 0; i < n; i++) crack[i] = hp.Process(Synth.Noise(rng), 2500f) * Synth.AttackDecay(Synth.Seconds(i), 0.001f, 0.008f);
            Synth.Mix(b, Synth.Normalize(crack, 1f), 0.6f);
            n = Synth.Index(1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                b[i] += Synth.Sine(35.0 * i / Synth.Rate) * 0.8f * Synth.AttackDecay(t, 0.01f, 0.5f) * Synth.Gate(t, 1.5f, 0.01f, 0.3f);
            }
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>Drowned: the last air escaping, 40 bubbles over 1.5 s.</summary>
        static float[] BubbleTorrent(System.Random rng)
        {
            var b = Synth.Buffer(1.65f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            int n = Synth.Index(1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                b[i] = lp2.Process(lp1.Process(Synth.Noise(rng), 600f), 600f) * Synth.Gate(t, 1.5f, 0.02f, 0.9f);
            }
            Synth.Normalize(b, 0.4f);
            for (int k = 0; k < 40; k++)
            {
                float u = (float)rng.NextDouble();
                Synth.AddBlip(b, u * 1.5f, rng, Synth.Range(rng, 0.4f, 1f) * (1f - u * 0.5f), 200f, 700f);
            }
            return Synth.Normalize(b, 0.5f);
        }

        /// <summary>A crewmate's suit stopped reporting: three falling beeps (1.2, 0.9, 0.6 kHz).</summary>
        static float[] NoVitals()
        {
            var b = Synth.Buffer(0.46f);
            Synth.AddSquare(b, 0f, 1200f, 0.12f, 1f);
            Synth.AddSquare(b, 0.16f, 900f, 0.12f, 1f);
            Synth.AddSquare(b, 0.32f, 600f, 0.12f, 1f);
            Synth.Speaker(b, 300f, 3200f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>Credits in: a latch "ka", then a "chunk" of low noise and a 180 Hz thump.</summary>
        static float[] KaChunk(System.Random rng)
        {
            var b = Synth.Buffer(0.25f);
            var ka = Synth.Buffer(0.012f);
            var hp = new Synth.HighPass();
            for (int i = 0; i < ka.Length; i++) ka[i] = hp.Process(Synth.Noise(rng), 2500f) * Synth.AttackDecay(Synth.Seconds(i), 0.0005f, 0.002f);
            Synth.Mix(b, Synth.Normalize(ka, 1f), 0.5f);
            var chunk = Synth.Buffer(0.08f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            for (int i = 0; i < chunk.Length; i++)
                chunk[i] = lp2.Process(lp1.Process(Synth.Noise(rng), 400f), 400f) * Synth.AttackDecay(Synth.Seconds(i), 0.001f, 0.025f);
            Synth.Mix(b, Synth.Normalize(chunk, 1f), 0.8f, 0.06f);
            var thump = Synth.Buffer(0.12f);
            for (int i = 0; i < thump.Length; i++) thump[i] = Synth.Sine(180.0 * i / Synth.Rate) * Synth.AttackDecay(Synth.Seconds(i), 0.002f, 0.03f);
            Synth.Mix(b, thump, 0.8f, 0.06f);
            return Synth.Normalize(b, 0.45f);
        }

        /// <summary>Surfacing: saws at 110 and 165 Hz through an 800 Hz low-pass, 1.5 s.</summary>
        static float[] Foghorn()
        {
            var b = Synth.Buffer(1.6f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            double p1 = 0, p2 = 0;
            int n = Synth.Index(1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                float vibrato = 1f + 0.004f * Synth.Sine(5.0 * i / Synth.Rate);
                p1 += 110f * vibrato / Synth.Rate;
                p2 += 165f * vibrato / Synth.Rate;
                float x = Synth.Saw(p1) + Synth.Saw(p2) * 0.8f;
                b[i] = lp2.Process(lp1.Process(x, 800f), 800f) * Synth.Gate(t, 1.5f, 0.12f, 0.45f);
            }
            return Synth.Normalize(b, 0.4f);
        }

        /// <summary>Ballast tanks flooding (going down): low gurgling water with bubbles, 2 s.</summary>
        static float[] BallastFlood(System.Random rng)
        {
            var b = Synth.Buffer(2.1f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            int n = Synth.Index(2f), step = Synth.Index(1f / 8f);
            float gurgle = 0.5f, target = 0.5f;
            for (int i = 0; i < n; i++)
            {
                if (i % step == 0) target = Synth.Range(rng, 0.2f, 1f); // a new random level 8 times a second
                gurgle += (target - gurgle) * 0.002f;
                float cutoff = 500f + 400f * gurgle;
                b[i] = lp2.Process(lp1.Process(Synth.Noise(rng), cutoff), cutoff) * gurgle * Synth.Gate(Synth.Seconds(i), 2f, 0.25f, 0.7f);
            }
            Synth.Normalize(b, 1f);
            for (int k = 0; k < 26; k++) Synth.AddBlip(b, Synth.Range(rng, 0.1f, 1.9f), rng, Synth.Range(rng, 0.2f, 0.5f), 150f, 500f);
            return Synth.Normalize(b, 0.4f);
        }

        /// <summary>Ballast blown (going up): a high-pressure hiss with bubbles, 1.5 s.</summary>
        static float[] BallastBlow(System.Random rng)
        {
            var b = Synth.Buffer(1.6f);
            var hp1 = new Synth.HighPass();
            var hp2 = new Synth.HighPass();
            int n = Synth.Index(1.5f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                float x = hp2.Process(hp1.Process(Synth.Noise(rng), 1500f), 1500f);
                b[i] = x * Synth.AttackDecay(t, 0.04f, 0.6f) * Synth.Gate(t, 1.5f, 0.01f, 0.4f);
            }
            Synth.Normalize(b, 0.8f);
            for (int k = 0; k < 20; k++) Synth.AddBlip(b, Synth.Range(rng, 0.05f, 1.4f), rng, Synth.Range(rng, 0.3f, 0.6f), 300f, 900f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>
        /// The drive motor, a seamless 2 s loop: saws at 38 and 57 Hz (whole cycles in 2 s) with a
        /// 4 Hz chug, low-passed at 300 Hz. Rendered twice as long so the filters settle, and the
        /// second half is kept.
        /// </summary>
        static float[] Motor()
        {
            int n = Synth.Index(2f);
            var raw = new float[n * 2];
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            for (int i = 0; i < raw.Length; i++)
            {
                double t = (double)i / Synth.Rate;
                float x = Synth.Saw(38.0 * t) + Synth.Saw(57.0 * t) * 0.7f;
                x *= 0.85f + 0.15f * Synth.Sine(4.0 * t);
                raw[i] = lp2.Process(lp1.Process(x, 300f), 300f);
            }
            var b = new float[n];
            Array.Copy(raw, n, b, 0, n);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>Active sonar: a 1.5 kHz ping decaying over about 1.2 s, echoes at 0.25 s (-9 dB) and 0.5 s (-16 dB).</summary>
        static float[] SonarPing()
        {
            var ping = Synth.Buffer(1.3f);
            for (int i = 0; i < ping.Length; i++)
            {
                float t = Synth.Seconds(i);
                ping[i] = Synth.Sine(1500.0 * i / Synth.Rate) * Synth.AttackDecay(t, 0.003f, 0.3f) * Synth.Gate(t, 1.3f, 0.001f, 0.1f);
            }
            var b = Synth.Buffer(1.85f);
            Synth.Mix(b, ping, 1f);
            Synth.Mix(b, ping, 0.355f, 0.25f);
            Synth.Mix(b, ping, 0.158f, 0.5f);
            return Synth.Normalize(b, 0.35f);
        }

        /// <summary>A chip into the reader: plastic slide (1.5 to 4 kHz), latch click, then the drive whirring up.</summary>
        static float[] ChipInsert(System.Random rng)
        {
            var b = Synth.Buffer(1.1f);

            var slide = Synth.Buffer(0.13f);
            var bp = new Synth.BandPass();
            int n = Synth.Index(0.12f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                slide[i] = bp.Process(Synth.Noise(rng), 1500f * Mathf.Pow(4000f / 1500f, t / 0.12f), 0.3f) * Synth.Gate(t, 0.12f, 0.01f, 0.03f);
            }
            Synth.Mix(b, Synth.Normalize(slide, 1f), 0.6f);

            var click = Synth.Buffer(0.006f);
            var hp = new Synth.HighPass();
            for (int i = 0; i < click.Length; i++) click[i] = hp.Process(Synth.Noise(rng), 3000f) * Synth.AttackDecay(Synth.Seconds(i), 0.0003f, 0.0015f);
            Synth.Mix(b, Synth.Normalize(click, 1f), 0.8f, 0.125f);

            // Drive whirr: a saw rising 55 to 110 Hz over 0.8 s, ticking 12 times a second.
            var whirr = Synth.Buffer(0.85f);
            var lp1 = new Synth.LowPass();
            var lp2 = new Synth.LowPass();
            double phase = 0;
            n = Synth.Index(0.8f);
            for (int i = 0; i < n; i++)
            {
                float t = Synth.Seconds(i);
                phase += Mathf.Lerp(55f, 110f, t / 0.8f) / Synth.Rate;
                float tick = Mathf.Exp(-(t * 12f % 1f) * 14f);
                float x = Synth.Saw(phase) * (0.6f + 0.4f * tick) + Synth.Noise(rng) * tick * 0.3f;
                whirr[i] = lp2.Process(lp1.Process(x, 1200f), 1200f) * Synth.Gate(t, 0.8f, 0.05f, 0.15f);
            }
            Synth.Mix(b, Synth.Normalize(whirr, 1f), 0.5f, 0.2f);
            return Synth.Normalize(b, 0.4f);
        }
    }
}
