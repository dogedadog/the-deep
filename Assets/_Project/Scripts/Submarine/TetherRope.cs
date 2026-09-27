using TheDeep.Core;
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
        // Reused every step so the width update allocates nothing.
        readonly AnimationCurve widthCurve = new();
        readonly Keyframe[] widthKeys = new Keyframe[5];
        LineRenderer line;

        /// <summary>Don't draw rope within this many metres of the diver (used for the diver's own rope).</summary>
        public float HideNearEnd { get; set; }

        /// <summary>Rope paid out, metres.</summary>
        public float Length { get; set; } = 5f;
        /// <summary>How much longer the rope is being forced to be than its length (m). 0 = slack.</summary>
        public float Stretch { get; private set; }
        /// <summary>Direction the rope pulls the end back along (towards the next point in).</summary>
        public Vector3 PullDirection { get; private set; }

        public void Init(Vector3 start, Vector3 end, Material material)
        {
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
            }

            // Collide with the world: if a point moved into rock (or the hull) this step, stop it at the surface.
            // A point whose neighbours are far away is snagged behind something (e.g. the rope was
            // yanked through a wall); let it pull free rather than stay stuck and eat up rope.
            int mask = TetherAnchor.RopeCollisionMask;
            float snag = Mathf.Max(rest * 4f, 1.5f);
            for (int i = 1; i < Segments; i++)
            {
                bool snagged = Vector3.Distance(points[i], points[i - 1]) > snag || Vector3.Distance(points[i], points[i + 1]) > snag;
                if (!snagged && Physics.Linecast(previous[i], points[i], out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
                    points[i] = hit.point + hit.normal * 0.04f;
            }
            points[0] = start;
            points[Segments] = end;

            float path = 0f;
            for (int i = 0; i < Segments; i++) path += Vector3.Distance(points[i], points[i + 1]);
            Stretch = Mathf.Max(0f, path - Length);
            // Look a few points back so a single kinked segment doesn't flip the pull direction.
            Vector3 back = points[Segments - 3] - end;
            PullDirection = back.sqrMagnitude > 0.0001f ? back.normalized : (start - end).normalized;

            // The diver's own view: drop the last bit of rope so it doesn't sweep across the camera.
            int shown = points.Length;
            if (HideNearEnd > 0f)
                while (shown > 2 && Vector3.Distance(points[shown - 1], end) < HideNearEnd) shown--;
            if (line.positionCount != shown) line.positionCount = shown;
            for (int i = 0; i < shown; i++) line.SetPosition(i, points[i]);
            UpdateWidth(shown);
        }

        /// <summary>
        /// Keep the line at least about a pixel wide at the pixelated resolution, so a distant rope stays
        /// one continuous line instead of breaking into dashes. Up close it stays a 5 cm rope.
        /// </summary>
        void UpdateWidth(int shown)
        {
            var cam = Camera.main;
            if (cam == null) return; // spectating through a camera that isn't MainCamera: keep the last widths
            int setting = GameSettings.PixelHeight > 0 ? GameSettings.PixelHeight : Screen.height;
            float px = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(64, setting);
            Vector3 eye = cam.transform.position;
            for (int k = 0; k < widthKeys.Length; k++)
            {
                float t = k / (float)(widthKeys.Length - 1);
                Vector3 p = points[Mathf.RoundToInt(t * (shown - 1))];
                widthKeys[k] = new Keyframe(t, Mathf.Clamp(Vector3.Distance(p, eye) * px * 1.3f, 0.05f, 0.3f));
            }
            widthCurve.keys = widthKeys;
            line.widthCurve = widthCurve;
            line.widthMultiplier = 1f;
        }
    }
}
