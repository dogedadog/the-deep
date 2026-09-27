using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Data;
using TheDeep.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Player
{
    /// <summary>
    /// The diver's handheld scanner. Hold left mouse on a <see cref="ScanTarget"/> to record it
    /// (keep aim on moving creatures), then press T to radio the data to the sub. Data is held on
    /// the scanner until sent; climbing back aboard uploads anything still held. Data held when
    /// the diver dies is lost, and the crew may scan those targets again.
    /// </summary>
    [RequireComponent(typeof(DiverController))]
    public class DiverScanner : NetworkBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float range = 9f;
        [SerializeField] Material beamMaterial;

        readonly NetworkVariable<int> heldCount = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly List<int> held = new();
        readonly Collider[] zoneHits = new Collider[16];

        DiverController diver;
        FirstPersonController input;
        DiverHealth health;
        ScanTarget aimed;
        float progress;
        string flash;
        float flashUntil;
        int heldExpedition;
        bool wasDead;
        Text readout;
        LineRenderer beam;

        /// <summary>Packets on this diver's scanner that haven't been sent yet (visible to the crew).</summary>
        public int HeldCount => heldCount.Value;

        void Awake()
        {
            diver = GetComponent<DiverController>();
            input = GetComponent<FirstPersonController>();
            health = GetComponent<DiverHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            wasDead = health != null && health.IsDead;
            diver.HudLines.Add(HudLine);
            diver.DivingChanged += OnDivingChanged;
            BuildReadout();
        }

        void OnDivingChanged(bool inWater)
        {
            // Back aboard: plug the scanner in and upload whatever wasn't radioed in.
            if (!inWater && held.Count > 0) Transmit(1f, docked: true);
            if (!inWater) SetBeam(false, Vector3.zero);
        }

        /// <summary>
        /// Owner: the expedition ended with data still on the scanner. It's lost, not carried into the next one.
        /// A scan finished just before the end may be reported after the server's reset, so free
        /// those targets again (sent after that report, so it arrives after it).
        /// </summary>
        public void DiscardHeld()
        {
            if (!IsOwner) return;
            progress = 0f;
            if (held.Count == 0) return;
            Flash($"EXPEDITION OVER  -  {held.Count} UNSENT PACKET(S) LOST");
            int[] ids = held.ToArray();
            held.Clear();
            heldCount.Value = 0;
            ExpeditionState.Instance?.ForgetScansRpc(ids);
        }

        void Update()
        {
            if (!IsOwner) return;
            bool dead = health != null && health.IsDead;
            if (dead && !wasDead) DropHeldOnDeath();
            wasDead = dead;

            bool active = diver.IsDiving && !input.InputLocked && !dead;
            readout.gameObject.SetActive(active || Time.time < flashUntil);
            if (!active)
            {
                SetBeam(false, Vector3.zero);
                UpdateReadout(null, false); // only a flash, e.g. the upload message aboard
                return;
            }

            // What are we pointing at?
            ScanTarget target = null;
            Vector3 hitPoint = Vector3.zero;
            if (Physics.SphereCast(head.position, 0.3f, head.forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<ScanTarget>();
                hitPoint = hit.point;
                // Scan zones are invisible: end the beam at the thing, not on the zone's surface.
                if (target != null && hit.collider.isTrigger)
                    hitPoint = head.position + head.forward *
                               Mathf.Clamp(Vector3.Dot(target.transform.position - head.position, head.forward), hit.distance, range);
            }
            if (target == null) target = ZoneTarget(ref hitPoint);
            // A frame without a target just lets progress decay; only a different target resets it.
            if (target != null && target != aimed) progress = 0f;
            if (target != null) aimed = target;

            bool scanning = Controls.Held(GameAction.Scan) && Cursor.lockState == CursorLockMode.Locked;
            bool done = target != null && (held.Contains(target.Id) || (ExpeditionState.Instance != null && ExpeditionState.Instance.IsDocumented(target.Id)));

            if (target != null && scanning && !done)
            {
                progress += Time.deltaTime / target.ScanSeconds;
                if (progress >= 1f)
                {
                    if (held.Count == 0) heldExpedition = CrewProgress.Instance != null ? CrewProgress.Instance.Expedition : 0;
                    held.Add(target.Id);
                    heldCount.Value = held.Count;
                    ExpeditionState.Instance?.ReportScanRpc(target.Id);
                    Flash($"DATA RECORDED: {target.Title}  -  press {Controls.Label(GameAction.Transmit)} to transmit");
                    progress = 0f;
                }
            }
            else
            {
                progress = Mathf.Max(0f, progress - Time.deltaTime * 0.5f);
            }
            SetBeam(target != null && scanning && !done, hitPoint);

            if (Controls.Pressed(GameAction.Transmit))
            {
                float signal = SignalModel.Strength(transform.position);
                if (held.Count == 0) Flash("NO DATA TO TRANSMIT");
                else if (signal <= SignalModel.NoSignalThreshold) Flash("NO SIGNAL  -  get closer to the sub");
                else Transmit(signal, docked: false);
            }

            UpdateReadout(target, done);
        }

        /// <summary>
        /// Casts skip colliders they start inside, so up close (head inside a scan zone) pick the
        /// zone whose target is nearest the crosshair, within 60 degrees.
        /// </summary>
        ScanTarget ZoneTarget(ref Vector3 hitPoint)
        {
            ScanTarget best = null;
            float bestAngle = 60f;
            int n = Physics.OverlapSphereNonAlloc(head.position, 0.05f, zoneHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (!zoneHits[i].isTrigger) continue;
                var t = zoneHits[i].GetComponentInParent<ScanTarget>();
                if (t == null) continue;
                Vector3 c = t.transform.position;
                float angle = (c - head.position).sqrMagnitude < 0.09f ? 0f : Vector3.Angle(head.forward, c - head.position);
                if (angle >= bestAngle) continue;
                bestAngle = angle;
                best = t;
                hitPoint = c;
            }
            return best;
        }

        /// <summary>Owner, as the diver dies: unsent data dies too, so the crew may scan those targets again.</summary>
        void DropHeldOnDeath()
        {
            progress = 0f;
            flashUntil = 0f;
            if (held.Count == 0) return;
            int[] ids = held.ToArray();
            held.Clear();
            heldCount.Value = 0;
            ExpeditionState.Instance?.ForgetScansRpc(ids);
        }

        void Transmit(float signal, bool docked)
        {
            if (ExpeditionState.Instance == null) return;
            ExpeditionState.Instance.TransmitRpc(held.ToArray(), signal, heldExpedition);
            string quality = signal < SignalModel.CorruptionThreshold ? "ARRIVED CORRUPTED - crew must REPAIR it" : "RECEIVED CLEAN";
            Flash(docked
                ? $"SCANNER UPLOADED {held.Count} PACKET(S) ABOARD"
                : $"TRANSMITTED {held.Count} PACKET(S)  (SIGNAL {signal * 100f:0}%)  -  {quality}");
            held.Clear();
            heldCount.Value = 0;
        }

        void Flash(string message)
        {
            flash = message;
            flashUntil = Time.time + 4f;
        }

        string HudLine()
        {
            float signal = SignalModel.Strength(transform.position);
            string verdict = signal <= SignalModel.NoSignalThreshold ? "<color=#ff5040>NO SIGNAL</color>"
                : signal < SignalModel.CorruptionThreshold ? "<color=#ffc040>WEAK - DATA WILL ARRIVE CORRUPTED</color>"
                : "<color=#9fffc0>CLEAN</color>";
            return $"SCANNER {held.Count} PACKET(S) HELD    SIGNAL {SignalModel.Bars(signal)} {signal * 100f:0}%  {verdict}";
        }

        // ------------------------------------------------------------------ on-screen readout

        void BuildReadout()
        {
            var canvasGo = new GameObject("ScannerHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Readout", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -120);
            rt.sizeDelta = new Vector2(900, 110);
            readout = go.GetComponent<Text>();
            readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            readout.fontSize = 22;
            readout.fontStyle = FontStyle.Bold;
            readout.alignment = TextAnchor.UpperCenter;
            readout.color = new Color(0.55f, 0.95f, 1f, 0.95f);
            readout.raycastTarget = false;
            readout.gameObject.SetActive(false);

            var beamGo = new GameObject("ScanBeam", typeof(LineRenderer));
            beamGo.transform.SetParent(transform, false);
            beam = beamGo.GetComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.startWidth = 0.02f; // still a few pixels wide at 360p
            beam.endWidth = 0.06f;
            beam.sharedMaterial = beamMaterial;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;
        }

        void UpdateReadout(ScanTarget target, bool done)
        {
            string text = "";
            if (target != null)
            {
                text = $"{target.Title}   [{target.ClassLabel}  {target.Category.ToString().ToUpperInvariant()}]\n";
                if (held.Contains(target.Id)) text += $"DATA HELD - PRESS {Controls.Label(GameAction.Transmit)} TO SEND";
                else if (done) text += "ALREADY DOCUMENTED";
                else
                {
                    int bars = Mathf.RoundToInt(progress * 20f);
                    text += progress > 0f
                        ? $"SCANNING [{new string('#', bars)}{new string('.', 20 - bars)}] {progress * 100f:0}%"
                        : $"HOLD {Controls.Label(GameAction.Scan)} TO SCAN";
                }
            }
            if (Time.time < flashUntil) text += (text.Length > 0 ? "\n" : "") + flash;
            readout.text = text;
        }

        void SetBeam(bool on, Vector3 to)
        {
            if (beam == null) return;
            beam.enabled = on;
            if (!on) return;
            // From just below-right of the view, flickering a little.
            Vector3 from = head.position + head.right * 0.25f - head.up * 0.25f + head.forward * 0.3f;
            beam.SetPosition(0, from);
            beam.SetPosition(1, to + Random.insideUnitSphere * 0.05f);
        }
    }
}
