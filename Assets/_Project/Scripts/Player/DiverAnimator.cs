using TheDeep.Audio;
using TheDeep.Core;
using TheDeep.Footage;
using TheDeep.Submarine;
using TheDeep.Voice;
using Unity.Netcode;
using UnityEngine;

namespace TheDeep.Player
{
    /// <summary>
    /// Poses the jointed diver model in code, on every machine. Movement comes from how the player's
    /// transform moves (so remote players animate from their synced position); what they're doing
    /// (scanning, filming, at the terminal) comes from a small owner-written flag set.
    ///   In the sub: idle breathing, walking, typing at the terminal.
    ///   In the water: treading water, flutter kicking, body turning into the swim direction
    ///   (head-first when diving down), plus scanning, radio and reeling-in poses on top.
    /// It also makes the body's sounds, in step with the motion: footsteps aboard and breathing
    /// underwater (2D for the owner, 3D for everyone else).
    /// </summary>
    public class DiverAnimator : NetworkBehaviour
    {
        const byte FlagScanning = 1, FlagRecording = 2, FlagTerminal = 4;

        [SerializeField] Transform head;
        [SerializeField] Transform body, hips, neck;
        [SerializeField] Transform shoulderL, shoulderR, elbowL, elbowR, handL, handR;
        [SerializeField] Transform hipL, hipR, kneeL, kneeR, ankleL, ankleR;
        [SerializeField] GameObject finL, finR, scanner;
        [SerializeField] Renderer scannerLens, recordLight;
        [SerializeField] ParticleSystem bubbles;

        readonly NetworkVariable<byte> flags = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        DiverController diver;
        DiverHealth health;
        DiverTether tether;
        PlayerVoice voice;
        HelmetCamera helmetCam;
        FirstPersonController controls;
        PlayerInteractor interactor;
        TerminalStation terminal;
        MaterialPropertyBlock lensBlock;

        Vector3 bodyRest, lastPosition, velocity;
        float walkPhase, walkK, kickPhase, bodyPitch, bodyRoll, nextBreath;
        float swimW, scanW, radioW, reelW, terminalW;
        int lastStep, stepVariant;

        void Awake()
        {
            diver = GetComponent<DiverController>();
            health = GetComponent<DiverHealth>();
            tether = GetComponent<DiverTether>();
            voice = GetComponent<PlayerVoice>();
            helmetCam = GetComponent<HelmetCamera>();
            controls = GetComponent<FirstPersonController>();
            interactor = GetComponent<PlayerInteractor>();
            lensBlock = new MaterialPropertyBlock();
            bodyRest = body.localPosition;
            lastPosition = transform.position;
        }

        public override void OnNetworkSpawn() => lastPosition = transform.position;

        bool Has(byte flag) => (flags.Value & flag) != 0;

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (terminal == null) terminal = FindFirstObjectByType<TerminalStation>();
            byte f = 0;
            bool diving = diver.IsDiving;
            if (diving && !controls.InputLocked && Controls.Held(GameAction.Scan) && Cursor.lockState == CursorLockMode.Locked) f |= FlagScanning;
            if (helmetCam != null && helmetCam.Recording) f |= FlagRecording;
            if (!diving && terminal != null && terminal.User == interactor) f |= FlagTerminal;
            if (flags.Value != f) flags.Value = f;
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float t = Time.time;

            // How fast we're really moving (works for remote players too). Ignore teleports.
            Vector3 raw = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            if (raw.magnitude > 12f) raw = velocity;
            velocity = Vector3.Lerp(velocity, raw, 1f - Mathf.Exp(-8f * dt));

            bool diving = diver != null && diver.IsDiving;
            swimW = Ease(swimW, diving, dt, 3f);
            scanW = Ease(scanW, diving && Has(FlagScanning), dt, 6f);
            radioW = Ease(radioW, voice != null && voice.RadioTransmitting, dt, 6f);
            reelW = Ease(reelW, diving && tether != null && tether.IsReeling, dt, 5f);
            terminalW = Ease(terminalW, !diving && Has(FlagTerminal), dt, 4f);

