using TheDeep.Submarine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] Transform harness;
        [SerializeField] Material ropeMaterial;
        [SerializeField] float maxLength = 60f;
        [SerializeField] float minLength = 1.2f;
        [SerializeField, Tooltip("How fast the winch lets rope out when pulled, m/s.")] float payoutSpeed = 4f;
        [SerializeField, Tooltip("Reel-in speed, m/s.")] float reelSpeed = 1.8f;
        [SerializeField, Tooltip("Spring strength when the rope is stretched, N per metre.")] float stiffness = 700f;
        [SerializeField, Tooltip("Resists moving away along a taut rope, N per m/s.")] float damping = 180f;

        readonly NetworkVariable<float> paidOut = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> reeling = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        DiverController diver;
        TetherRope rope;

        public float MaxLength => maxLength;
        public float PaidOut => paidOut.Value;
        public bool IsReeling => reeling.Value;
        /// <summary>0 = slack, 1 = at its limit and straining.</summary>
        public float Tension => rope == null ? 0f : Mathf.Clamp01(rope.Stretch / 0.6f);
        public bool HasRope => rope != null;
        /// <summary>Owner side: reel in as if R were held (for the sub crew's winch control).</summary>
        public bool WinchReelIn { get; set; }

        void Awake() => diver = GetComponent<DiverController>();

        public override void OnNetworkSpawn()
        {
            diver.DivingChanged += OnDivingChanged;
            if (diver.IsDiving) OnDivingChanged(true);
            if (IsOwner) diver.HudExtra = HudLine;
        }

        public override void OnNetworkDespawn()
        {
            diver.DivingChanged -= OnDivingChanged;
            DestroyRope();
        }

        void OnDivingChanged(bool inWater)
        {
            if (!inWater)
            {
                DestroyRope();
                return;
            }
            var anchor = TetherAnchor.Instance;
            if (anchor == null || rope != null) return;

            var go = new GameObject($"Tether_{OwnerClientId}", typeof(LineRenderer), typeof(TetherRope));
            rope = go.GetComponent<TetherRope>();
            Vector3 start = anchor.AttachPoint((int)OwnerClientId);
            rope.Init(anchor, start, harness.position, ropeMaterial);
            if (IsOwner) paidOut.Value = Mathf.Clamp(Vector3.Distance(start, harness.position) + 1.5f, minLength, maxLength);
            rope.Length = Mathf.Max(paidOut.Value, minLength);
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
            var keyboard = Keyboard.current;
            bool wantReel = WinchReelIn ||
                            (keyboard != null && keyboard.rKey.isPressed && !GetComponent<FirstPersonController>().InputLocked);
            if (reeling.Value != wantReel) reeling.Value = wantReel;

            float length = paidOut.Value;
            if (wantReel)
                length -= reelSpeed * Time.deltaTime;
            else if (rope.Stretch > 0.1f)
                length += payoutSpeed * Time.deltaTime; // diver is pulling: winch lets more out
            length = Mathf.Clamp(length, minLength, maxLength);
            if (!Mathf.Approximately(length, paidOut.Value)) paidOut.Value = length;
        }

        void FixedUpdate()
        {
            if (rope == null || TetherAnchor.Instance == null) return;
            rope.Length = Mathf.Max(paidOut.Value, minLength);
            rope.Simulate(TetherAnchor.Instance.AttachPoint((int)OwnerClientId), harness.position, Time.fixedDeltaTime);

            if (!IsOwner || rope.Stretch <= 0f) return;
            Vector3 dir = rope.PullDirection;
            float awaySpeed = Mathf.Max(0f, -Vector3.Dot(diver.Body.linearVelocity, dir));
            float force = stiffness * rope.Stretch + damping * awaySpeed;
            diver.AddExternalForce(dir * Mathf.Min(force, 4000f));
        }

        string HudLine()
        {
            if (rope == null) return "";
            int bars = Mathf.RoundToInt(Tension * 10f);
            string meter = new string('|', bars) + new string('.', 10 - bars);
            string state = reeling.Value ? "REELING IN"
                : paidOut.Value >= maxLength - 0.05f ? "AT LIMIT"
                : rope.Stretch > 0.1f ? "PAYING OUT" : "SLACK";
            return $"TETHER {paidOut.Value:00.0}/{maxLength:0} M   TENSION [{meter}]   {state}   <size=15>(hold R to reel in)</size>";
        }
    }
}
