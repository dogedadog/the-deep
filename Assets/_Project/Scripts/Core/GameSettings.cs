using System;
using System.Linq;
using UnityEngine;

namespace TheDeep.Core
{
    /// <summary>
    /// Player preferences (controls, display, audio), saved per machine in PlayerPrefs.
    /// Not part of save slots. Systems read the values directly; <see cref="Changed"/> fires on edits.
    /// Edits update PlayerPrefs in memory; <see cref="Flush"/> writes them to disk.
    /// </summary>
    public static class GameSettings
    {
        public static readonly int[] FpsOptions = { 0, 30, 60, 120, 144, 165, 240 };
        public static readonly string[] PixelOptionNames = { "CHUNKY", "RETRO", "SOFT", "SHARP", "OFF" };
        static readonly int[] PixelOptionHeights = { 240, 360, 480, 720, 0 };
        public static readonly FullScreenMode[] WindowModes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
        public static readonly string[] WindowModeNames = { "BORDERLESS", "FULLSCREEN", "WINDOWED" };

        static bool loaded;
        static float sensitivity, fieldOfView, brightness, masterVolume, effectsVolume, micSensitivity, voiceVolume;
        static int micDevice;
        /// <summary>The chosen microphone's name ("" = system default). Kept even while that mic is unplugged.</summary>
        static string micName = "";
        static bool invertY, vsync;
        static int fpsIndex, pixelIndex, windowModeIndex, resolutionIndex;
        /// <summary>The chosen resolution itself, so a different monitor's list can't change it.</summary>
        static int resolutionWidth, resolutionHeight;

        public static event Action Changed;

        /// <summary>Multiplier on mouse look (1 = default).</summary>
        public static float Sensitivity { get { Load(); return sensitivity; } set => Set(ref sensitivity, Mathf.Clamp(value, 0.1f, 3f)); }
        public static bool InvertY { get { Load(); return invertY; } set => Set(ref invertY, value); }
        public static float FieldOfView { get { Load(); return fieldOfView; } set => Set(ref fieldOfView, Mathf.Clamp(value, 55f, 100f)); }
        /// <summary>Post exposure in stops, -1..1.</summary>
        public static float Brightness { get { Load(); return brightness; } set => Set(ref brightness, Mathf.Clamp(value, -1f, 1f)); }
        public static float MasterVolume { get { Load(); return masterVolume; } set => Set(ref masterVolume, Mathf.Clamp01(value)); }
        /// <summary>Sound effects and UI beeps, 0..1 (PlayerPrefs key "set.sfxVolume").</summary>
        public static float EffectsVolume { get { Load(); return effectsVolume; } set => Set(ref effectsVolume, Mathf.Clamp01(value)); }
        public static bool VSync { get { Load(); return vsync; } set => Set(ref vsync, value); }
        /// <summary>0 = only loud speech opens the mic, 1 = picks up whispers.</summary>
        public static float MicSensitivity { get { Load(); return micSensitivity; } set => Set(ref micSensitivity, Mathf.Clamp01(value)); }
        public static float VoiceVolume { get { Load(); return voiceVolume; } set => Set(ref voiceVolume, Mathf.Clamp(value, 0f, 2f)); }

        /// <summary>-1 = system default microphone, otherwise an index into Microphone.devices (resolved from the saved name).</summary>
        public static int MicDevice
        {
            get { Load(); return micDevice; }
            set
            {
                Load();
                var devices = Microphone.devices;
                int index = value < -1 ? devices.Length - 1 : value >= devices.Length ? -1 : value;
                string name = index < 0 ? "" : devices[index];
                if (index == micDevice && name == micName) return;
                micDevice = index;
                micName = name;
                Save();
                Changed?.Invoke();
            }
        }

        /// <summary>The microphone to record from, or null for the system default. Cheap: safe to poll.</summary>
        public static string MicDeviceName { get { Load(); return micDevice < 0 || string.IsNullOrEmpty(micName) ? null : micName; } }
        public static string MicDeviceLabel => MicDeviceName ?? "SYSTEM DEFAULT";
        public static int FpsIndex { get { Load(); return fpsIndex; } set => Set(ref fpsIndex, Wrap(value, FpsOptions.Length)); }
        public static int PixelIndex { get { Load(); return pixelIndex; } set => Set(ref pixelIndex, Wrap(value, PixelOptionNames.Length)); }
        public static int WindowModeIndex { get { Load(); return windowModeIndex; } set => Set(ref windowModeIndex, Wrap(value, WindowModes.Length)); }

