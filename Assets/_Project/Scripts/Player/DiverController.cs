using System;
using TheDeep.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDeep.Player
{
    /// <summary>
    /// Swimming outside the sub. While diving the player is a physics body (water drag, slight
    /// negative buoyancy) instead of a walking CharacterController; the headlamp turns on for everyone.
    /// WASD swims where you look, Space/Ctrl rise/sink, Shift swims harder.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DiverController : NetworkBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Light headlamp;
        [SerializeField] float swimAcceleration = 5f;
        [SerializeField] float boostAcceleration = 9f;
        [SerializeField] float verticalAcceleration = 4f;
        [SerializeField, Tooltip("Slight negative buoyancy, m/s^2.")] float sinkAcceleration = 0.25f;
        [SerializeField] float lookSensitivity = 0.08f;

        readonly NetworkVariable<bool> diving = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Rigidbody rb;
        CharacterController walker;
        CapsuleCollider swimmer;
        FirstPersonController walking;
        Vector3 swimInput;
        Vector3 externalForce;
        float pitch;
        Text hud;

        public bool IsDiving => diving.Value;
        public Rigidbody Body => rb;
        /// <summary>Raised on every client when this player enters (true) or leaves (false) the water.</summary>
        public event Action<bool> DivingChanged;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            walker = GetComponent<CharacterController>();
            swimmer = GetComponent<CapsuleCollider>();
            walking = GetComponent<FirstPersonController>();
        }

        public override void OnNetworkSpawn()
        {
            diving.OnValueChanged += (_, now) => Apply(now);
            Apply(diving.Value);
            if (IsOwner) BuildHud();
        }

        /// <summary>Owner only: drop into the water at <paramref name="point"/>.</summary>
        public void EnterWater(Transform point)
        {
            if (!IsOwner || diving.Value) return;
            diving.Value = true;
            Apply(true);
            Teleport(point);
            float p = head.localEulerAngles.x;
            pitch = p > 180f ? p - 360f : p;
        }

        /// <summary>Owner only: climb back into the cabin at <paramref name="point"/>.</summary>
        public void ExitWater(Transform point)
        {
            if (!IsOwner || !diving.Value) return;
            Teleport(point);
            diving.Value = false;
            Apply(false);
        }

        /// <summary>Extra force this physics step (e.g. the tether pulling), in Newtons.</summary>
        public void AddExternalForce(Vector3 force) => externalForce += force;

        void Apply(bool inWater)
        {
            headlamp.enabled = inWater;
            walker.enabled = !inWater;
            swimmer.enabled = inWater;
            if (IsOwner)
            {
                walking.enabled = !inWater;
                if (!inWater && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = !inWater;
            }
            else
            {
                rb.isKinematic = true; // remote players are moved by their NetworkTransform
            }
            if (hud != null) hud.gameObject.SetActive(inWater);
            DivingChanged?.Invoke(inWater);
        }

        void Teleport(Transform point)
        {
            var rot = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
            rb.position = point.position;
            rb.rotation = rot;
            transform.SetPositionAndRotation(point.position, rot);
            GetComponent<NetworkTransform>().Teleport(point.position, rot, transform.localScale);
        }

        void Update()
        {
            if (!IsOwner || !diving.Value) return;
            UpdateHud();

            swimInput = Vector3.zero;
            if (walking.InputLocked) return; // paused, or looking through the exterior camera

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, delta.x, 0f));
                pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                FirstPersonController.SetCursorLocked(true);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += head.forward;
            if (keyboard.sKey.isPressed) move -= head.forward;
            if (keyboard.dKey.isPressed) move += head.right;
            if (keyboard.aKey.isPressed) move -= head.right;
            float accel = keyboard.leftShiftKey.isPressed ? boostAcceleration : swimAcceleration;
            swimInput = Vector3.ClampMagnitude(move, 1f) * accel;
            if (keyboard.spaceKey.isPressed) swimInput += Vector3.up * verticalAcceleration;
            if (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed) swimInput += Vector3.down * verticalAcceleration;
        }

        void FixedUpdate()
        {
            if (!IsOwner || !diving.Value)
            {
                externalForce = Vector3.zero;
                return;
            }
            rb.AddForce(swimInput + Vector3.down * sinkAcceleration, ForceMode.Acceleration);
            rb.AddForce(externalForce, ForceMode.Force);
            externalForce = Vector3.zero;
        }

        // ------------------------------------------------------------------ HUD

        /// <summary>Extra lines other systems can show on the diver HUD (e.g. the tether).</summary>
        public Func<string> HudExtra;

        void BuildHud()
        {
            var canvasGo = new GameObject("DiverHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Readout", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(28, 24);
            rt.sizeDelta = new Vector2(700, 160);
            hud = go.GetComponent<Text>();
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.fontSize = 22;
            hud.fontStyle = FontStyle.Bold;
            hud.color = new Color(0.6f, 1f, 0.75f, 0.9f);
            hud.alignment = TextAnchor.LowerLeft;
            hud.raycastTarget = false;
            hud.gameObject.SetActive(diving.Value);
        }

        void UpdateHud()
        {
            if (hud == null) return;
            Vector3 f = head.forward;
            float heading = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
            string extra = HudExtra != null ? "\n" + HudExtra() : "";
            hud.text = $"DEPTH {WorldInfo.DepthAt(transform.position.y):0000.0} M    HDG {heading:000}°    SPD {rb.linearVelocity.magnitude:0.0} M/S{extra}\n" +
                       "<size=15>WASD swim   SPACE/CTRL up/down   SHIFT fast   E under the hatch to climb aboard</size>";
        }
    }
}