            finL.SetActive(diving);
            finR.SetActive(diving);
            scanner.SetActive(diving);
            if (recordLight != null) recordLight.enabled = Has(FlagRecording);
            lensBlock.SetColor("_EmissionColor", scanW > 0.5f ? new Color(0.6f, 2.4f, 2.8f) * (0.8f + Mathf.Sin(t * 30f) * 0.2f) : new Color(0.1f, 0.3f, 0.35f));
            scannerLens.SetPropertyBlock(lensBlock);

            float headPitch = head.localEulerAngles.x;
            if (headPitch > 180f) headPitch -= 360f;

            var walk = WalkPose(dt, t);
            var swim = SwimPose(dt, t);
            var pose = Pose.Blend(walk, swim, swimW);

            // Body: swings around the neck into the swim direction.
            body.localPosition = bodyRest + Vector3.up * pose.Bob;
            body.localRotation = Quaternion.Euler(pose.BodyPitch, 0f, pose.BodyRoll);
            neck.localRotation = Quaternion.Euler(Mathf.Clamp(headPitch - pose.BodyPitch, -75f, 60f) + (voice != null && voice.Talking ? Mathf.Sin(t * 9f) * 2f : 0f), 0f, 0f);
            hips.localRotation = Quaternion.Euler(pose.HipsPitch, 0f, 0f);

            // Layers on top: terminal typing, reeling the line in, scanning, holding the radio.
            var armL = pose.ArmL;
            var armR = pose.ArmR;
            if (terminalW > 0f)
            {
                armL = Arm.Lerp(armL, new Arm(-55f, -10f, -60f + Mathf.Sin(t * 9f) * 4f, Mathf.Sin(t * 14f) * 14f), terminalW);
                armR = Arm.Lerp(armR, new Arm(-55f, -10f, -60f + Mathf.Sin(t * 11f + 1f) * 4f, Mathf.Sin(t * 13f + 2f) * 14f), terminalW);
            }
            if (reelW > 0f)
            {
                float pull = Mathf.Sin(t * 5f) * 12f;
                armL = Arm.Lerp(armL, new Arm(-60f + pull, -14f, -50f, 0f), reelW);
                armR = Arm.Lerp(armR, new Arm(-60f - pull, -14f, -50f, 0f), reelW);
            }
            if (scanW > 0f)
            {
                // Point the scanner where the diver is looking.
                float aim = Mathf.Clamp(-90f + headPitch - pose.BodyPitch, -200f, 20f);
                armR = Arm.Lerp(armR, new Arm(aim, -4f, -12f, 0f), scanW);
            }
            if (radioW > 0f)
                armL = Arm.Lerp(armL, new Arm(-35f, 110f, -115f, 20f), radioW); // hand up to the side of the helmet

            Apply(shoulderL, elbowL, handL, armL, 1f);
            Apply(shoulderR, elbowR, handR, armR, -1f);
            hipL.localRotation = Quaternion.Euler(pose.LegL.Hip, 0f, pose.LegL.Spread);
            hipR.localRotation = Quaternion.Euler(pose.LegR.Hip, 0f, -pose.LegR.Spread);
            kneeL.localRotation = Quaternion.Euler(pose.LegL.Knee, 0f, 0f);
            kneeR.localRotation = Quaternion.Euler(pose.LegR.Knee, 0f, 0f);
            ankleL.localRotation = Quaternion.Euler(pose.LegL.Ankle, 0f, 0f);
            ankleR.localRotation = Quaternion.Euler(pose.LegR.Ankle, 0f, 0f);

            // Breathing out underwater: a puff of bubbles every few seconds, faster when air runs low.
            // Never from a corpse (its particles stay visible).
            bool alive = health == null || !health.IsDead;
            if (diving && alive && t > nextBreath)
            {
                float air = health != null ? health.Air01 : 1f;
                float k = air < 0.25f ? Mathf.Lerp(0.35f, 1f, air / 0.25f) : 1f;
                nextBreath = t + Random.Range(3.5f, 5f) * k;
                if (voice == null || !voice.Talking)
                {
                    if (bubbles != null) bubbles.Emit(Random.Range(6, 11));
                    if (IsOwner) SfxPlayer.Play(Sfx.Breath, 0.12f);
                    else if (IsSpawned) SfxPlayer.PlayAt(Sfx.Exhale, head.position, 0.25f, 10f);
                }
            }
            else if (!diving) nextBreath = t + 2f; // first breath a moment after the splash, not on top of it

