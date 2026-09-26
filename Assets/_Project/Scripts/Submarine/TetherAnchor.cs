using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>Where divers' ropes leave the hull (the fairlead beside the dive hatch). Moves with the sub.</summary>
    public class TetherAnchor : MonoBehaviour
    {
        public static TetherAnchor Instance { get; private set; }

        /// <summary>What ropes collide with: everything except players (and triggers, which are ignored anyway).</summary>
        public static int RopeCollisionMask => ~(1 << LayerMask.NameToLayer("Player"));

        void Awake() => Instance = this;

        /// <summary>Slightly different exit points so several ropes don't overlap exactly.</summary>
        public Vector3 AttachPoint(int index) => transform.position + transform.right * ((index % 5) - 2) * 0.06f;
    }
}
