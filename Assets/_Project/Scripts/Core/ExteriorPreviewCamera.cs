using System.Collections.Generic;
using TheDeep.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDeep.Core
{
    /// <summary>
    /// Camera orbiting the submarine outside. It's the backdrop of the title menu, and a dev tool
    /// in-game: press the Exterior camera key (F2) to swap your view to it (mouse to look around, scroll to zoom).
    /// Dead players spectate through it: their own body, or (Interact) any crewmate still diving.
    /// </summary>
    public class ExteriorPreviewCamera : MonoBehaviour
    {
        const float BodyDistance = 9f, CrewmateDistance = 6f;

        [SerializeField] GameObject previewRig;
        [SerializeField] Transform orbitCenter;
        [SerializeField] float distance = 14f;
        [SerializeField] float autoSpinDegreesPerSecond = 5f;

        float yaw = 210f;
        float pitch = 12f;
        bool menuMode = true;

        bool Previewing => previewRig.activeSelf;

        /// <summary>While the title menu is up: always show the orbit camera, slow auto-spin, no input.</summary>
        public bool MenuMode
        {
            get => menuMode;
            set
            {
                menuMode = value;
                previewRig.SetActive(value);
            }
        }

        bool spectating;
        Transform spectateTarget, ownBody;
        PlayerNetwork watched; // the crewmate being watched; null while on your own body
        readonly List<PlayerNetwork> crewmates = new();
        GameObject spectateHud;
        Text spectateLabel;
        int shownCrew = -1;
        bool shownCanCycle;

        void Awake() => BuildSpectateHud();

        /// <summary>Dead players watch from outside, orbiting where they died.</summary>
        public void Spectate(Transform target)
        {
            bool fresh = !spectating || ownBody != target;
            spectating = true;
            ownBody = target;
            if (fresh) Watch(null);
            shownCrew = -1;
            previewRig.SetActive(true);
        }

        public void StopSpectating()
        {
            if (!spectating) return;
            spectating = false;
            spectateTarget = ownBody = null;
            watched = null;
            distance = 14f;
            previewRig.SetActive(menuMode);
            spectateHud.SetActive(false);
        }

        void Watch(PlayerNetwork crewmate)
        {
            watched = crewmate;
            spectateTarget = crewmate != null ? crewmate.transform : ownBody;
            distance = crewmate != null ? CrewmateDistance : BodyDistance;
        }

        void Update()
        {
            if (spectating) UpdateSpectating();
            else if (!menuMode) HandleToggle();
            if (!Previewing) return;

            var mouse = Mouse.current;
            // Only while the cursor is captured, so moving it to the pause menu doesn't spin the view.
            if (!menuMode && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = GameSettings.AdjustLook(mouse.delta.ReadValue()) * 0.1f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -60f, 80f);
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f, 6f, 40f);
            }
            // The cursor got released (alt-tab, the Editor): clicking grabs it again, but not a click on the pause menu.
            else if (!menuMode && mouse != null && mouse.leftButton.wasPressedThisFrame && CanGrabCursor())
            {
                FirstPersonController.SetCursorLocked(true);
            }
            yaw += autoSpinDegreesPerSecond * Time.deltaTime;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 center = spectateTarget != null ? spectateTarget.position + Vector3.up : orbitCenter.position;
            previewRig.transform.SetPositionAndRotation(center - rot * Vector3.forward * distance, rot);
        }

        void UpdateSpectating()
        {
            // Our own player is gone (we left the session): back to the plain menu orbit.
            if (ownBody == null)
            {
                StopSpectating();
                return;
            }

            FindCrewmates();
            // The crewmate we watch climbed aboard, died or left: back to our own body.
            if (spectateTarget != ownBody && !crewmates.Contains(watched)) Watch(null);

            var local = PlayerNetwork.Local;
            bool paused = local != null && local.Controller.Has(FirstPersonController.Lock.Pause);
            // Cycle crewmate by crewmate, then back to your own body.
            if (!paused && crewmates.Count > 0 && Controls.Pressed(GameAction.Interact))
            {
                int next = crewmates.IndexOf(watched) + 1;
                Watch(next < crewmates.Count ? crewmates[next] : null);
            }

            // Rebuild the label only when it changes (and after a pause, where the key may have been rebound).
            int crew = watched != null ? watched.CrewNumber : 0;
            bool canCycle = crewmates.Count > 0;
            if (paused) shownCrew = -1;
            else if (crew != shownCrew || canCycle != shownCanCycle)
            {
                shownCrew = crew;
                shownCanCycle = canCycle;
                string hint = canCycle ? $"  -  [{Controls.Label(GameAction.Interact)}] " : null;
                spectateLabel.text = crew > 0
                    ? $"SPECTATING D{crew}" + (canCycle ? hint + "NEXT" : "")
                    : "YOUR BODY" + (canCycle ? hint + "WATCH A CREWMATE" : "");
            }
            spectateHud.SetActive(!menuMode && !paused);
        }

        /// <summary>Crewmates you can watch: still diving and alive, in crew order.</summary>
        void FindCrewmates()
        {
            crewmates.Clear();
            var all = PlayerNetwork.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p == PlayerNetwork.Local || p.transform == ownBody) continue;
                var diver = p.GetComponent<DiverController>();
                var health = p.GetComponent<DiverHealth>();
                if (diver != null && diver.IsDiving && (health == null || !health.IsDead)) crewmates.Add(p);
            }
            if (crewmates.Count > 1) crewmates.Sort((a, b) => a.CrewNumber.CompareTo(b.CrewNumber));
        }

        static bool CanGrabCursor()
        {
            var local = PlayerNetwork.Local;
            return local != null && !local.Controller.Has(FirstPersonController.Lock.Pause | FirstPersonController.Lock.Terminal);
        }

        void HandleToggle()
        {
            var keyboard = Keyboard.current;
            var player = PlayerNetwork.Local;
            if (keyboard == null || player == null || !Controls.Pressed(GameAction.ExteriorView)) return;
            // Never under the pause menu, and don't hijack the view while the player is busy (terminal).
            if (player.Controller.Has(FirstPersonController.Lock.Pause)) return;
            if (!Previewing && player.Controller.InputLocked) return;

            bool preview = !Previewing;
            previewRig.SetActive(preview);
            player.SetFirstPersonView(!preview);
            FirstPersonController.SetCursorLocked(true);
        }

        void BuildSpectateHud()
        {
            spectateHud = new GameObject("SpectateHUD", typeof(Canvas), typeof(CanvasScaler));
            spectateHud.transform.SetParent(transform, false);
            var canvas = spectateHud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = spectateHud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(spectateHud.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 70f);
            rt.sizeDelta = new Vector2(-120f, 32f);
            spectateLabel = go.GetComponent<Text>();
            spectateLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            spectateLabel.fontSize = 20;
            spectateLabel.fontStyle = FontStyle.Bold;
            spectateLabel.alignment = TextAnchor.MiddleCenter;
            spectateLabel.color = new Color(0.85f, 0.95f, 0.9f);
            spectateLabel.raycastTarget = false;
            spectateHud.SetActive(false);
        }
    }
}
