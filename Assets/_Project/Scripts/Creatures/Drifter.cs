using TheDeep.Core;
using UnityEngine;

namespace TheDeep.Creatures
{
    /// <summary>
    /// Stand-in movement until real creature AI (step 5): circles slowly while bobbing up and down.
    /// Driven by the shared clock so every player sees it in the same place.
    /// </summary>
    public class Drifter : MonoBehaviour
    {
        [SerializeField] float radius = 4f;
        [SerializeField] float secondsPerLap = 60f;
        [SerializeField] float bobHeight = 0.6f;
        [SerializeField] float bobSeconds = 5f;

        Vector3 center;

        void Awake() => center = transform.position;

        void Update()
        {
            float t = SharedClock.Time;
            float angle = t / secondsPerLap * Mathf.PI * 2f;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            pos.y += Mathf.Sin(t / bobSeconds * Mathf.PI * 2f) * bobHeight;
            Vector3 heading = new(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(heading));
        }
    }
}
