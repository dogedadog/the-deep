using UnityEngine;

namespace TheDeep.Core
{
    /// <summary>Spins an object around a local axis (propellers, beacons).</summary>
    public class Rotator : MonoBehaviour
    {
        [SerializeField] Vector3 axis = Vector3.up;
        [SerializeField] float degreesPerSecond = 90f;

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
