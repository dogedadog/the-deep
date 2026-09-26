using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDeep.Core
{
    /// <summary>Pushes <see cref="GameSettings"/> into the engine at startup and whenever they change.</summary>
    public class SettingsRuntime : MonoBehaviour
    {
        ColorAdjustments colorAdjustments;

        void Start()
        {
            var volume = FindFirstObjectByType<Volume>();
            // .profile gives this scene its own copy, so the asset on disk isn't modified.
            if (volume != null) volume.profile.TryGet(out colorAdjustments);
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDestroy() => GameSettings.Changed -= Apply;

        void Apply()
        {
            GameSettings.ApplyDisplay();
            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = GameSettings.Brightness;
            }
        }
    }
}
