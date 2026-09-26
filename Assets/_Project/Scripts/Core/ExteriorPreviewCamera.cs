using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDeep.Core
{
    /// <summary>
    /// Dev tool: press V to swap between the player and a camera orbiting the submarine outside.
    /// Mouse to look around, scroll to zoom. Will be replaced by real diver gameplay in step 2.
    /// </summary>
    public class ExteriorPreviewCamera : MonoBehaviour
    {
        [SerializeField] GameObject player;
        [SerializeField] GameObject previewRig;
        [SerializeField] Transform orbitCenter;
        [SerializeField] float distance = 14f;
        [SerializeField] float autoSpinDegreesPerSecond = 5f;

        float yaw = 210f;
        float pitch = 12f;

        bool Previewing => previewRig.activeSelf;

        void Start() => previewRig.SetActive(false);

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame)
            {
                bool preview = !Previewing;
                previewRig.SetActive(preview);
                player.SetActive(!preview);
                Player.FirstPersonController.SetCursorLocked(true);
            }
            if (!Previewing) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * 0.1f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -60f, 80f);
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f, 6f, 40f);
            }
            yaw += autoSpinDegreesPerSecond * Time.deltaTime;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            previewRig.transform.SetPositionAndRotation(orbitCenter.position - rot * Vector3.forward * distance, rot);
        }
    }
}
