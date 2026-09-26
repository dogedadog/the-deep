using TheDeep.Core;
using UnityEngine;

namespace TheDeep.Creatures
{
    /// <summary>Pulses an emissive glow (and optional light), like bioluminescence.</summary>
    public class GlowPulse : MonoBehaviour
    {
        [SerializeField] Renderer[] renderers;
        [SerializeField] Color glow = new(0.3f, 0.8f, 1.2f);
        [SerializeField] Light lamp;
        [SerializeField] float seconds = 3f;

        MaterialPropertyBlock block;
        float baseIntensity;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (lamp != null) baseIntensity = lamp.intensity;
        }

        void Update()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(SharedClock.Time / seconds * Mathf.PI * 2f + transform.position.x);
            float k = 0.45f + 0.55f * wave;
            block.SetColor("_EmissionColor", glow * k);
            foreach (var r in renderers) r.SetPropertyBlock(block);
            if (lamp != null) lamp.intensity = baseIntensity * k;
        }
    }
}
