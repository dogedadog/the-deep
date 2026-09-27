using TheDeep.Core;
using UnityEngine;

namespace TheDeep.Creatures
{
    /// <summary>
    /// Pulses an emissive glow (and optional light), like bioluminescence. Each renderer keeps its own
    /// material's glow colour; <see cref="phaseSpread"/> offsets them so they twinkle instead of flashing as one.
    /// </summary>
    public class GlowPulse : MonoBehaviour
    {
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Renderer[] renderers;
        [Tooltip("Used for renderers whose material has no glow colour of its own.")]
        [SerializeField] Color glow = new(0.3f, 0.8f, 1.2f);
        [SerializeField] Light lamp;
        [SerializeField] float seconds = 3f;
        [Tooltip("Phase step (radians) between renderers. 0 = all pulse together; 2.399 (golden angle) looks random.")]
        [SerializeField] float phaseSpread;

        MaterialPropertyBlock block;
        Color[] baseEmission;
        float baseIntensity;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (lamp != null) baseIntensity = lamp.intensity;
            if (renderers == null) renderers = new Renderer[0];
            // Read from the shared material: renderer.material would make a copy per renderer.
            baseEmission = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i] != null ? renderers[i].sharedMaterial : null;
                var color = mat != null && mat.HasProperty(EmissionColor) ? mat.GetColor(EmissionColor) : Color.black;
                baseEmission[i] = color.maxColorComponent > 0.001f ? color : glow;
            }
        }

        void Update()
        {
            float phase = SharedClock.Time / seconds * Mathf.PI * 2f + transform.position.x;
            float sum = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                float k = Brightness(phase + i * phaseSpread);
                sum += k;
                if (renderers[i] == null) continue;
                block.SetColor(EmissionColor, baseEmission[i] * k);
                renderers[i].SetPropertyBlock(block);
            }
            // The light follows the average glow (with no renderers, the plain pulse).
            if (lamp != null) lamp.intensity = baseIntensity * (renderers.Length > 0 ? sum / renderers.Length : Brightness(phase));
        }

        static float Brightness(float phase) => 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(phase));
    }
}
