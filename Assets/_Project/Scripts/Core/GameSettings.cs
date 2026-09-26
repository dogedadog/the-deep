using System;
using System.Linq;
using UnityEngine;

namespace TheDeep.Core
{
    /// <summary>
    /// Player preferences (controls, display, audio), saved per machine in PlayerPrefs.
    /// Not part of save slots. Systems read the values directly; <see cref="Changed"/> fires on edits.
    /// </summary>
    public static class GameSettings
    {
        public static readonly int[] FpsOptions = { 0, 30, 60, 120, 144, 165, 240 };
        public static readonly string[] PixelOptionNames = { "CHUNKY", "RETRO", "SOFT", "SHARP", "OFF" };
        static readonly int[] PixelOptionHeights = { 240, 360, 480, 720, 0 };
        public static readonly FullScreenMode[] WindowModes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
        public static readonly string[] WindowModeNames = { "BORDERLESS", "FULLSCREEN", "WINDOWED" };

        static bool loaded;
        static float sensitivity, fieldOfView, brightness, masterVolume, micSensitivity, voiceVolume;
        static int micDevice;
        static bool invertY, vsync;
        static int fpsIndex, pixelIndex, windowModeIndex, resolutionIndex;

        public static event Action Changed;

        /// <summary>Multiplier on mouse look (1 = default).</summary>
        public static float Sensitivity { get { Load(); return sensitivity; } set => Set(ref sensitivity, Mathf.Clamp(value, 0.1f, 3f)); }
        public static bool InvertY { get { Load(); return invertY; } set => Set(ref invertY, value); }
        public static float FieldOfView { get { Load(); return fieldOfView; } set => Set(ref fieldOfView, Mathf.Clamp(value, 55f, 100f)); }
        /// <summary>Post exposure in stops, -1..1.</summary>
        public static float Brightness { get { Load(); return brightness; } set => Set(ref brightness, Mathf.Clamp(value, -1f, 1f)); }
        public static float MasterVolume { get { Load(); return masterVolume; } set => Set(ref masterVolume, Mathf.Clamp01(value)); }
        public static bool VSync { get { Load(); return vsync; } set => Set(ref vsync, value); }
        /// <summary>0 = only loud speech opens the mic, 1 = picks up whispers.</summary>
        public static float MicSensitivity { get { Load(); return micSensitivity; } set => Set(ref micSensitivity, Mathf.Clamp01(value)); }
        public static float VoiceVolume { get { Load(); return voiceVolume; } set => Set(ref voiceVolume, Mathf.Clamp(value, 0f, 2f)); }
        /// <summary>-1 = system default microphone, otherwise an index into Microphone.devices.</summary>
        public static int MicDevice { get { Load(); return micDevice; } set => Set(ref micDevice, value < -1 ? Microphone.devices.Length - 1 : value >= Microphone.devices.Length ? -1 : value); }

        public static string MicDeviceName => MicDevice < 0 || MicDevice >= Microphone.devices.Length ? null : Microphone.devices[MicDevice];
        public static string MicDeviceLabel => MicDeviceName ?? "SYSTEM DEFAULT";
        public static int FpsIndex { get { Load(); return fpsIndex; } set => Set(ref fpsIndex, Wrap(value, FpsOptions.Length)); }
        public static int PixelIndex { get { Load(); return pixelIndex; } set => Set(ref pixelIndex, Wrap(value, PixelOptionNames.Length)); }
        public static int WindowModeIndex { get { Load(); return windowModeIndex; } set => Set(ref windowModeIndex, Wrap(value, WindowModes.Length)); }
        public static int ResolutionIndex { get { Load(); return resolutionIndex; } set => Set(ref resolutionIndex, Wrap(value, Resolutions.Length)); }

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
                var r = Resolutions[Mathf.Clamp(ResolutionIndex, 0, Resolutions.Length - 1)];
                return $"{r.width} x {r.height}";
            }
        }

        public static string FpsName => FpsOptions[FpsIndex] == 0 ? "UNLIMITED" : FpsOptions[FpsIndex].ToString();

        static readonly string[] Keys =
        {
            "set.sensitivity", "set.invertY", "set.fov", "set.brightness", "set.volume",
            "set.vsync", "set.fps", "set.pixel", "set.window", "set.resolution",
            "set.micSensitivity", "set.voiceVolume", "set.micDevice",
        };

        public static void ResetToDefaults()
        {
            foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
            loaded = false;
            Load();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Apply display/audio settings to the engine. Call after changing window settings.</summary>
        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : (FpsOptions[FpsIndex] == 0 ? -1 : FpsOptions[FpsIndex]);
            AudioListener.volume = MasterVolume;
#if !UNITY_EDITOR
            var r = Resolutions[Mathf.Clamp(ResolutionIndex, 0, Resolutions.Length - 1)];
            Screen.SetResolution(r.width, r.height, WindowModes[WindowModeIndex]);
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
            vsync = PlayerPrefs.GetInt("set.vsync", 1) == 1;
            micSensitivity = PlayerPrefs.GetFloat("set.micSensitivity", 0.6f);
            voiceVolume = PlayerPrefs.GetFloat("set.voiceVolume", 1f);
            micDevice = PlayerPrefs.GetInt("set.micDevice", -1);
            fpsIndex = Wrap(PlayerPrefs.GetInt("set.fps", 2), FpsOptions.Length);
            pixelIndex = Wrap(PlayerPrefs.GetInt("set.pixel", 1), PixelOptionNames.Length);
            windowModeIndex = Wrap(PlayerPrefs.GetInt("set.window", 0), WindowModes.Length);
            // Default to the monitor's current resolution.
            var current = Screen.currentResolution;
            int nativeIndex = Array.FindLastIndex(Resolutions, r => r.width == current.width && r.height == current.height);
            resolutionIndex = Wrap(PlayerPrefs.GetInt("set.resolution", nativeIndex < 0 ? Resolutions.Length - 1 : nativeIndex), Resolutions.Length);
        }

        static void Save()
        {
            PlayerPrefs.SetFloat("set.sensitivity", sensitivity);
            PlayerPrefs.SetInt("set.invertY", invertY ? 1 : 0);
            PlayerPrefs.SetFloat("set.fov", fieldOfView);
            PlayerPrefs.SetFloat("set.brightness", brightness);
            PlayerPrefs.SetFloat("set.volume", masterVolume);
            PlayerPrefs.SetInt("set.vsync", vsync ? 1 : 0);
            PlayerPrefs.SetFloat("set.micSensitivity", micSensitivity);
            PlayerPrefs.SetFloat("set.voiceVolume", voiceVolume);
            PlayerPrefs.SetInt("set.micDevice", micDevice);
            PlayerPrefs.SetInt("set.fps", fpsIndex);
            PlayerPrefs.SetInt("set.pixel", pixelIndex);
            PlayerPrefs.SetInt("set.window", windowModeIndex);
            PlayerPrefs.SetInt("set.resolution", resolutionIndex);
            PlayerPrefs.Save();
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