        public static int ResolutionIndex
        {
            get { Load(); return resolutionIndex; }
            set
            {
                Load();
                var list = Resolutions;
                int index = Wrap(value, list.Length);
                var r = list[index];
                if (index == resolutionIndex && r.width == resolutionWidth && r.height == resolutionHeight) return;
                resolutionIndex = index;
                resolutionWidth = r.width;
                resolutionHeight = r.height;
                Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Rendered image height for the pixelated look; 0 means full resolution.</summary>
        public static int PixelHeight => PixelOptionHeights[PixelIndex];

        /// <summary>Distinct resolutions the monitor supports, smallest first.</summary>
        public static Resolution[] Resolutions
        {
            get
            {
                var list = Screen.resolutions
                    .GroupBy(r => (r.width, r.height)).Select(g => g.Last())
                    .OrderBy(r => r.width * r.height).ToArray();
                return list.Length > 0 ? list : new[] { Screen.currentResolution };
            }
        }

        public static string ResolutionName
        {
            get
            {
                Load();
                return $"{resolutionWidth} x {resolutionHeight}";
            }
        }

        public static string FpsName => FpsOptions[FpsIndex] == 0 ? "UNLIMITED" : FpsOptions[FpsIndex].ToString();

        static readonly string[] Keys =
        {
            "set.sensitivity", "set.invertY", "set.fov", "set.brightness", "set.volume", "set.sfxVolume",
            "set.vsync", "set.fps", "set.pixel", "set.window", "set.resW", "set.resH",
            "set.micSensitivity", "set.voiceVolume", "set.micName",
            // Older builds saved these as list indices.
            "set.resolution", "set.micDevice",
        };

        public static void ResetToDefaults()
        {
            foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
            loaded = false;
            Load();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Writes the settings to disk (edits only update PlayerPrefs in memory).</summary>
        public static void Flush() => PlayerPrefs.Save();

        public static void ApplyAudio() => AudioListener.volume = MasterVolume;

#if !UNITY_EDITOR
        /// <summary>What Screen.SetResolution was last called with, so unrelated edits don't re-apply the display.</summary>
        static (int width, int height, FullScreenMode mode) appliedDisplay;
#endif

        /// <summary>Apply display/audio settings to the engine. Cheap unless the resolution or window mode changed.</summary>
        public static void ApplyDisplay()
        {
            int vSyncCount = VSync ? 1 : 0;
            if (QualitySettings.vSyncCount != vSyncCount) QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = VSync ? -1 : (FpsOptions[FpsIndex] == 0 ? -1 : FpsOptions[FpsIndex]);
            ApplyAudio();
#if !UNITY_EDITOR
            (int width, int height, FullScreenMode mode) wanted = (resolutionWidth, resolutionHeight, WindowModes[WindowModeIndex]);
            if (wanted == appliedDisplay) return;
            appliedDisplay = wanted;
            Screen.SetResolution(wanted.width, wanted.height, wanted.mode);
#endif
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            sensitivity = PlayerPrefs.GetFloat("set.sensitivity", 1f);
            invertY = PlayerPrefs.GetInt("set.invertY", 0) == 1;
            fieldOfView = PlayerPrefs.GetFloat("set.fov", 70f);
            brightness = PlayerPrefs.GetFloat("set.brightness", 0f);
            masterVolume = PlayerPrefs.GetFloat("set.volume", 0.8f);
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("set.sfxVolume", 1f));
            vsync = PlayerPrefs.GetInt("set.vsync", 1) == 1;
            micSensitivity = PlayerPrefs.GetFloat("set.micSensitivity", 0.6f);
            voiceVolume = PlayerPrefs.GetFloat("set.voiceVolume", 1f);
            // The mic is saved by name; one that's unplugged falls back to the system default.
            micName = PlayerPrefs.GetString("set.micName", "");
            micDevice = string.IsNullOrEmpty(micName) ? -1 : Array.IndexOf(Microphone.devices, micName);
            fpsIndex = Wrap(PlayerPrefs.GetInt("set.fps", 2), FpsOptions.Length);
            pixelIndex = Wrap(PlayerPrefs.GetInt("set.pixel", 1), PixelOptionNames.Length);
            windowModeIndex = Wrap(PlayerPrefs.GetInt("set.window", 0), WindowModes.Length);

            // The resolution is saved as width x height; default to (or fall back on) the monitor's current one.
            var list = Resolutions;
            var current = Screen.currentResolution;
            int nativeIndex = Array.FindLastIndex(list, r => r.width == current.width && r.height == current.height);
            if (nativeIndex < 0) nativeIndex = list.Length - 1;
            int savedWidth = PlayerPrefs.GetInt("set.resW", 0), savedHeight = PlayerPrefs.GetInt("set.resH", 0);
            int savedIndex = Array.FindLastIndex(list, r => r.width == savedWidth && r.height == savedHeight);
            resolutionIndex = Mathf.Clamp(savedIndex < 0 ? nativeIndex : savedIndex, 0, list.Length - 1);
            resolutionWidth = list[resolutionIndex].width;
            resolutionHeight = list[resolutionIndex].height;
        }

        static void Save()
        {
            PlayerPrefs.SetFloat("set.sensitivity", sensitivity);
            PlayerPrefs.SetInt("set.invertY", invertY ? 1 : 0);
            PlayerPrefs.SetFloat("set.fov", fieldOfView);
            PlayerPrefs.SetFloat("set.brightness", brightness);
            PlayerPrefs.SetFloat("set.volume", masterVolume);
            PlayerPrefs.SetFloat("set.sfxVolume", effectsVolume);
            PlayerPrefs.SetInt("set.vsync", vsync ? 1 : 0);
            PlayerPrefs.SetFloat("set.micSensitivity", micSensitivity);
            PlayerPrefs.SetFloat("set.voiceVolume", voiceVolume);
            PlayerPrefs.SetString("set.micName", micName);
            PlayerPrefs.SetInt("set.fps", fpsIndex);
            PlayerPrefs.SetInt("set.pixel", pixelIndex);
            PlayerPrefs.SetInt("set.window", windowModeIndex);
            PlayerPrefs.SetInt("set.resW", resolutionWidth);
            PlayerPrefs.SetInt("set.resH", resolutionHeight);
        }

        static void Set<T>(ref T field, T value)
        {
            Load();
            if (Equals(field, value)) return;
            field = value;
            Save();
            Changed?.Invoke();
        }

        static int Wrap(int value, int count) => count <= 0 ? 0 : (value % count + count) % count;

        /// <summary>Mouse delta after sensitivity and invert-Y are applied.</summary>
        public static Vector2 AdjustLook(Vector2 delta) => new(delta.x * Sensitivity, delta.y * Sensitivity * (InvertY ? -1f : 1f));
    }
}
