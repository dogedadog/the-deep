using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TheDeep.Core;

namespace TheDeep.Core.Rendering
{
    /// <summary>
    /// Retro look: this camera renders into a small render texture (e.g. 360px tall) which is then
    /// stretched to the screen with hard pixel edges and a colour-reduce/dither shader.
    /// Also provides a full-resolution "event camera" so world-space UI (the terminal) still gets
    /// correct mouse positions.
    /// Open water clears to the fog colour, and the far plane is pulled in to where the fog hides everything.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelatedCamera : MonoBehaviour
    {
        /// <summary>Feature-less renderer for the output camera; the scene builder adds it to the URP asset.</summary>
        public const string OutputRendererName = "PC_Renderer_Output";

        [SerializeField] Material screenMaterial;
        [SerializeField, Range(4, 256)] float colorLevels = 40;
        [SerializeField, Range(0, 2)] float ditherStrength = 1f;

        Camera cam;
        Camera eventCamera;
        RenderTexture target;
        RawImage output;
        Material materialInstance;
        int? overrideHeight;
        float baseFarClip;

        /// <summary>Use this camera for UI raycasts; it matches the view but uses real screen pixels.</summary>
        public Camera EventCamera => eventCamera;

        /// <summary>Temporarily use at least this resolution (e.g. sharper while reading the terminal). Null to restore.</summary>
        public void SetOverrideHeight(int? height) => overrideHeight = height;

        /// <summary>Fixed field of view instead of the player's setting (e.g. so the terminal screen fits). Null to restore.</summary>
        public float? FovOverride { get; set; }

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseFarClip = cam.farClipPlane;

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
            // It draws nothing, so skip the HDR intermediate target, shadows, post and depth/colour copies.
            outputCam.allowHDR = false;
            outputCam.allowMSAA = false;
            outputCam.useOcclusionCulling = false;
            var outputData = outputCam.GetUniversalAdditionalCameraData();
            outputData.renderShadows = false;
            outputData.renderPostProcessing = false;
            outputData.requiresColorOption = CameraOverrideOption.Off;
            outputData.requiresDepthOption = CameraOverrideOption.Off;
            // The default renderer would also run its SSAO for this empty camera; use the bare one if it's there.
            int outputRenderer = RendererIndex(OutputRendererName);
            if (outputRenderer >= 0) outputData.SetRenderer(outputRenderer);

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
            MatchFog();

            eventCamera.fieldOfView = cam.fieldOfView;
            eventCamera.nearClipPlane = cam.nearClipPlane;
            eventCamera.farClipPlane = cam.farClipPlane;

            if (materialInstance != null)
            {
                materialInstance.SetFloat("_ColorLevels", colorLevels);
                materialInstance.SetFloat("_DitherStrength", ditherStrength);
            }
        }

        /// <summary>
        /// Empty water clears to the fog colour, so it matches fully fogged rock (and follows the station's
        /// fog). Past about 4 / density the exponential-squared fog has hidden everything, so stop drawing there.
        /// </summary>
        void MatchFog()
        {
            if (cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = RenderSettings.fogColor;
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared)
                cam.farClipPlane = Mathf.Min(baseFarClip, Mathf.Clamp(4f / Mathf.Max(RenderSettings.fogDensity, 0.001f), 60f, 1000f));
            else
                cam.farClipPlane = baseFarClip;
        }

        /// <summary>Index of the named renderer in the active URP asset, or -1 if it has none by that name.</summary>
        static int RendererIndex(string rendererName)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null) return -1;
            var renderers = asset.rendererDataList;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].name == rendererName) return i;
            return -1;
        }

        void Recreate(int width, int height)
        {
            cam.targetTexture = null;
            ReleaseTarget();
            target = new RenderTexture(width, height, 24) { filterMode = FilterMode.Point, name = "PixelatedView" };
            target.Create();
            cam.targetTexture = target;
            output.texture = target;
        }

        void ReleaseTarget()
        {
            if (target == null) return;
            target.Release();
            Destroy(target);
            target = null;
        }

        void OnDestroy()
        {
            if (cam != null) cam.targetTexture = null;
            ReleaseTarget();
            if (materialInstance != null) Destroy(materialInstance);
        }
    }
}
