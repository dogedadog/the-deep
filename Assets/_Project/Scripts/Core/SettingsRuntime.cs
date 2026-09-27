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

        // Settings edits only update PlayerPrefs in memory; make sure they reach the disk.
        void OnApplicationQuit() => GameSettings.Flush();

        void Apply()
        {
            // Only touches the resolution / window mode when those actually changed (not on every slider tick).
            GameSettings.ApplyDisplay();
            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = GameSettings.Brightness;
            }
        }
    }
}
