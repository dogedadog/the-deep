using UnityEngine;

namespace TheDeep.Voice
{
    /// <summary>
    /// Plays one remote player's voice from a small jitter buffer, through either the proximity
    /// path (3D, muffled underwater) or the radio path (2D walkie-talkie: band-limited, distorted,
    /// static and dropouts that worsen with signal, squelch crackles at the start and end).
    /// The effects are done per voice in code, since each one depends on that speaker's situation.
    /// </summary>
    public class VoiceOutput : MonoBehaviour
    {
        public enum Mode { Proximity, Radio }

        const int Capacity = VoiceCodec.SampleRate * 2;
        const int Prebuffer = 480;         // 60 ms before starting playback
        const int MaxLatency = 2400;       // drop audio beyond 300 ms of backlog

        readonly float[] ring = new float[Capacity];
        readonly object gate = new();
        readonly System.Random rng = new();
        int readIndex, writeIndex, count;
        bool playing;
        int squelchTail, squelchHead, dropoutLeft;

        Mode mode;
        AudioSource source;

        // Set on the main thread, read by the audio callback.
        volatile float gain = 1f;
        volatile float quality = 1f;
        volatile bool muffled;

        // Filter state (audio thread only).
        float hpPrevIn, hpPrevOut, lpOut, muffleOut;

        public void Init(Mode outputMode)
        {
            mode = outputMode;
            var clip = AudioClip.Create($"Voice_{mode}", VoiceCodec.SampleRate, 1, VoiceCodec.SampleRate, true, OnRead);
            source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = mode == Mode.Proximity ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 20f;
            source.dopplerLevel = 0f;
            source.Play();
        }

        /// <summary>Main thread: how this voice should sound right now.</summary>
        public void Configure(float volume, float signalQuality, bool underwater)
        {
            gain = volume;
            quality = Mathf.Clamp01(signalQuality);
            muffled = underwater;
        }

        public void Push(float[] samples)
        {
            lock (gate)
            {
                foreach (float s in samples)
                {
                    ring[writeIndex] = s;
                    writeIndex = (writeIndex + 1) % Capacity;
                    if (count < Capacity) count++;
                    else readIndex = (readIndex + 1) % Capacity;
                }
                // Keep latency low: if we've fallen behind, skip ahead.
                while (count > MaxLatency)
                {
                    readIndex = (readIndex + 1) % Capacity;
                    count--;
                }
            }
        }

        void OnRead(float[] data)
        {
            float g = gain, q = quality;
            bool underwater = muffled;
            lock (gate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (!playing && count >= Prebuffer)
                    {
                        playing = true;
                        squelchHead = 400; // "kssht" as the channel opens
                    }
                    float voice = 0f;
                    if (playing)
                    {
                        if (count > 0)
                        {
                            voice = ring[readIndex];
                            readIndex = (readIndex + 1) % Capacity;
                            count--;
                        }
                        else
                        {
                            playing = false;
                            squelchTail = 900; // the channel closing
                        }
                    }
                    data[i] = (mode == Mode.Radio ? RadioChain(voice, q) : ProximityChain(voice, underwater)) * g;
                }
            }
        }

        float ProximityChain(float x, bool underwater)
        {
            if (!underwater) return x;
            // Heavy low-pass: voices through water and a helmet.
            muffleOut += 0.35f * (x - muffleOut);
            return muffleOut * 1.3f + Noise() * 0.004f;
        }

        float RadioChain(float x, float q)
        {
            // Band-pass ~350-2800 Hz.
            float hp = 0.78f * (hpPrevOut + x - hpPrevIn);
            hpPrevIn = x;
            hpPrevOut = hp;
            lpOut += 0.68f * (hp - lpOut);
            // Overdriven cheap speaker.
            float v = (float)System.Math.Tanh(lpOut * 3f) * 0.8f;

            // Weak signal: voice cuts out in chunks, and is gone entirely near the edge of range.
            if (dropoutLeft > 0) { dropoutLeft--; v = 0f; }
            else if (q < 0.55f && rng.NextDouble() < (0.55f - q) * 0.004f) dropoutLeft = 300 + rng.Next(900);
            if (q < 0.12f) v = 0f;

            bool active = playing || squelchTail > 0;
            float hiss = active ? Noise() * (0.02f + (1f - q) * 0.22f) : 0f;
            if (squelchHead > 0) { hiss += Noise() * 0.3f * (squelchHead / 400f); squelchHead--; }
            if (squelchTail > 0) { hiss += Noise() * 0.35f * (squelchTail / 900f); squelchTail--; }
            // Occasional crackle bursts on bad signal.
            if (active && q < 0.7f && rng.NextDouble() < (0.7f - q) * 0.002f) hiss += Noise() * 0.6f;
            return v + hiss;
        }

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
    }
}
