using UnityEngine;

namespace TheDeep.Footage
{
    /// <summary>
    /// The last recordings of Dive Team 7, generated the same way on every machine. Each diver
    /// drifts across the shaft floor toward the glyph stones, then something enormous comes out of the
    /// dark above them. The camera ends where the body lies.
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
            return clip;
        }

        static Vector3 Random3(System.Random rng) =>
            new((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
    }
}
