using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>Blinks a small console light on and off.</summary>
    [RequireComponent(typeof(Renderer))]
    public class BlinkingIndicator : MonoBehaviour
    {
        [SerializeField] float interval = 0.8f;

        Renderer lamp;
        float offset;

        void Awake()
        {
            lamp = GetComponent<Renderer>();
            offset = Random.value * interval * 2f;
        }

        void Update() => lamp.enabled = Mathf.Repeat(Time.time + offset, interval * 2f) < interval;
    }
}
