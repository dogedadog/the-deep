using TheDeep.Core;
using TheDeep.Player;
using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// Placeholder open-water soundscape, synthesised in code like <see cref="SubAmbience"/>:
    /// a deep pressure rumble that gets heavier the deeper you are, and slow swells of moving water.
    /// Fades in while the local player is in the water (dead divers too) and out when they climb aboard.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class OceanAmbience : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [SerializeField, Range(0f, 1f)] float waterLevel = 0.03f;

        const float TwoPi = 2f * Mathf.PI;
        const double SwellHz = 0.07;
        // Where the rumble is heaviest: a little below the deepest station.
        const float DeepestDepth = 2450f;
        // Bring the filtered noise up to a useful level (the low-passes eat most of it).
        const float RumbleMakeup = 1.5f;
        const float WaterMakeup = 6f;

        /// <summary>One noise source and filter chain per stereo channel, so left and right don't match.</summary>
        class Voice
        {
            public readonly System.Random rng; // UnityEngine.Random isn't allowed on the audio thread
            public float brown, dc, rumbleA, rumbleB, waterA, waterB;
            public Voice(int seed) { rng = new System.Random(seed); }
            public float Noise() => (float)rng.NextDouble() * 2f - 1f;
        }

        readonly Voice left = new(4321), right = new(8765);
        int sampleRate;
        float gainRate, depthRate, rumbleCoef, waterCoef, dcCoef;

        // Written on the main thread (Update), read on the audio thread.
        volatile float target;
        volatile float depthTarget;
        PlayerNetwork localPlayer;
        DiverController localDiver;

        float gain, depth01;
        double swellPhase; // wrapped to 0..1, so it stays precise in long sessions

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            gainRate = 1f / (1.5f * sampleRate); // fades over about 1.5 s
            depthRate = 1f / (0.5f * sampleRate);
            rumbleCoef = OnePole(80f);
            waterCoef = OnePole(600f);
            dcCoef = OnePole(20f);
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            if (!source.isPlaying) source.Play();
        }

        float OnePole(float hz) => 1f - Mathf.Exp(-TwoPi * hz / sampleRate);

        void Update()
        {
            // Re-fetch when the local player changes (join, leave, rehost).
            var local = PlayerNetwork.Local;
            if (local != localPlayer)
            {
                localPlayer = local;
                localDiver = local != null ? local.GetComponent<DiverController>() : null;
            }
            // Dead divers stay 'diving' until the expedition ends, so spectators keep hearing the water.
            target = localDiver != null && localDiver.IsDiving ? 1f : 0f;
            // Depth is already 1280 m at y = 0: map the shaft's own range to 0..1.
            if (local != null)
                depthTarget = Mathf.InverseLerp(WorldInfo.SurfaceDepth, DeepestDepth, WorldInfo.DepthAt(local.transform.position.y));
        }

        /// <summary>Hook for the creature step: a distant moan through the water. Silent for now.</summary>
        public void Moan(float loudness) { }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float to = target;
            if (to <= 0f && gain < 0.0001f)
            {
                // Aboard (or in the menus): nothing to hear.
                System.Array.Clear(data, 0, data.Length);
                return;
            }

            float deep = depthTarget;
            double dt = 1.0 / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                gain += (to - gain) * gainRate;
                depth01 += (deep - depth01) * depthRate;
                swellPhase += SwellHz * dt;
                if (swellPhase >= 1.0) swellPhase -= 1.0;

                float rumbleLevel = (0.15f + 0.25f * depth01) * RumbleMakeup;
                float waterGain = waterLevel * WaterMakeup;
                // The swell rolls a quarter cycle apart on the two sides.
                float swell = TwoPi * (float)swellPhase;
                float l = Render(left, rumbleLevel, waterGain * (0.6f + 0.4f * Mathf.Sin(swell)));
                float r = channels > 1 ? Render(right, rumbleLevel, waterGain * (0.6f + 0.4f * Mathf.Cos(swell))) : l;

                float g = gain * volume;
                for (int c = 0; c < channels; c++) data[i + c] = ((c & 1) == 0 ? l : r) * g;
            }
        }

        float Render(Voice v, float rumbleLevel, float waterGain)
        {
            // Pressure rumble: brown noise, drift removed, through two ~80 Hz low-passes.
            v.brown = Mathf.Clamp(v.brown + v.Noise() * 0.02f, -1f, 1f) * 0.998f;
            v.dc += (v.brown - v.dc) * dcCoef;
            v.rumbleA += (v.brown - v.dc - v.rumbleA) * rumbleCoef;
            v.rumbleB += (v.rumbleA - v.rumbleB) * rumbleCoef;
            // Moving water: noise through two ~600 Hz low-passes.
            float white = v.Noise();
            v.waterA += (white - v.waterA) * waterCoef;
            v.waterB += (v.waterA - v.waterB) * waterCoef;
            return v.rumbleB * rumbleLevel + v.waterB * waterGain;
        }
    }
}