            // Footsteps aboard, one per half walk cycle. WalkPose runs in the water too, hence !diving.
            int step = Mathf.FloorToInt(walkPhase / Mathf.PI);
            if (step != lastStep)
            {
                lastStep = step;
                if (!diving && walkK > 0.2f && alive && IsSpawned)
                {
                    stepVariant = 1 - stepVariant;
                    float pitch = Random.Range(0.94f, 1.06f);
                    if (IsOwner) SfxPlayer.Play(Sfx.Step, 0.12f, pitch, stepVariant);
                    else SfxPlayer.PlayAt(Sfx.Step, transform.position, 0.35f, 12f, pitch, stepVariant);
                }
            }
        }

        // ------------------------------------------------------------------ poses

        Pose WalkPose(float dt, float t)
        {
            var local = transform.InverseTransformDirection(velocity);
            float speed = new Vector2(local.x, local.z).magnitude;
            float k = Mathf.Clamp01(speed / 2.5f);
            walkK = k;
            walkPhase += speed * 4.2f * dt * (local.z < -0.1f ? -1f : 1f);
            float s = Mathf.Sin(walkPhase);
            float breath = Mathf.Sin(t * 1.6f);
            return new Pose
            {
                Bob = breath * 0.006f - Mathf.Abs(Mathf.Cos(walkPhase)) * 0.03f * k,
                BodyPitch = 3f * k,
                HipsPitch = 0f,
                ArmL = new Arm(-s * 24f * k + breath * 1.5f, 7f, -12f - 10f * k, 0f),
                ArmR = new Arm(s * 24f * k + breath * 1.5f, 7f, -12f - 10f * k, 0f),
                LegL = new Leg(s * 28f * k, Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f)) * 45f * k, 0f, 2f),
                LegR = new Leg(-s * 28f * k, Mathf.Max(0f, Mathf.Sin(walkPhase + 1.6f + Mathf.PI)) * 45f * k, 0f, 2f),
            };
        }

        Pose SwimPose(float dt, float t)
        {
            float speed = velocity.magnitude;
            float move = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1.4f, speed));
            var local = transform.InverseTransformDirection(velocity);

            // Face into the direction of travel: up = upright, forward = flat, down = head first.
            float angle = Mathf.Atan2(local.z, local.y) * Mathf.Rad2Deg;
            if (angle < -60f) angle = local.y < 0f ? 170f : -35f;
            angle = Mathf.Clamp(angle, -35f, 170f);
            float targetPitch = Mathf.Lerp(12f + Mathf.Sin(t * 0.7f) * 4f, angle, move);
            float targetRoll = -Mathf.Clamp(local.x / Mathf.Max(speed, 0.1f), -1f, 1f) * 30f * move;
            bodyPitch = Mathf.Lerp(bodyPitch, targetPitch, 1f - Mathf.Exp(-3f * dt));
            bodyRoll = Mathf.Lerp(bodyRoll, targetRoll, 1f - Mathf.Exp(-3f * dt));

            // Flutter kick when moving, slow treading when not.
            float hz = Mathf.Lerp(0.8f, 1.6f + speed * 0.25f, move);
            kickPhase += dt * Mathf.PI * 2f * hz;
            float kick = Mathf.Sin(kickPhase);
            float kickAmp = Mathf.Lerp(16f, 24f, move);
            float tread = Mathf.Sin(t * 1.9f);

            Leg Kick(float phase) => new(
                Mathf.Lerp(-28f + Mathf.Sin(t * 1.9f + phase) * 22f, Mathf.Sin(kickPhase + phase) * kickAmp, move),
                Mathf.Lerp(50f + Mathf.Sin(t * 1.9f + phase + 1.2f) * 25f, 8f + Mathf.Max(0f, Mathf.Sin(kickPhase + phase + 1.2f)) * 22f, move),
                Mathf.Lerp(55f, 78f + Mathf.Sin(kickPhase + phase - 1f) * 16f, move),
                Mathf.Lerp(8f, 3f, move));

            return new Pose
            {
                Bob = Mathf.Sin(t * 1.1f) * 0.04f * (1f - move),
                BodyPitch = bodyPitch,
                BodyRoll = bodyRoll,
                HipsPitch = Mathf.Lerp(10f, 0f, move) + kick * 3f * move,
                // Treading: sculling out to the sides. Swimming: arms streamlined along the body.
                ArmL = Arm.Lerp(new Arm(-25f + tread * 25f, 45f + tread * 12f, -35f, tread * 20f), new Arm(10f, 8f, -8f, 0f), move),
                ArmR = Arm.Lerp(new Arm(-25f + tread * 25f, 45f + tread * 12f, -35f, -tread * 20f), new Arm(10f, 8f, -8f, 0f), move),
                LegL = Kick(0f),
                LegR = Kick(Mathf.PI),
            };
        }

        /// <param name="side">+1 left, -1 right: mirrors the outward roll.</param>
        static void Apply(Transform shoulder, Transform elbow, Transform hand, Arm a, float side)
        {
            shoulder.localRotation = Quaternion.Euler(a.Pitch, 0f, -side * a.Out);
            elbow.localRotation = Quaternion.Euler(a.Elbow, 0f, 0f);
            hand.localRotation = Quaternion.Euler(a.Wrist, 0f, 0f);
        }

        static float Ease(float w, bool on, float dt, float speed) => Mathf.MoveTowards(w, on ? 1f : 0f, dt * speed);

        /// <summary>Shoulder pitch (negative = forward), outward swing (degrees away from the body), elbow bend (negative), wrist.</summary>
        struct Arm
        {
            public float Pitch, Out, Elbow, Wrist;
            public Arm(float pitch, float outward, float elbow, float wrist) { Pitch = pitch; Out = outward; Elbow = elbow; Wrist = wrist; }
            public static Arm Lerp(Arm a, Arm b, float k) => new(
                Mathf.Lerp(a.Pitch, b.Pitch, k), Mathf.Lerp(a.Out, b.Out, k), Mathf.Lerp(a.Elbow, b.Elbow, k), Mathf.Lerp(a.Wrist, b.Wrist, k));
        }

        /// <summary>Hip swing (negative = forward), knee bend, ankle (positive = toes pointed), spread.</summary>
        struct Leg
        {
            public float Hip, Knee, Ankle, Spread;
            public Leg(float hip, float knee, float ankle, float spread) { Hip = hip; Knee = knee; Ankle = ankle; Spread = spread; }
            public static Leg Lerp(Leg a, Leg b, float k) => new(
                Mathf.Lerp(a.Hip, b.Hip, k), Mathf.Lerp(a.Knee, b.Knee, k), Mathf.Lerp(a.Ankle, b.Ankle, k), Mathf.Lerp(a.Spread, b.Spread, k));
        }

        struct Pose
        {
            public float Bob, BodyPitch, BodyRoll, HipsPitch;
            public Arm ArmL, ArmR;
            public Leg LegL, LegR;

            public static Pose Blend(Pose a, Pose b, float k) => new()
            {
                Bob = Mathf.Lerp(a.Bob, b.Bob, k),
                BodyPitch = Mathf.Lerp(a.BodyPitch, b.BodyPitch, k),
                BodyRoll = Mathf.Lerp(a.BodyRoll, b.BodyRoll, k),
                HipsPitch = Mathf.Lerp(a.HipsPitch, b.HipsPitch, k),
                ArmL = Arm.Lerp(a.ArmL, b.ArmL, k),
                ArmR = Arm.Lerp(a.ArmR, b.ArmR, k),
                LegL = Leg.Lerp(a.LegL, b.LegL, k),
                LegR = Leg.Lerp(a.LegR, b.LegR, k),
            };
        }
    }
}
