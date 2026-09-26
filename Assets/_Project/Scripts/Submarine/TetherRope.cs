using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// Verlet rope between a fixed start (the hull fairlead) and a moving end (a diver's harness).
    /// A fixed number of points whose spacing is Length / segments, so paying rope out or reeling
    /// it in is just a change of <see cref="Length"/>. Runs locally on every client.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TetherRope : MonoBehaviour
    {
        const int Segments = 80;
        const int Iterations = 24;
        static readonly Vector3 Sink = new(0f, -1.2f, 0f); // ropes slowly settle in water

        readonly Vector3[] points = new Vector3[Segments + 1];
        readonly Vector3[] previous = new Vector3[Segments + 1];
        LineRenderer line;
        TetherAnchor anchor;

        /// <summary>Rope paid out, metres.</summary>
        public float Length { get; set; } = 5f;
        /// <summary>How much longer the rope is being forced to be than its length (m). 0 = slack.</summary>
        public float Stretch { get; private set; }
        /// <summary>Direction the rope pulls the end back along (towards the next point in).</summary>
        public Vector3 PullDirection { get; private set; }

        public void Init(TetherAnchor anchorPoint, Vector3 start, Vector3 end, Material material)
        {
            anchor = anchorPoint;
            line = GetComponent<LineRenderer>();
            line.positionCount = points.Length;
            line.widthMultiplier = 0.05f;
            line.numCapVertices = 2;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i <= Segments; i++)
                points[i] = previous[i] = Vector3.Lerp(start, end, i / (float)Segments);
        }

        public void Simulate(Vector3 start, Vector3 end, float dt)
        {
            float rest = Mathf.Max(Length, 0.1f) / Segments;

            // Integrate the free points.
            for (int i = 1; i < Segments; i++)
            {
                Vector3 velocity = (points[i] - previous[i]) * 0.96f; // water drag
                previous[i] = points[i];
                points[i] += velocity + Sink * (dt * dt);
            }

            // Satisfy segment lengths, with both ends pinned.
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                points[0] = start;
                points[Segments] = end;
                for (int i = 0; i < Segments; i++)
                {
                    Vector3 delta = points[i + 1] - points[i];
                    float d = delta.magnitude;
                    if (d < 0.00001f) continue;
                    float error = (d - rest) / d;
                    // Ropes resist stretching but not bunching up.
                    if (error < 0f) continue;
                    Vector3 correction = delta * (error * 0.5f);
                    if (i != 0) points[i] += correction;
                    if (i + 1 != Segments) points[i + 1] -= correction;
                }
                if (anchor != null)
                    for (int i = 1; i < Segments; i++) points[i] = anchor.Collide(points[i]);
            }
            points[0] = start;
            points[Segments] = end;

            float path = 0f;
            for (int i = 0; i < Segments; i++) path += Vector3.Distance(points[i], points[i + 1]);
            Stretch = Mathf.Max(0f, path - Length);
            // Look a few points back so a single kinked segment doesn't flip the pull direction.
            Vector3 back = points[Segments - 3] - end;
            PullDirection = back.sqrMagnitude > 0.0001f ? back.normalized : (start - end).normalized;

            line.SetPositions(points);
        }
    }
}
