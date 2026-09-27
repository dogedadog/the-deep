using System.Collections;
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
        /// <summary>How long the owner holds on black 'NO VITALS' before the spectator view.</summary>
        const float DeathHoldSeconds = 1.5f;

        /// <summary>Radial mask for the danger overlay: clear in the middle, opaque at the edges. Built once.</summary>
        static Sprite edgeMask;

        [SerializeField] float airSeconds = 360f;
        [SerializeField] GameObject bodyPrefab;
        [SerializeField] Light headlamp;

        readonly NetworkVariable<bool> dead = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> airPercent = new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        DiverController diver;
        PlayerNetwork net;
        float air, crush;
        string causeOfDeath = "";
        string chipNote = "";
        Coroutine deathBeat;
        Image vignette;
        /// <summary>Flat black over the whole view for the last seconds (the vignette's centre stays clear).</summary>
        Image fade;
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
                // Flat dim while spectating; the death beat keeps its opaque black until it ends.
                if (deathBeat == null)
                {
                    vignette.sprite = null;
                    vignette.color = new Color(0f, 0f, 0f, 0.35f);
                }
                return;
            }

            float danger = 0f;
            float closing = 0f; // 0..1 over the last seconds of air or suit integrity
            string text = "";
            if (diver.IsDiving)
            {
                // Boosting burns air faster.
                bool exerting = Controls.Held(GameAction.Sprint) && !net.Controller.InputLocked && diver.Body.linearVelocity.sqrMagnitude > 1f;
                air -= Time.deltaTime * (exerting ? 1.6f : 1f);
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
                    text += $"!! LOW AIR - {Mathf.CeilToInt(air)}s LEFT - RETURN TO THE SUB !!\n";
                }

                // Last seconds: darkness closes in and the lamp stutters (the lamp is DiverController's otherwise).
                closing = Mathf.Max(Mathf.InverseLerp(6f, 0f, air), Mathf.InverseLerp(CrushSeconds - 3f, CrushSeconds, crush));
                bool lampOn = closing <= 0f || Random.value > closing * 0.6f;
                if (headlamp != null && headlamp.enabled != lampOn) headlamp.enabled = lampOn;
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
            var red = new Color(0.6f, 0f, 0f, danger * (0.4f + 0.45f * pulse));
            vignette.color = closing > 0f ? Color.Lerp(red, Color.black, closing) : red;
            fade.color = new Color(0f, 0f, 0f, closing * closing * 0.95f);
        }

        string AirLine()
        {
            int bars = Mathf.Clamp(Mathf.RoundToInt(Air01 * 10f), 0, 10);
            string color = Air01 < 0.25f ? "#ff5040" : "#9fffc0";
            int s = Mathf.CeilToInt(Mathf.Max(0f, air));
            return $"<color={color}>AIR [{new string('|', bars)}{new string('.', 10 - bars)}] {s / 60}:{s % 60:00}</color>";
        }

        /// <summary>Owner: this diver dies here.</summary>
        void Die(string cause)
        {
            causeOfDeath = cause;
            var archive = FootageArchive.Instance;
            var clip = GetComponent<HelmetCamera>()?.TakeChip(endsInDeath: true);
            int chipId = clip != null && archive != null ? archive.NewChipId() : 0;

            // Only promise chips that exist (counted before the body takes the carried ones).
            int carried = 0;
            if (archive != null && archive.IsSpawned)
                foreach (var c in archive.Chips)
                    if (c.Status == ChipStatus.Carried && c.Carrier == OwnerClientId) carried++;
            chipNote = chipId != 0 ? "Your camera chip is still in your suit - your crew can recover it."
                : carried > 0 ? "The chips you carried are still in your suit - your crew can recover them."
                : "Your camera held no footage.";

            // Body and NO VITALS first; the footage upload can take a while. The chip is registered as InBody.
            DiedRpc(transform.position, transform.rotation, net.CrewNumber, chipId);
            dead.Value = true;
            ApplyDead(true);
            if (chipId != 0) archive.Submit(chipId, clip, ChipStatus.InBody);
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
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; // runs twice on the owner
                rb.isKinematic = true;
            }
            else if (diver.IsDiving) rb.isKinematic = false;

            if (isDead)
            {
                // Runs twice on the owner (Die and OnValueChanged): start the beat only once.
                if (deathBeat == null) deathBeat = StartCoroutine(DeathBeat());
            }
            else
            {
                if (deathBeat != null) StopCoroutine(deathBeat);
                deathBeat = null;
                FindFirstObjectByType<ExteriorPreviewCamera>()?.StopSpectating();
                net.SetFirstPersonView(true);
                if (vignette != null) vignette.sprite = edgeMask;
                if (fade != null) fade.color = Color.clear;
                if (deathText != null) deathText.gameObject.SetActive(false);
            }
            if (warning != null && isDead) warning.text = "";
        }

        /// <summary>Owner: hold on black 'NO VITALS' in first person for a moment, then cut to the spectator view.</summary>
        IEnumerator DeathBeat()
        {
            if (vignette != null)
            {
                vignette.sprite = null;
                vignette.color = Color.black;
            }
            if (fade != null) fade.color = Color.clear;
            if (deathText != null)
            {
                deathText.gameObject.SetActive(true);
                deathText.text = "NO VITALS";
            }
            yield return new WaitForSeconds(DeathHoldSeconds);

            deathBeat = null;
            net.SetFirstPersonView(false);
            FindFirstObjectByType<ExteriorPreviewCamera>()?.Spectate(transform);
            if (deathText != null)
                deathText.text = $"YOU DIED\n<size=22>{causeOfDeath}</size>\n\n<size=18>{chipNote}\n" +
                                 "You can still hear the radio. You'll be back aboard when the expedition ends.</size>";
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
            if (edgeMask == null) edgeMask = BuildEdgeMask();
            vignette.sprite = edgeMask;

            var f = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(canvasGo.transform, false);
            var frt = (RectTransform)f.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fade = f.GetComponent<Image>();
            fade.raycastTarget = false;
            fade.color = Color.clear;

            warning = MakeText(canvasGo.transform, "Warning", 26, new Color(1f, 0.35f, 0.25f), TextAnchor.UpperCenter, new Vector2(0, -120));
            deathText = MakeText(canvasGo.transform, "Death", 54, new Color(0.95f, 0.85f, 0.8f), TextAnchor.MiddleCenter, Vector2.zero);
            deathText.gameObject.SetActive(false);
        }

        /// <summary>64x64 white mask whose alpha rises from the middle to the edges, so danger closes in from the sides.</summary>
        static Sprite BuildEdgeMask()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DangerVignette",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var centre = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) / (size * 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, r)));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
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
