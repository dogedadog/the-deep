using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>A faulty lamp: gentle buzz most of the time, with occasional bursts of flickering out.</summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        [SerializeField] Renderer lampRenderer;
        [SerializeField] Material onMaterial;
        [SerializeField] Material offMaterial;
        [SerializeField] Vector2 secondsBetweenBursts = new(3f, 10f);
        [SerializeField] Vector2 burstDuration = new(0.15f, 0.9f);

        Light lamp;
        float baseIntensity;
        float nextBurst;
        float burstEnd;
        float nextToggle;
        bool burstOn;

        void Awake()
        {
            lamp = GetComponent<Light>();
            baseIntensity = lamp.intensity;
            nextBurst = Time.time + Random.Range(secondsBetweenBursts.x, secondsBetweenBursts.y);
        }

        void Update()
        {
            float t = Time.time;
            if (t > nextBurst)
            {
                burstEnd = t + Random.Range(burstDuration.x, burstDuration.y);
                nextBurst = burstEnd + Random.Range(secondsBetweenBursts.x, secondsBetweenBursts.y);
            }

            float k;
            if (t < burstEnd)
            {
                if (t > nextToggle)
                {
                    burstOn = !burstOn;
                    nextToggle = t + Random.Range(0.02f, 0.12f);
                }
                k = burstOn ? 1f : 0.03f;
            }
            else
            {
                k = 0.88f + 0.12f * Mathf.PerlinNoise(t * 9f, 0.3f);
            }

            lamp.intensity = baseIntensity * k;
            if (lampRenderer != null && onMaterial != null && offMaterial != null)
                lampRenderer.sharedMaterial = k > 0.3f ? onMaterial : offMaterial;
        }
    }
}
