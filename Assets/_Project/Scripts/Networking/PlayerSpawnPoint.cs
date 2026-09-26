using UnityEngine;

namespace TheDeep.Networking
{
    /// <summary>Where players appear when they join. Player N uses point N (wrapping around).</summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        [SerializeField] int index;
        public int Index => index;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.3f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 0.6f);
        }
    }
}
