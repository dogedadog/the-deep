using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Footage;
using TheDeep.Progression;
using TheDeep.Submarine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Player
{
    /// <summary>
    /// What can kill a diver for now: running out of air, or staying deeper than the suit is rated
    /// for. On death the diver leaves a body (with their camera chip and any chips they carried),
    /// and spectates until the expedition ends, when everyone is revived aboard.
    /// </summary>
    public class DiverHealth : NetworkBehaviour
    {
        const float CrushSeconds = 10f;

        [SerializeField] float airSeconds = 360f;
        [SerializeField] GameObject bodyPrefab;
        [SerializeField] Light headlamp;

        readonly NetworkVariable<bool> dead = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> airPercent = new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        DiverController diver;
        PlayerNetwork net;
        float air, crush;
        string causeOfDeath = "";
        Image vignette;
        Text warning, deathText;

        public bool IsDead => dead.Value;
        /// <summary>Air left, 0..1 (synced so the crew can see it on the Diver Map).</summary>
        public float Air01 => IsOwner ? air / airSeconds : airPercent.Value / 100f;

        void Awake()
        {
            diver = GetComponent<DiverController>();
            net = GetComponent<PlayerNetwork>();
            air = airSeconds;
        }

        public override void OnNetworkSpawn()
        {
            dead.OnValueChanged += (_, now) => ApplyDead(now);
            ApplyDead(dead.Value);
            if (!IsOwner) return;
            diver.HudLines.Insert(0, AirLine);
            BuildOverlay();
        }

        void Update()
        {
            if (!IsOwner) return;
            if (dead.Value)
            {
                vignette.color = new Color(0f, 0f, 0f, 0.35f);
                return;
            }

            float danger = 0f;
            string text = "";
            if (diver.IsDiving)
            {
                air -= Time.deltaTime;
                int level = CrewProgress.Instance != null ? CrewProgress.Instance.Level(UpgradeType.DepthRating) : 0;
                bool tooDeep = WorldInfo.DepthAt(transform.position.y) > UpgradeCatalog.DepthRating(level);
                crush = tooDeep ? crush + Time.deltaTime : Mathf.Max(0f, crush - Time.deltaTime * 2f);

                if (air <= 0f) { Die("DROWNED - OUT OF AIR"); return; }
                if (crush >= CrushSeconds) { Die("CRUSHED - BEYOND SUIT DEPTH RATING"); return; }

                if (crush > 0f)
                {
                    danger = Mathf.Max(danger, crush / CrushSeconds);
                    text += $"!! SUIT FAILING - ASCEND !!  CRUSH IN {CrushSeconds - crush:0.0}s\n";
                }
                if (Air01 < 0.25f)
                {
                    danger = Mathf.Max(danger, 1f - Air01 / 0.25f);
                    text += $"!! LOW AIR - {air:0}s LEFT - RETURN TO THE SUB !!\n";
                }
            }
            else
            {
                air = Mathf.Min(airSeconds, air + Time.deltaTime * 90f); // topped up quickly aboard
                crush = 0f;
            }

            warning.text = text;
            byte percent = (byte)Mathf.Clamp(Mathf.RoundToInt(air / airSeconds * 100f), 0, 100);
            if (percent != airPercent.Value) airPercent.Value = percent;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (4f + danger * 6f));
            vignette.color = new Color(0.6f, 0f, 0f, danger * (0.25f + 0.3f * pulse));
        }

        string AirLine()
        {
            int bars = Mathf.Clamp(Mathf.RoundToInt(Air01 * 10f), 0, 10);
            string color = Air01 < 0.25f ? "#ff5040" : "#9fffc0";
            return $"<color={color}>AIR [{new string('|', bars)}{new string('.', 10 - bars)}] {air / 60f:0}:{air % 60f:00}</color>";
        }

        /// <summary>Owner: this diver dies here.</summary>
        void Die(string cause)
        {
            causeOfDeath = cause;
            var archive = FootageArchive.Instance;
            int chipId = 0;
            var clip = GetComponent<HelmetCamera>()?.TakeChip(endsInDeath: true);
            if (clip != null && archive != null)
            {
                chipId = archive.NewChipId();
                archive.Submit(chipId, clip, ChipStatus.InBody);
            }
            DiedRpc(transform.position, transform.rotation, net.CrewNumber, chipId);
            dead.Value = true;
            ApplyDead(true);
        }

        [Rpc(SendTo.Server)]
        void DiedRpc(Vector3 position, Quaternion rotation, int crew, int chipId, RpcParams rpcParams = default)
        {
            if (bodyPrefab == null) return;
            var body = Instantiate(bodyPrefab, position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            body.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            var chipIds = new List<int>();
            if (chipId != 0) chipIds.Add(chipId);
            if (FootageArchive.Instance != null) chipIds.AddRange(FootageArchive.Instance.ServerCarriedBy(rpcParams.Receive.SenderClientId));
            body.GetComponent<DiverBody>().ServerInit(crew, chipIds);
            if (ExpeditionStateAnnounce != null) ExpeditionStateAnnounce($"D{crew} - NO VITALS");
        }

        /// <summary>Hooked up by the expedition so deaths get a crew-wide banner.</summary>
        public static System.Action<string> ExpeditionStateAnnounce;

        /// <summary>Owner: back to life aboard (the expedition ended).</summary>
        public void Revive()
        {
            if (!IsOwner) return;
            air = airSeconds;
            crush = 0f;
            if (dead.Value)
            {
                dead.Value = false;
                ApplyDead(false);
            }
            if (diver.IsDiving && DiveHatch.CabinEntry != null) diver.ExitWater(DiveHatch.CabinEntry);
        }

        void ApplyDead(bool isDead)
        {
            // Everyone: hide the avatar and switch off its collision and lamp.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<Canvas>() == null && r is not ParticleSystemRenderer && r is not LineRenderer)
                    r.forceRenderingOff = isDead;
            foreach (var c in GetComponents<Collider>()) if (isDead) c.enabled = false;
            if (!isDead)
            {
                // Restore the right collider for walking or swimming.
                GetComponent<CharacterController>().enabled = !diver.IsDiving;
                GetComponent<CapsuleCollider>().enabled = diver.IsDiving;
            }
            if (headlamp != null && isDead) headlamp.enabled = false;
            if (!IsOwner) return;

            var rb = GetComponent<Rigidbody>();
            if (isDead)
            {
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            else if (diver.IsDiving) rb.isKinematic = false;

            var preview = FindFirstObjectByType<ExteriorPreviewCamera>();
            if (isDead)
            {
                net.SetFirstPersonView(false);
                preview?.Spectate(transform);
            }
            else
            {
                preview?.StopSpectating();
                net.SetFirstPersonView(true);
            }
            if (deathText != null)
            {
                deathText.gameObject.SetActive(isDead);
                deathText.text = $"YOU DIED\n<size=22>{causeOfDeath}</size>\n\n<size=18>Your camera chip is still in your suit - your crew can recover it.\n" +
                                 "You can still hear the radio. You'll be back aboard when the expedition ends.</size>";
            }
            if (warning != null && isDead) warning.text = "";
        }

        void BuildOverlay()
        {
            var canvasGo = new GameObject("HealthHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;

            var v = new GameObject("Danger", typeof(RectTransform), typeof(Image));
            v.transform.SetParent(canvasGo.transform, false);
            var vrt = (RectTransform)v.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            vignette = v.GetComponent<Image>();
            vignette.raycastTarget = false;
            vignette.color = Color.clear;

            warning = MakeText(canvasGo.transform, "Warning", 26, new Color(1f, 0.35f, 0.25f), TextAnchor.UpperCenter, new Vector2(0, -120));
            deathText = MakeText(canvasGo.transform, "Death", 54, new Color(0.95f, 0.85f, 0.8f), TextAnchor.MiddleCenter, Vector2.zero);
            deathText.gameObject.SetActive(false);
        }

        static Text MakeText(Transform parent, string name, int size, Color color, TextAnchor anchor, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(60, 60);
            rt.offsetMax = new Vector2(-60, -60);
            rt.anchoredPosition += offset;
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }
    }
}
