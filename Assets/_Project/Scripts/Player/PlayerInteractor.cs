using TheDeep.Core;
using TheDeep.Core.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDeep.Player
{
    /// <summary>Looks for interactables under the crosshair and shows a "[E] ..." prompt.</summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] float range = 2.5f;
        [SerializeField] float aimRadius = 0.12f;

        GameObject crosshair;
        Text prompt;

        public FirstPersonController Controller { get; private set; }
        public Camera Camera => playerCamera;
        /// <summary>May be null if the camera isn't pixelated.</summary>
        public PixelatedCamera Pixelation { get; private set; }
        /// <summary>Camera to use for clicking world-space UI.</summary>
        public Camera EventCamera => Pixelation != null ? Pixelation.EventCamera : playerCamera;

        void Awake()
        {
            Controller = GetComponent<FirstPersonController>();
            Pixelation = playerCamera.GetComponent<PixelatedCamera>();
            BuildHud();
        }

        void Update()
        {
            if (Controller.InputLocked)
            {
                crosshair.SetActive(false);
                prompt.text = "";
                return;
            }

            crosshair.SetActive(true);
            IInteractable target = null;
            Transform cam = playerCamera.transform;
            // A thick ray, so you don't have to aim at the exact pixel of an object.
            if (Physics.SphereCast(cam.position, aimRadius, cam.forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Collide))
                target = hit.collider.GetComponentInParent<IInteractable>();

            if (target != null && target.CanInteract(this))
            {
                prompt.text = "[E] " + target.Prompt;
                if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    target.Interact(this);
            }
            else
            {
                prompt.text = "";
            }
        }

        void BuildHud()
        {
            var canvasGo = new GameObject("PlayerHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            crosshair = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            crosshair.transform.SetParent(canvasGo.transform, false);
            var ch = (RectTransform)crosshair.transform;
            ch.sizeDelta = new Vector2(6, 6);
            var chImage = crosshair.GetComponent<Image>();
            chImage.color = new Color(1f, 1f, 1f, 0.7f);
            chImage.raycastTarget = false;

            var promptGo = new GameObject("Prompt", typeof(RectTransform), typeof(Text));
            promptGo.transform.SetParent(canvasGo.transform, false);
            var pr = (RectTransform)promptGo.transform;
            pr.sizeDelta = new Vector2(600, 50);
            pr.anchoredPosition = new Vector2(0, -60);
            prompt = promptGo.GetComponent<Text>();
            prompt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            prompt.fontSize = 28;
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.color = new Color(0.85f, 0.95f, 0.9f);
            prompt.raycastTarget = false;
        }
    }
}
