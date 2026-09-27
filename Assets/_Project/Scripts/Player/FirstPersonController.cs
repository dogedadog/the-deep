using TheDeep.Core;
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

        /// <summary>Why input is locked. Each reason is set and cleared by its own owner, so they don't undo each other.</summary>
        [System.Flags]
        public enum Lock { None = 0, Pause = 1, Terminal = 2, View = 4 }

        CharacterController controller;
        float pitch;
        float verticalVelocity;
        Lock locks;

        /// <summary>When true, movement and mouse look are ignored (paused, at the terminal, or not in first person).</summary>
        public bool InputLocked => locks != Lock.None;
        public bool Has(Lock reason) => (locks & reason) != 0;
        public void SetLock(Lock reason, bool on) => locks = on ? locks | reason : locks & ~reason;

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
                if (Controls.Held(GameAction.MoveForward)) move.y += 1;
                if (Controls.Held(GameAction.MoveBack)) move.y -= 1;
                if (Controls.Held(GameAction.MoveRight)) move.x += 1;
                if (Controls.Held(GameAction.MoveLeft)) move.x -= 1;
                sprint = Controls.Held(GameAction.Sprint);
            }

            // If the cursor got released (Esc in the Editor, alt-tab), clicking the game grabs it again.
            if (!InputLocked && mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);

            if (!InputLocked && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = GameSettings.AdjustLook(mouse.delta.ReadValue()) * lookSensitivity;
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
