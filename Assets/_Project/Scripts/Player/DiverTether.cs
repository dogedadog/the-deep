using TheDeep.Core;
using TheDeep.Progression;
using TheDeep.Submarine;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Player
{
    /// <summary>
    /// A diver's safety line back to the sub. While diving, a rope runs from the hull fairlead to
    /// this diver's harness on every client. The winch free-spools rope out as the diver swims away,
    /// up to <see cref="MaxLength"/>; past that the rope goes taut and pulls back. Hold R to reel in.
    /// </summary>
    [RequireComponent(typeof(DiverController))]
    public class DiverTether : NetworkBehaviour
    {
        /// <summary>Stretch the hanging rope keeps even when slack (m); below this it never pulls or pays out.</summary>
        const float SlackTolerance = 0.1f;

        [SerializeField] Transform harness;
        [SerializeField] Material ropeMaterial;
        [SerializeField, Tooltip("Rope length with no upgrades.")] float baseMaxLength = 60f;
        [SerializeField] float minLength = 1.2f;
        [SerializeField, Tooltip("Reel-in speed, m/s.")] float reelSpeed = 3f;
        [SerializeField, Tooltip("Spring strength when the rope is stretched, N per metre.")] float stiffness = 700f;
        [SerializeField, Tooltip("Resists moving away along a taut rope, N per m/s.")] float damping = 180f;

        readonly NetworkVariable<float> paidOut = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> reeling = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        DiverController diver;
        DiverHealth health;
        PlayerNetwork net;
        TetherRope rope;

        /// <summary>Longest the rope can pay out, including the crew's rope upgrade.</summary>
        public float MaxLength => CrewProgress.Instance != null
            ? UpgradeCatalog.RopeLength(CrewProgress.Instance.Level(UpgradeType.RopeLength))
            : baseMaxLength;
        public float PaidOut => paidOut.Value;
        public bool IsReeling => reeling.Value;
        /// <summary>0 = slack, 1 = at its limit and straining.</summary>
        public float Tension => rope == null ? 0f : Mathf.Clamp01(Mathf.Max(0f, rope.Stretch - SlackTolerance) / 0.6f);
        public bool HasRope => rope != null;
        /// <summary>Owner side: reel in as if R were held (for the sub crew's winch control).</summary>
        public bool WinchReelIn { get; set; }

        /// <summary>Fairlead slot the rope leaves the hull from: one per crew slot.</summary>
        int Slot => net != null ? net.CrewNumber - 1 : (int)OwnerClientId;
        bool Dead => health != null && health.IsDead;

        void Awake()
        {
            diver = GetComponent<DiverController>();
            health = GetComponent<DiverHealth>();
            net = GetComponent<PlayerNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            diver.DivingChanged += OnDivingChanged;
            if (diver.IsDiving) OnDivingChanged(true);
            if (IsOwner) diver.HudLines.Add(HudLine);
        }

        /// <summary>Sub crew's winch control (Diver Map app): anyone can ask the diver's client to reel in.</summary>
        [Rpc(SendTo.Owner)]
        public void SetWinchRpc(bool reelIn) => WinchReelIn = reelIn;

        public override void OnNetworkDespawn()
        {
            diver.DivingChanged -= OnDivingChanged;
            DestroyRope();
        }

        void OnDivingChanged(bool inWater)
        {
            if (!inWater)
            {
                WinchReelIn = false;
                DestroyRope();
                return;
            }
            // Late joiners don't get a rope to a body.
            if (Dead) return;
            var anchor = TetherAnchor.Instance;
            if (anchor == null || rope != null) return;

            var go = new GameObject($"Tether_{OwnerClientId}", typeof(LineRenderer), typeof(TetherRope));
            rope = go.GetComponent<TetherRope>();
            Vector3 start = anchor.AttachPoint(Slot);
            rope.Init(start, harness.position, ropeMaterial);
            if (IsOwner) paidOut.Value = Mathf.Clamp(Vector3.Distance(start, harness.position) + 1.5f, minLength, MaxLength);
            rope.Length = Mathf.Max(paidOut.Value, minLength);
            if (IsOwner) rope.HideNearEnd = 1.6f;
            Debug.Log($"[Tether] Rope attached to player {OwnerClientId}");
        }

        void DestroyRope()
        {
            if (rope != null) Destroy(rope.gameObject);
            rope = null;
        }

        void Update()
        {
            if (!IsOwner || rope == null) return;
            bool dead = Dead;
            // The crew's winch stops by itself once the diver is back at the hatch.
            var anchor = TetherAnchor.Instance;
            if (WinchReelIn && (dead || (anchor != null && Vector3.Distance(anchor.AttachPoint(Slot), harness.position) < minLength + 1f)))
                WinchReelIn = false;
            bool wantReel = !dead && (WinchReelIn ||
                            (Controls.Held(GameAction.ReelIn) && !GetComponent<FirstPersonController>().InputLocked));
            if (reeling.Value != wantReel) reeling.Value = wantReel;

            // Paying out happens in FixedUpdate, where Stretch is measured.
            float length = paidOut.Value;
            if (wantReel) length -= reelSpeed * Time.deltaTime;
            length = Mathf.Clamp(length, minLength, MaxLength);
            if (!Mathf.Approximately(length, paidOut.Value)) paidOut.Value = length;
        }

        void FixedUpdate()
        {
            var anchor = TetherAnchor.Instance;
            if (rope == null || anchor == null) return;
            Vector3 start = anchor.AttachPoint(Slot);
            bool dead = Dead;
            // A dead diver's line still leads to the body while the sub stays put; it's cut when the sub leaves.
            // (Runs on every client: each one owns its own rope object.)
            if (dead)
            {
                var nav = SubNavigation.Instance;
                if ((nav != null && nav.Travelling) || Vector3.Distance(start, harness.position) > MaxLength + 5f)
                {
                    DestroyRope();
                    return;
                }
            }
            rope.Length = Mathf.Max(paidOut.Value, minLength);
            rope.Simulate(start, harness.position, Time.fixedDeltaTime);

            if (!IsOwner || dead) return;
            // Free-spool: the winch lets out exactly the rope being pulled, once per physics step.
            if (!reeling.Value && paidOut.Value < MaxLength && rope.Stretch > SlackTolerance)
                paidOut.Value = Mathf.Min(MaxLength, paidOut.Value + rope.Stretch);

            // Only pull while the winch holds the rope: reeling in, or all of it paid out.
            bool holding = reeling.Value || paidOut.Value >= MaxLength - 0.05f;
            if (!holding || rope.Stretch <= SlackTolerance) return;
            float s = rope.Stretch - SlackTolerance;
            Vector3 dir = rope.PullDirection;
            float awaySpeed = Mathf.Max(0f, -Vector3.Dot(diver.Body.linearVelocity, dir));
            float force = stiffness * s + damping * awaySpeed * Mathf.Clamp01(s / 0.3f);
            diver.AddExternalForce(dir * Mathf.Min(force, 4000f));
        }

        string HudLine()
        {
            if (rope == null) return "";
            int bars = Mathf.RoundToInt(Tension * 10f);
            string meter = new string('|', bars) + new string('.', 10 - bars);
            string state = WinchReelIn ? "<color=#ffb040>SUB WINCHING YOU IN</color>"
                : reeling.Value ? "REELING IN"
                : paidOut.Value >= MaxLength - 0.05f && rope.Stretch > SlackTolerance ? "AT LIMIT"
                : rope.Stretch > SlackTolerance ? "PAYING OUT" : "SLACK";
            // Rope once paid out stays out, so also show how far from the sub the diver really is.
            var anchor = TetherAnchor.Instance;
            string distance = anchor != null ? $"   {Vector3.Distance(anchor.AttachPoint(Slot), harness.position):0} M OUT" : "";
            return $"TETHER {paidOut.Value:00.0}/{MaxLength:0} M{distance}   TENSION [{meter}]   {state}   <size=15>(hold {Controls.Label(GameAction.ReelIn)} to reel in)</size>";
        }
    }
}
