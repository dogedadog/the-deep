using UnityEngine;
using UnityEngine.UI;
using TheDeep.Core;

namespace TheDeep.Core.Rendering
{
    /// <summary>
    /// Retro look: this camera renders into a small render texture (e.g. 360px tall) which is then
    /// stretched to the screen with hard pixel edges and a colour-reduce/dither shader.
    /// Also provides a full-resolution "event camera" so world-space UI (the terminal) still gets
    /// correct mouse positions.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelatedCamera : MonoBehaviour
    {
        [SerializeField] Material screenMaterial;
        [SerializeField, Range(4, 256)] float colorLevels = 40;
        [SerializeField, Range(0, 2)] float ditherStrength = 1f;

        Camera cam;
        Camera eventCamera;
        RenderTexture target;
        RawImage output;
        Material materialInstance;
        int? overrideHeight;

        /// <summary>Use this camera for UI raycasts; it matches the view but uses real screen pixels.</summary>
        public Camera EventCamera => eventCamera;

        /// <summary>Temporarily use at least this resolution (e.g. sharper while reading the terminal). Null to restore.</summary>
        public void SetOverrideHeight(int? height) => overrideHeight = height;

        /// <summary>Fixed field of view instead of the player's setting (e.g. so the terminal screen fits). Null to restore.</summary>
        public float? FovOverride { get; set; }

        void Awake()
        {
            cam = GetComponent<Camera>();

            var eventGo = new GameObject("UIEventCamera", typeof(Camera));
            eventGo.transform.SetParent(transform, false);
            eventCamera = eventGo.GetComponent<Camera>();
            eventCamera.enabled = false;
            eventCamera.cullingMask = 0;

            // Something has to draw to the actual screen: an empty camera that just clears to black,
            // with an overlay canvas on top showing the low-res image.
            var outputCamGo = new GameObject("PixelOutputCamera", typeof(Camera));
            outputCamGo.transform.SetParent(transform, false);
            var outputCam = outputCamGo.GetComponent<Camera>();
            outputCam.cullingMask = 0;
            outputCam.clearFlags = CameraClearFlags.SolidColor;
            outputCam.backgroundColor = Color.black;
            outputCam.depth = cam.depth + 1;

            var canvasGo = new GameObject("PixelOutput", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100; // HUD canvases draw on top of this

            var imageGo = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)imageGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            output = imageGo.GetComponent<RawImage>();
            output.raycastTarget = false;
            if (screenMaterial != null)
            {
                materialInstance = new Material(screenMaterial);
                output.material = materialInstance;
            }
        }

        void LateUpdate()
        {
            // Pixel size comes from the player's settings (0 = off, full resolution).
            int setting = GameSettings.PixelHeight > 0 ? GameSettings.PixelHeight : Screen.height;
            int height = Mathf.Clamp(Mathf.Max(overrideHeight ?? 0, setting), 64, Screen.height);
            cam.fieldOfView = FovOverride ?? GameSettings.FieldOfView;
            int width = Mathf.Max(1, Mathf.RoundToInt(height * (float)Screen.width / Screen.height));
            if (target == null || target.width != width || target.height != height)
                Recreate(width, height);

            eventCamera.fieldOfView = cam.fieldOfView;
            eventCamera.nearClipPlane = cam.nearClipPlane;
            eventCamera.farClipPlane = cam.farClipPlane;

            if (materialInstance != null)
            {
                materialInstance.SetFloat("_ColorLevels", colorLevels);
                materialInstance.SetFloat("_DitherStrength", ditherStrength);
            }
        }

        void Recreate(int width, int height)
        {
            cam.targetTexture = null;
            if (target != null) target.Release();
            target = new RenderTexture(width, height, 24) { filterMode = FilterMode.Point, name = "PixelatedView" };
            target.Create();
            cam.targetTexture = target;
            output.texture = target;
        }

        void OnDestroy()
        {
            if (cam != null) cam.targetTexture = null;
            if (target != null) target.Release();
            if (materialInstance != null) Destroy(materialInstance);
        }
    }
}
