using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// A waterproof camera bolted to the hull. Off by default; the terminal's camera app switches
    /// one on at a time and points it at its feed texture. The tally light glows while it's live,
    /// but the camera only renders while someone can actually see the monitor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SubCamera : MonoBehaviour
    {
        [SerializeField] int number = 1;
        [SerializeField] string label = "BOW";
        [SerializeField] Renderer tallyLight;

        Camera cam;

        public int Number => number;
        public string Label => label;

        void Awake()
        {
            cam = GetComponent<Camera>();
            SetLive(false, null);
        }

        public void SetLive(bool live, RenderTexture target)
        {
            if (cam == null) cam = GetComponent<Camera>();
            cam.targetTexture = live ? target : null;
            cam.enabled = live;
            if (tallyLight != null) tallyLight.enabled = live;
        }

        /// <summary>Render into the feed or hold its last frame. Only a live camera (one with a feed) can render.</summary>
        public void Render(bool on)
        {
            if (cam == null) cam = GetComponent<Camera>();
            bool render = on && cam.targetTexture != null;
            if (cam.enabled != render) cam.enabled = render;
        }

        void LateUpdate()
        {
            // The fog changes per station; clearing to it keeps open water from looking lighter than the rock.
            if (cam != null && cam.enabled) cam.backgroundColor = RenderSettings.fogColor;
        }
    }
}
