using TheDeep.Core;
using UnityEngine;

namespace TheDeep.Creatures
{
    /// <summary>Stand-in movement: crawls back and forth between two seabed points, turning at each end.</summary>
    public class Crawler : MonoBehaviour
    {
        [SerializeField] Vector3 pointA;
        [SerializeField] Vector3 pointB;
        [SerializeField] float speed = 0.25f;

        void Update()
        {
            float length = Mathf.Max(Vector3.Distance(pointA, pointB), 0.01f);
            float travelled = SharedClock.Time * speed;
            bool outbound = Mathf.Repeat(travelled, length * 2f) < length;
            float t = Mathf.PingPong(travelled, length) / length;
            Vector3 heading = (outbound ? pointB - pointA : pointA - pointB).normalized;
            transform.SetPositionAndRotation(Vector3.Lerp(pointA, pointB, t), Quaternion.LookRotation(heading));
        }
    }
}
