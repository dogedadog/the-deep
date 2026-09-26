using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField, Tooltip("Vertical resolution of the game image. Lower = chunkier pixels.")]
        int pixelHeight = 360;
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

        /// <summary>Temporarily use a different resolution (e.g. sharper while reading the terminal). Null to restore.</summary>
        public void SetOverrideHeight(int? height) => overrideHeight = height;

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
            int height = Mathf.Clamp(overrideHeight ?? pixelHeight, 64, Screen.height);
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
