using TheDeep.Core;
using TheDeep.Data;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.Submarine;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI
{
    /// <summary>
    /// One amber line at the top-left telling a new crew what to do next (suit up, scan, radio in,
    /// LOG, SUBMIT, spend, go deeper). Picked from synced state only, so it fits each player's own
    /// situation, and it goes away after the first few expeditions. Creates itself at startup.
    /// </summary>
    public class ObjectiveHud : MonoBehaviour
    {
        const int LastGuidedExpedition = 3;
        const float CheckInterval = 0.25f;
        const float FlashSeconds = 1f;
        static readonly Color Amber = new(1f, 0.816f, 0.376f); // #ffd060
        static readonly Color Flash = new(1f, 0.97f, 0.85f);

        Text line;
        float nextCheck;
        float flashUntil;
        string shown;

        PlayerNetwork player;
        DiverController diver;
        DiverScanner scanner;
        DiverHealth health;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("ObjectiveHud");
            DontDestroyOnLoad(go);
            go.AddComponent<ObjectiveHud>();
        }

        void Awake() => BuildHud();

        void Update()
        {
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckInterval;
                string text = PickLine();
                line.enabled = text != null;
                if (text != null && text != shown)
                {
                    shown = text;
                    line.text = text;
                    flashUntil = Time.unscaledTime + FlashSeconds;
                }
            }
            if (line.enabled)
                line.color = Color.Lerp(Amber, Flash, Mathf.Clamp01((flashUntil - Time.unscaledTime) / FlashSeconds));
        }

        /// <summary>The next step for the local player, or null to show nothing.</summary>
        string PickLine()
        {
            var local = PlayerNetwork.Local;
            if (local == null || local.Controller == null || local.Controller.InputLocked) return null;
            var progress = CrewProgress.Instance;
            if (progress == null || !progress.IsSpawned || progress.Expedition > LastGuidedExpedition) return null;
            var state = ExpeditionState.Instance;
            if (state == null || !state.IsSpawned) return null;

            if (local != player)
            {
                player = local;
                diver = local.GetComponent<DiverController>();
                scanner = local.GetComponent<DiverScanner>();
                health = local.GetComponent<DiverHealth>();
            }
            if (health != null && health.IsDead) return null;

            if (diver != null && diver.IsDiving)
            {
                return scanner != null && scanner.HeldCount > 0
                    ? $"Radio your data: press {Controls.Label(GameAction.Transmit)} (closer to the sub = cleaner)"
                    : $"Find something glowing below and HOLD {Controls.Label(GameAction.Scan)} on it";
            }

            bool pending = false, corrupted = false, logged = false;
            foreach (var p in state.Packets)
            {
                if (p.Status == PacketStatus.Pending) pending = true;
                else if (p.Status == PacketStatus.Corrupted) corrupted = true;
                else if (p.Status == PacketStatus.Logged) logged = true;
            }
            if (pending || corrupted) return $"New data!  TERMINAL (bow, port side) > COMMS / DATA > {(pending ? "LOG" : "REPAIR")}";
            if (logged) return "COMMS / DATA: SUBMIT LOGGED DATA for credits";
            if (CanAffordUpgrade(progress, state.Credits)) return "Spend credits: TERMINAL > BALANCE";
            if (CanGoDeeper()) return "Suits rated deeper: TERMINAL > NAV / DEPTH";
            if (!AnyoneDiving()) return $"Suit up: DIVE HATCH (stern floor) - look down at it and press {Controls.Label(GameAction.Interact)}";
            return "Watch your divers: TERMINAL > DIVER MAP / RADIO";
        }

        static bool CanAffordUpgrade(CrewProgress progress, int credits)
        {
            for (int i = 0; i < UpgradeCatalog.Count; i++)
            {
                var type = (UpgradeType)i;
                if (!UpgradeCatalog.IsAvailable(type)) continue;
                int cost = UpgradeCatalog.Cost(type, progress.Level(type));
                if (cost >= 0 && cost <= credits) return true;
            }
            return false;
        }

        /// <summary>The suits are already rated for a station below the current one.</summary>
        static bool CanGoDeeper()
        {
            var nav = SubNavigation.Instance;
            if (nav == null || !nav.IsSpawned || nav.Stations == null) return false;
            for (int i = nav.Current + 1; i < nav.Stations.Length; i++)
                if (nav.CanTravel(i) == null) return true;
            return false;
        }

        static bool AnyoneDiving()
        {
            foreach (var d in FindObjectsByType<DiverController>(FindObjectsSortMode.None))
                if (d.IsSpawned && d.IsDiving) return true;
            return false;
        }

        void BuildHud()
        {
            var canvasGo = new GameObject("ObjectiveHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Objective", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(28f, -70f);
            rt.sizeDelta = new Vector2(1000f, 60f);
            line = go.GetComponent<Text>();
            line.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            line.fontSize = 20;
            line.fontStyle = FontStyle.Bold;
            line.color = Amber;
            line.alignment = TextAnchor.UpperLeft;
            line.raycastTarget = false;
            line.enabled = false;
        }
    }
}
