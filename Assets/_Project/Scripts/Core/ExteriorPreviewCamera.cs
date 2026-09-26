using TheDeep.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDeep.Core
{
    /// <summary>
    /// Camera orbiting the submarine outside. It's the backdrop of the title menu, and a dev tool
    /// in-game: press V to swap your view to it (mouse to look around, scroll to zoom).
    /// </summary>
    public class ExteriorPreviewCamera : MonoBehaviour
    {
        [SerializeField] GameObject previewRig;
        [SerializeField] Transform orbitCenter;
        [SerializeField] float distance = 14f;
        [SerializeField] float autoSpinDegreesPerSecond = 5f;

        float yaw = 210f;
        float pitch = 12f;
        bool menuMode = true;

        bool Previewing => previewRig.activeSelf;

        /// <summary>While the title menu is up: always show the orbit camera, slow auto-spin, no input.</summary>
        public bool MenuMode
        {
            get => menuMode;
            set
            {
                menuMode = value;
                previewRig.SetActive(value);
            }
        }

        void Update()
        {
            if (!menuMode) HandleToggle();
            if (!Previewing) return;

            var mouse = Mouse.current;
            if (!menuMode && mouse != null)
            {
                Vector2 delta = GameSettings.AdjustLook(mouse.delta.ReadValue()) * 0.1f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -60f, 80f);
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f, 6f, 40f);
            }
            yaw += autoSpinDegreesPerSecond * Time.deltaTime;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            previewRig.transform.SetPositionAndRotation(orbitCenter.position - rot * Vector3.forward * distance, rot);
        }

        void HandleToggle()
        {
            var keyboard = Keyboard.current;
            var player = PlayerNetwork.Local;
            if (keyboard == null || player == null || !keyboard.vKey.wasPressedThisFrame) return;
            // Don't hijack the view while the player is busy (terminal, pause menu).
            if (!Previewing && player.Controller.InputLocked) return;

            bool preview = !Previewing;
            previewRig.SetActive(preview);
            player.SetFirstPersonView(!preview);
            FirstPersonController.SetCursorLocked(true);
        }
    }
}
