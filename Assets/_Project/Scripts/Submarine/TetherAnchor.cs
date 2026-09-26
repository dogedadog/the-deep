using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// Where divers' ropes leave the hull (the fairlead beside the dive hatch). Also holds the
    /// rough shape of the hull, so rope simulations can keep themselves outside it.
    /// </summary>
    public class TetherAnchor : MonoBehaviour
    {
        [SerializeField, Tooltip("Hull centre line runs along world X at this Y/Z.")] Vector2 hullAxisYZ = new(1.3f, 0f);
        [SerializeField] Vector2 hullAxisX = new(-5.6f, 5.6f);
        [SerializeField] float hullRadius = 2.5f;
        [SerializeField] float seafloorY = -9f;

        public static TetherAnchor Instance { get; private set; }
        public float SeafloorY => seafloorY;

        void Awake() => Instance = this;

        /// <summary>Slightly different exit points so several ropes don't overlap exactly.</summary>
        public Vector3 AttachPoint(int index) => transform.position + transform.right * ((index % 5) - 2) * 0.06f;

        /// <summary>Pushes a point out of the hull (approximated as a capsule) and above the seafloor.</summary>
        public Vector3 Collide(Vector3 p)
        {
            var axisPoint = new Vector3(Mathf.Clamp(p.x, hullAxisX.x, hullAxisX.y), hullAxisYZ.x, hullAxisYZ.y);
            Vector3 offset = p - axisPoint;
            float d = offset.magnitude;
            if (d < hullRadius && d > 0.0001f) p = axisPoint + offset / d * hullRadius;
            if (p.y < seafloorY + 0.03f) p.y = seafloorY + 0.03f;
            return p;
        }
    }
}
