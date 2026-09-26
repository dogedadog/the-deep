using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheDeep.Core
{
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Sprint,
        SwimUp, SwimDown, Interact, Scan, Transmit, ReelIn,
        Radio, Record, MuteMic, ExteriorView,
    }

    /// <summary>A key or a mouse button.</summary>
    [Serializable]
    public struct Binding
    {
        public Key Key;
        /// <summary>-1 = keyboard; 0 left, 1 right, 2 middle, 3 back, 4 forward.</summary>
        public int Mouse;

        public static Binding OfKey(Key key) => new() { Key = key, Mouse = -1 };
        public static Binding OfMouse(int button) => new() { Key = Key.None, Mouse = button };
    }

    /// <summary>
    /// Rebindable controls, saved per machine. Game code asks <see cref="Held"/>/<see cref="Pressed"/>
    /// for an action instead of reading keys directly. (Esc is fixed: pause/back.)
    /// </summary>
    public static class Controls
    {
        public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));

        static readonly Binding[] Defaults =
        {
            Binding.OfKey(Key.W), Binding.OfKey(Key.S), Binding.OfKey(Key.A), Binding.OfKey(Key.D), Binding.OfKey(Key.LeftShift),
            Binding.OfKey(Key.Space), Binding.OfKey(Key.LeftCtrl), Binding.OfKey(Key.E), Binding.OfMouse(0), Binding.OfKey(Key.T), Binding.OfKey(Key.R),
            Binding.OfKey(Key.V), Binding.OfKey(Key.F), Binding.OfKey(Key.M), Binding.OfKey(Key.F2),
        };

        static readonly string[] Names =
        {
            "Move forward", "Move back", "Move left", "Move right", "Sprint / swim fast",
            "Swim up", "Swim down", "Interact", "Scan (hold)", "Transmit data", "Reel in tether (hold)",
            "Radio talk (hold)", "Record camera (hold)", "Mute microphone", "Exterior camera (dev)",
        };

        static Binding[] current;

        public static event Action Changed;

        public static string Name(GameAction action) => Names[(int)action];
        public static Binding Get(GameAction action) => Bindings[(int)action];

        static Binding[] Bindings
        {
            get
            {
                if (current == null) Load();
                return current;
            }
        }

        public static bool Held(GameAction action)
        {
            var control = ControlFor(Get(action));
            return control != null && control.isPressed;
        }

        public static bool Pressed(GameAction action)
        {
            var control = ControlFor(Get(action));
            return control != null && control.wasPressedThisFrame;
        }

        public static bool Released(GameAction action)
        {
            var control = ControlFor(Get(action));
            return control != null && control.wasReleasedThisFrame;
        }

        static ButtonControl ControlFor(Binding binding)
        {
            if (binding.Mouse >= 0)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null) return null;
                return binding.Mouse switch
                {
                    0 => mouse.leftButton,
                    1 => mouse.rightButton,
                    2 => mouse.middleButton,
                    3 => mouse.backButton,
                    _ => mouse.forwardButton,
                };
            }
            var keyboard = Keyboard.current;
            return keyboard == null || binding.Key == Key.None ? null : keyboard[binding.Key];
        }

        public static string Label(Binding binding)
        {
            if (binding.Mouse >= 0)
                return binding.Mouse switch { 0 => "LEFT MOUSE", 1 => "RIGHT MOUSE", 2 => "MIDDLE MOUSE", 3 => "MOUSE 4", _ => "MOUSE 5" };
            return binding.Key switch
            {
                Key.None => "-",
                Key.LeftCtrl => "L-CTRL",
                Key.RightCtrl => "R-CTRL",
                Key.LeftShift => "L-SHIFT",
                Key.RightShift => "R-SHIFT",
                Key.LeftAlt => "L-ALT",
                Key.Space => "SPACE",
                _ => binding.Key.ToString().ToUpperInvariant(),
            };
        }

        public static string Label(GameAction action) => Label(Get(action));

        public static void Rebind(GameAction action, Binding binding)
        {
            Bindings[(int)action] = binding;
            Save();
            Changed?.Invoke();
        }

        public static void ResetAll()
        {
            current = (Binding[])Defaults.Clone();
            Save();
            Changed?.Invoke();
        }

        /// <summary>For the rebind screen: the first key or mouse button pressed this frame (Esc excluded).</summary>
        public static bool TryCapture(out Binding binding)
        {
            binding = default;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key == null || key.keyCode == Key.Escape || !key.wasPressedThisFrame) continue;
                    binding = Binding.OfKey(key.keyCode);
                    return true;
                }
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                ButtonControl[] buttons = { mouse.leftButton, mouse.rightButton, mouse.middleButton, mouse.backButton, mouse.forwardButton };
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (!buttons[i].wasPressedThisFrame) continue;
                    binding = Binding.OfMouse(i);
                    return true;
                }
            }
            return false;
        }

        static void Load()
        {
            current = (Binding[])Defaults.Clone();
            foreach (var action in All)
            {
                string saved = PlayerPrefs.GetString("bind." + action, "");
                if (saved.StartsWith("M:") && int.TryParse(saved.Substring(2), out int button))
                    current[(int)action] = Binding.OfMouse(button);
                else if (saved.StartsWith("K:") && Enum.TryParse(saved.Substring(2), out Key key))
                    current[(int)action] = Binding.OfKey(key);
            }
        }

        static void Save()
        {
            foreach (var action in All)
            {
                var b = current[(int)action];
                PlayerPrefs.SetString("bind." + action, b.Mouse >= 0 ? "M:" + b.Mouse : "K:" + b.Key);
            }
            PlayerPrefs.Save();
        }
    }
}
