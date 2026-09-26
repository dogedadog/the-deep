using UnityEngine;

namespace TheDeep.Core
{
    /// <summary>
    /// Radio signal strength between a diver and the sub (0..1). Placeholder distance model until
    /// the walkie-talkie step, where range upgrades will extend it.
    /// </summary>
    public static class SignalModel
    {
        static readonly Vector3 SubCenter = new(0f, 1.3f, 0f);
        const float FullStrengthRange = 25f;
        const float DeadRange = 80f;

        /// <summary>Below this, transmitted data arrives corrupted and must be repaired on the terminal.</summary>
        public const float CorruptionThreshold = 0.6f;
        /// <summary>At or below this, nothing gets through.</summary>
        public const float NoSignalThreshold = 0.1f;

        public static float Strength(Vector3 position)
        {
            float d = Vector3.Distance(position, SubCenter);
            return Mathf.Clamp01(1f - (d - FullStrengthRange) / (DeadRange - FullStrengthRange));
        }

        public static string Bars(float strength)
        {
            int n = Mathf.RoundToInt(strength * 5f);
            return new string('|', n) + new string('.', 5 - n);
        }
    }
}
