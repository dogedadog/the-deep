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
        const float FadeOutSeconds = 0.8f;
        const float FadeInSeconds = 1.2f;
        const float TripSeconds = FadeOutSeconds + TravelSeconds * 0.3f + TravelSeconds * 0.4f + FadeInSeconds;
        const float JumpAt = FadeOutSeconds + TravelSeconds * 0.3f;
        const float FadeInAt = TripSeconds - FadeInSeconds;

        [SerializeField] Transform submarine;
        [SerializeField] Light downwelling;
        [SerializeField] Station[] stations;

        readonly NetworkVariable<int> station = new(0);
        CanvasGroup overlay;
        Text overlayText;
        Coroutine trip;
        // The trip on the overlay: a heading and a depth counter ticking from one station to the next.
        string tripHeading;
        float tripFromDepth, tripToDepth, tripDepth;
        int shownDepth;

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
            // In-scene objects keep their values over LEAVE -> HOST, so every new session starts at the top.
            // Reset before subscribing: a server write fires OnValueChanged at once and would play a trip.
            if (IsServer) station.Value = 0;
            station.OnValueChanged += OnStationChanged;
            Snap(station.Value);
        }

        public override void OnNetworkDespawn()
        {
            station.OnValueChanged -= OnStationChanged;
            StopAllCoroutines();
            trip = null;
            Travelling = false;
            overlay.alpha = 0f;
            overlay.gameObject.SetActive(false);
        }

        void OnStationChanged(int from, int to)
        {
            // An order mid-trip (END EXPEDITION recalling the sub) turns the running trip around under
            // the black overlay, so only one trip ever drives the overlay and Travelling.
            if (trip != null) StopCoroutine(trip);
            trip = StartCoroutine(Travel(from, to));
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
            {
                if (!diver.IsDiving) continue;
                // The dead stay 'in the water' until the expedition ends, but they don't hold the sub.
                var health = diver.GetComponent<DiverHealth>();
                if (health != null && health.IsDead) continue;
                var crew = diver.GetComponent<PlayerNetwork>();
                return crew != null ? $"D{crew.CrewNumber} IS STILL OUTSIDE" : "ALL DIVERS MUST BE ABOARD";
            }
            return null;
        }

        [Rpc(SendTo.Server)]
        public void RequestTravelRpc(int target)
        {
            if (target >= 0 && target < stations.Length && CanTravel(target) == null) station.Value = target;
        }

        /// <summary>Server: back to the first station (the crew surfaced). A trip under way turns around.</summary>
        public void ServerReturnToStart()
        {
            if (IsServer && station.Value != 0) station.Value = 0;
        }

        void Snap(int index)
        {
            Vector3 was = submarine.position;
            submarine.position = stations[index].point.position;
            ApplyEnvironment(index);
            // Late joiners and rehosts can find the sub away from where its particles prewarmed.
            if ((submarine.position - was).sqrMagnitude > 1f) ReseedParticles();
        }

        void ApplyEnvironment(int index)
        {
            var s = stations[index];
            if (downwelling != null) downwelling.intensity = s.downwelling;
            RenderSettings.fogColor = s.fogColor;
            RenderSettings.fogDensity = s.fogDensity;
            SignalModel.SubCenter = submarine.position + Vector3.up * 1.3f;
        }

        /// <summary>
        /// Fade to black, jump, fade back in. One flat loop rather than nested waits, so stopping the
        /// trip stops all of it (a nested coroutine would run on by itself).
        /// </summary>
        IEnumerator Travel(int from, int to)
        {
            // A trip turned around mid-way counts on from the depth already on the overlay.
            tripFromDepth = Travelling ? tripDepth : DepthOf(stations[from]);
            Travelling = true;
            var target = stations[to];
            tripToDepth = DepthOf(target);
            tripDepth = tripFromDepth;
            tripHeading = $"{(tripToDepth > tripFromDepth ? "DESCENDING" : "ASCENDING")} TO {target.name.ToUpperInvariant()}";
            shownDepth = int.MinValue;
            // The hull lurches into motion and the faulty lamps stutter.
            foreach (var lamp in FindObjectsByType<FlickerLight>(FindObjectsSortMode.None)) lamp.Burst(1.2f);

            float startAlpha = overlay.alpha;
            overlay.gameObject.SetActive(true);
            bool jumped = false;
            for (float t = 0f; t < TripSeconds; t += Time.deltaTime)
            {
                // Fully black on both sides of the jump: crewmates' NetworkTransforms arrive a few ticks later.
                if (!jumped && t >= JumpAt)
                {
                    Jump(to);
                    jumped = true;
                }
                overlay.alpha = t < FadeOutSeconds ? Mathf.Lerp(startAlpha, 1f, t / FadeOutSeconds)
                    : t < FadeInAt ? 1f : 1f - (t - FadeInAt) / FadeInSeconds;
                TickCounter(t);
                yield return null;
            }
            if (!jumped) Jump(to);
            overlay.alpha = 0f;
            overlay.gameObject.SetActive(false);
            Travelling = false;
            trip = null;
        }

        void Jump(int to)
        {
            Vector3 point = stations[to].point.position;
            Vector3 delta = point - submarine.position;
            submarine.position = point;
            ReseedParticles();
            ApplyEnvironment(to);
            MoveLocalPlayer(delta);
        }

        /// <summary>Everyone aboard rides along: each client moves its own player.</summary>
        static void MoveLocalPlayer(Vector3 delta)
        {
            var local = PlayerNetwork.Local;
            if (local == null) return;
            var diver = local.GetComponent<DiverController>();
            if (diver != null && diver.IsDiving)
            {
                // Someone who dropped out just before GO is pulled aboard (CabinEntry has already moved
                // with the sub). The dead stay with their body.
                var health = local.GetComponent<DiverHealth>();
                bool dead = health != null && health.IsDead;
                if (!dead && DiveHatch.CabinEntry != null) diver.ExitWater(DiveHatch.CabinEntry);
                return;
            }
            var cc = local.GetComponent<CharacterController>();
            cc.enabled = false;
            local.GetComponent<NetworkTransform>().Teleport(local.transform.position + delta, local.transform.rotation, local.transform.localScale);
            cc.enabled = true;
        }

        /// <summary>
        /// The sub's marine snow, dust and drips simulate in world space, so they stay behind when it
        /// jumps: fill them in again around its new position.
        /// </summary>
        void ReseedParticles()
        {
            foreach (var ps in submarine.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                // Only running ambient loops; one-shot or stopped effects are left alone.
                if (main.simulationSpace != ParticleSystemSimulationSpace.World || !main.loop || !ps.isPlaying) continue;
                ps.Clear(false);
                // Fixed steps keep the ages spread out, so they don't all expire together a lifetime from now.
                ps.Simulate(main.startLifetime.constantMax, false, true, true);
                ps.Play(false);
            }
        }

        /// <summary>Overlay text: where the sub is heading and the depth it has reached.</summary>
        void TickCounter(float elapsed)
        {
            tripDepth = Mathf.Lerp(tripFromDepth, tripToDepth, Mathf.SmoothStep(0f, 1f, elapsed / TripSeconds));
            int depth = Mathf.RoundToInt(tripDepth);
            if (depth == shownDepth) return;
            shownDepth = depth;
            overlayText.text = $"{tripHeading}\n{depth:0000} M";
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
