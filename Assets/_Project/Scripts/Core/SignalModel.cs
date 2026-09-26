using UnityEngine;

namespace TheDeep.Core
{
    /// <summary>
    /// Radio signal strength between a diver and the sub (0..1). Placeholder distance model until
    /// the walkie-talkie step, where range upgrades will extend it.
    /// </summary>
    public static class SignalModel
    {
        /// <summary>Middle of the sub's hull; updated when the sub moves between dive stations.</summary>
        public static Vector3 SubCenter { get; set; } = new(0f, 1.3f, 0f);
        const float FullStrengthRange = 25f;
        const float DeadRange = 80f;

        /// <summary>Below this, transmitted data arrives corrupted and must be repaired on the terminal.</summary>
        public const float CorruptionThreshold = 0.6f;
        /// <summary>At or below this, nothing gets through.</summary>
        public const float NoSignalThreshold = 0.1f;

        /// <summary>Walkie-talkie range upgrade: scales both ranges.</summary>
        public static float RangeMultiplier { get; set; } = 1f;

        public static float Strength(Vector3 position)
        {
            float d = Vector3.Distance(position, SubCenter);
            float full = FullStrengthRange * RangeMultiplier;
            float dead = DeadRange * RangeMultiplier;
            return Mathf.Clamp01(1f - (d - full) / (dead - full));
        }

        public static string Bars(float strength)
        {
            int n = Mathf.RoundToInt(strength * 5f);
            return new string('|', n) + new string('.', 5 - n);
        }
    }
}
