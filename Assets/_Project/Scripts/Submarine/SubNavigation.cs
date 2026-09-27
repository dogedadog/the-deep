using System;
using System.Collections;
using TheDeep.Core;
using TheDeep.Player;
using TheDeep.Progression;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Submarine
{
    /// <summary>
    /// The sub hangs at one of several dive stations, each deeper than the last. The crew moves it
    /// from the NAV app once every diver is aboard and the suits are rated for the depth. Moving
    /// shifts the whole sub (and everyone inside) and changes the light and water around it.
    /// </summary>
    public class SubNavigation : NetworkBehaviour
    {
        [Serializable]
        public class Station
        {
            public string name;
            public Transform point;
            [Tooltip("Suit depth-rating upgrade level needed to dive here.")] public int requiredDepthLevel;
            [Tooltip("Faint light from above; almost none at the bottom of the shaft.")] public float downwelling = 0.5f;
            public Color fogColor = new(0.01f, 0.035f, 0.045f);
            public float fogDensity = 0.045f;
        }

        const float TravelSeconds = 4f;

        [SerializeField] Transform submarine;
        [SerializeField] Light downwelling;
        [SerializeField] Station[] stations;

        readonly NetworkVariable<int> station = new(0);
        CanvasGroup overlay;
        Text overlayText;

        public static SubNavigation Instance { get; private set; }
        public Station[] Stations => stations;
        public int Current => station.Value;
        public bool Travelling { get; private set; }

        /// <summary>Where the sub is right now (its origin: cabin floor, centre).</summary>
        public static Vector3 SubPosition => Instance != null && Instance.submarine != null ? Instance.submarine.position : Vector3.zero;

        public static float DepthOf(Station s) => WorldInfo.DepthAt(s.point.position.y);

        void Awake()
        {
            Instance = this;
            BuildOverlay();
        }

        public override void OnNetworkSpawn()
        {
            station.OnValueChanged += (from, to) => StartCoroutine(Travel(from, to));
            Snap(station.Value);
        }

        /// <summary>Null if the sub can go to <paramref name="target"/> now, otherwise the reason it can't.</summary>
        public string CanTravel(int target)
        {
            if (target == Current) return "SUB IS HERE";
            if (Travelling) return "IN TRANSIT";
            int level = CrewProgress.Instance != null ? CrewProgress.Instance.Level(UpgradeType.DepthRating) : 0;
            if (level < stations[target].requiredDepthLevel)
                return $"NEEDS DEPTH RATING L{stations[target].requiredDepthLevel}";
            foreach (var diver in FindObjectsByType<DiverController>(FindObjectsSortMode.None))
                if (diver.IsDiving) return "ALL DIVERS MUST BE ABOARD";
            return null;
        }

        [Rpc(SendTo.Server)]
        public void RequestTravelRpc(int target)
        {
            if (target >= 0 && target < stations.Length && CanTravel(target) == null) station.Value = target;
        }

        /// <summary>Server: back to the first station (the crew surfaced).</summary>
        public void ServerReturnToStart()
        {
            if (IsServer && station.Value != 0) station.Value = 0;
        }

        void Snap(int index)
        {
            submarine.position = stations[index].point.position;
            ApplyEnvironment(index);
        }

        void ApplyEnvironment(int index)
        {
            var s = stations[index];
            if (downwelling != null) downwelling.intensity = s.downwelling;
            RenderSettings.fogColor = s.fogColor;
            RenderSettings.fogDensity = s.fogDensity;
            SignalModel.SubCenter = submarine.position + Vector3.up * 1.3f;
        }

        IEnumerator Travel(int from, int to)
        {
            Travelling = true;
            var target = stations[to];
            bool down = DepthOf(target) > DepthOf(stations[from]);
            overlayText.text = $"{(down ? "DESCENDING" : "ASCENDING")} TO {target.name.ToUpperInvariant()}\n{DepthOf(target):0} M";

            yield return Fade(0f, 1f, 0.8f);
            yield return new WaitForSeconds(TravelSeconds * 0.3f);

            Vector3 delta = target.point.position - submarine.position;
            submarine.position = target.point.position;
            ApplyEnvironment(to);
            MoveLocalPlayer(delta);

            yield return new WaitForSeconds(TravelSeconds * 0.4f);
            yield return Fade(1f, 0f, 1.2f);
            Travelling = false;
        }

        /// <summary>Everyone aboard rides along: each client moves its own player.</summary>
        static void MoveLocalPlayer(Vector3 delta)
        {
            var local = PlayerNetwork.Local;
            if (local == null) return;
            var diver = local.GetComponent<DiverController>();
            if (diver != null && diver.IsDiving) return;
            var cc = local.GetComponent<CharacterController>();
            cc.enabled = false;
            local.GetComponent<NetworkTransform>().Teleport(local.transform.position + delta, local.transform.rotation, local.transform.localScale);
            cc.enabled = true;
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            overlay.gameObject.SetActive(true);
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                overlay.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
            overlay.alpha = to;
            if (to <= 0f) overlay.gameObject.SetActive(false);
        }

        void BuildOverlay()
        {
            var canvasGo = new GameObject("TravelOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            overlay = canvasGo.GetComponent<CanvasGroup>();
            overlay.blocksRaycasts = false;

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)black.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            black.GetComponent<Image>().color = Color.black;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            overlayText = textGo.GetComponent<Text>();
            overlayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overlayText.fontSize = 34;
            overlayText.fontStyle = FontStyle.Bold;
            overlayText.alignment = TextAnchor.MiddleCenter;
            overlayText.color = new Color(0.6f, 0.95f, 0.8f);
            canvasGo.SetActive(false);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
