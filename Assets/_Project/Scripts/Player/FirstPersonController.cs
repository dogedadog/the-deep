using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDeep.Player
{
    /// <summary>Simple walking first-person controller used inside the submarine.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float walkSpeed = 2.5f;
        [SerializeField] float sprintSpeed = 4.5f;
        [SerializeField] float lookSensitivity = 0.08f;
        [SerializeField] float gravity = -20f;

        CharacterController controller;
        float pitch;
        float verticalVelocity;

        /// <summary>When true, movement and mouse look are ignored (e.g. while using the terminal).</summary>
        public bool InputLocked { get; set; }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Awake() => controller = GetComponent<CharacterController>();

        void Start() => SetCursorLocked(true);

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 move = Vector2.zero;
            bool sprint = false;

            if (!InputLocked && keyboard != null)
            {
                if (keyboard.wKey.isPressed) move.y += 1;
                if (keyboard.sKey.isPressed) move.y -= 1;
                if (keyboard.dKey.isPressed) move.x += 1;
                if (keyboard.aKey.isPressed) move.x -= 1;
                sprint = keyboard.leftShiftKey.isPressed;
            }

            if (!InputLocked && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, delta.x, 0f);
                pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);
                head.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = (transform.right * move.x + transform.forward * move.y).normalized
                               * (sprint ? sprintSpeed : walkSpeed);
            velocity.y = verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
