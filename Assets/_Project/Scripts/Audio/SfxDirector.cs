using System.Collections.Generic;
using TheDeep.Core;
using TheDeep.Data;
using TheDeep.Footage;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.Submarine;
using TheDeep.UI.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.Audio
{
    /// <summary>
    /// Decides when sound effects play by edge-detecting state every client already has (divers'
    /// diving and dead flags, the scanner count, the expedition lists, the sub's travel), so no
    /// RPC exists just for sound. The first look at anything only records it: spawning, joining a
    /// session in progress or rehosting never plays a burst of old events.
    /// </summary>
    public class SfxDirector : MonoBehaviour
    {
        const float CrushSeconds = 10f; // DiverHealth's time from "too deep" to crushed
        const float LowAir = 0.25f;
        const float DockDelay = 0.4f; // covers the end-of-expedition RPC and list deltas arriving on different ticks
        const int AlarmNone = 0, AlarmCrush = 1, AlarmAir = 2;

        /// <summary>Last seen state of another player.</summary>
        class Crewmate
        {
            public DiverController Diver;
            public DiverHealth Health;
            public bool Known, Diving, Dead;
        }

        readonly Dictionary<PlayerNetwork, Crewmate> crew = new();
        readonly List<PlayerNetwork> departed = new();
        float nextCrewScan;

        // The local player.
        PlayerNetwork local;
        DiverController localDiver;
        DiverHealth localHealth;
        DiverScanner localScanner;
        bool localKnown, wasDiving, wasDead;
        int lastHeld, alarm;
        float crushTime, nextAlarm, nextCrack, lastUpload = -10f, pendingDock = -1f, lastTurnover = -10f;

        // The expedition and the sub.
        bool worldKnown, chipsKnown, travelKnown, wasTravelling;
        int lastPacketId, lastCredits, lastLevels, lastExpedition, lastChips, lastStation;
        SfxLoop motor;
        float travelStart, motorStopStart = -1f, motorStopPitch, pingAt = -1f;
        ChipReader reader;

        void OnEnable()
        {
            RetroUI.ButtonClicked += OnButtonClicked;
            RetroUI.DisabledClicked += OnDisabledClicked;
        }

        void OnDisable()
        {
            RetroUI.ButtonClicked -= OnButtonClicked;
            RetroUI.DisabledClicked -= OnDisabledClicked;
            Forget();
        }

        static void OnButtonClicked(Button button) => SfxPlayer.Play(Sfx.Click, 0.2f, Random.Range(0.95f, 1.05f));

        static void OnDisabledClicked() => SfxPlayer.Play(Sfx.Bonk, 0.3f);

        void Update()
        {
            var state = ExpeditionState.Instance;
            var progress = CrewProgress.Instance;
            if (state == null || !state.IsSpawned || progress == null || !progress.IsSpawned)
            {
                Forget(); // menus, leaving, rehosting: everything is new again next time
                return;
            }
            UpdateLocal();
            UpdateCrew();
            UpdateExpedition(state, progress);
            UpdateDockedUpload();
            UpdateChips();
            UpdateTravel();
        }

        /// <summary>Back to "never seen anything": the next observation of each thing only records it.</summary>
        void Forget()
        {
            crew.Clear();
            localKnown = worldKnown = chipsKnown = travelKnown = false;
            alarm = AlarmNone;
            crushTime = 0f;
            pendingDock = -1f;
            pingAt = -1f;
            if (motor != null) motor.Stop(0f);
            motor = null;
        }

        bool LocalAboard => local != null && localDiver != null && !localDiver.IsDiving;

        static bool TooDeep(float y)
        {
            var progress = CrewProgress.Instance;
            int level = progress != null ? progress.Level(UpgradeType.DepthRating) : 0;
            return WorldInfo.DepthAt(y) > UpgradeCatalog.DepthRating(level);
        }

        // ------------------------------------------------------------------ the local player

        void UpdateLocal()
        {
            var net = PlayerNetwork.Local;
            if (net != local)
            {
                local = net;
                localKnown = false;
                localDiver = net != null ? net.GetComponent<DiverController>() : null;
                localHealth = net != null ? net.GetComponent<DiverHealth>() : null;
                localScanner = net != null ? net.GetComponent<DiverScanner>() : null;
            }
            if (local == null || localDiver == null || !local.IsSpawned) return;

            bool diving = localDiver.IsDiving;
            bool dead = localHealth != null && localHealth.IsDead;
            int held = localScanner != null ? localScanner.HeldCount : 0;
            bool tooDeep = TooDeep(local.transform.position.y);
            if (!localKnown)
            {
                localKnown = true;
                wasDiving = diving;
                wasDead = dead;
                lastHeld = held;
                alarm = AlarmNone;
                crushTime = 0f;
                pendingDock = -1f;
                return;
            }

            // Through the hatch.
            if (diving != wasDiving)
            {
                SfxPlayer.Play(diving ? Sfx.Splash : Sfx.Drain, 0.5f);
                SfxPlayer.Play(Sfx.Clank, 0.4f);
            }

            // Death sting: crushed or drowned.
            if (dead && !wasDead) SfxPlayer.Play(tooDeep ? Sfx.Implosion : Sfx.BubbleTorrent, 0.7f);

            // Scanner: data recorded, radioed in, uploaded aboard, or nothing to send.
            bool alive = !dead;
            if (held > lastHeld)
            {
                if (diving && alive) SfxPlayer.Play(Sfx.ScanDone, 0.3f);
            }
            else if (held < lastHeld)
            {
                if (!diving)
                {
                    // Plugged in aboard, or scans thrown away because the expedition ended (the same
                    // frame pulls the diver aboard): wait a moment and stay quiet if it turned over.
                    if (Time.time - lastTurnover > DockDelay) pendingDock = Time.time + DockDelay;
                    lastUpload = Time.time; // their own packets arriving aren't news
                }
                else if (alive && held == 0)
                    Upload(SignalModel.Strength(local.transform.position) < SignalModel.CorruptionThreshold ? Sfx.TransmitBroken : Sfx.TransmitClean);
            }
            else if (diving && alive && local.Controller != null && !local.Controller.InputLocked && Controls.Pressed(GameAction.Transmit)
                     && (held == 0 || SignalModel.Strength(local.transform.position) <= SignalModel.NoSignalThreshold))
            {
                // Same frame as the scanner: if it did transmit, the count dropped and the branch above ran instead.
                SfxPlayer.Play(Sfx.ErrorBuzz, 0.3f);
            }

            UpdateAlarms(diving && alive, tooDeep, localHealth != null ? localHealth.Air01 : 1f);
            wasDiving = diving;
            wasDead = dead;
            lastHeld = held;
        }

        void Upload(Sfx sound)
        {
            SfxPlayer.Play(sound, 0.3f);
            lastUpload = Time.time;
        }

        void UpdateDockedUpload()
        {
            if (pendingDock < 0f || Time.time < pendingDock) return;
            pendingDock = -1f;
            Upload(Sfx.TransmitClean);
        }

        /// <summary>
        /// Suit alarms. Beyond the rating: a klaxon twice a second plus hull cracks that speed up from
        /// 1 to 8 a second over the crush time. Otherwise low air: a helmet beep that speeds up as the
        /// air runs out. Keeps going while paused (the air still drains); stops aboard or dead.
        /// </summary>
        void UpdateAlarms(bool active, bool tooDeep, float air01)
        {
            float now = Time.time;
            int want = !active ? AlarmNone : tooDeep ? AlarmCrush : air01 < LowAir ? AlarmAir : AlarmNone;
            if (want != alarm)
            {
                alarm = want;
                nextAlarm = now;
                nextCrack = now + Random.Range(0.3f, 1f);
            }
            crushTime = !active ? 0f : tooDeep ? crushTime + Time.deltaTime : Mathf.Max(0f, crushTime - Time.deltaTime * 2f);

            if (alarm == AlarmCrush)
            {
                if (now >= nextAlarm)
                {
                    SfxPlayer.Play(Sfx.Klaxon, 0.3f);
                    nextAlarm = now + 0.5f;
                }
                if (now >= nextCrack)
                {
                    SfxPlayer.Play(Sfx.Crack, Random.Range(0.3f, 0.6f), Random.Range(0.85f, 1.15f));
                    float rate = Mathf.Lerp(1f, 8f, crushTime / CrushSeconds);
                    nextCrack = now + Random.Range(0.5f, 1.5f) / rate;
                }
            }
            else if (alarm == AlarmAir && now >= nextAlarm)
            {
                SfxPlayer.Play(Sfx.AirBeep, 0.3f);
                nextAlarm = now + Mathf.Lerp(3f, 0.5f, 1f - air01 / LowAir);
            }
        }

        // ------------------------------------------------------------------ other players

        void UpdateCrew()
        {
            if (Time.unscaledTime >= nextCrewScan)
            {
                nextCrewScan = Time.unscaledTime + 1f;
                departed.Clear();
                foreach (var pair in crew)
                    if (pair.Key == null) departed.Add(pair.Key);
                foreach (var gone in departed) crew.Remove(gone);
                foreach (var net in FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                    if (net.IsSpawned && !net.IsOwner && !crew.ContainsKey(net))
                        crew.Add(net, new Crewmate { Diver = net.GetComponent<DiverController>(), Health = net.GetComponent<DiverHealth>() });
            }

            foreach (var pair in crew)
            {
                var net = pair.Key;
                var mate = pair.Value;
                if (net == null || !net.IsSpawned || mate.Diver == null) continue;
                bool diving = mate.Diver.IsDiving;
                bool dead = mate.Health != null && mate.Health.IsDead;
                if (mate.Known)
                {
                    if (diving != mate.Diving)
                    {
                        Vector3 at = net.transform.position;
                        SfxPlayer.PlayAt(diving ? Sfx.Splash : Sfx.Drain, at, 0.6f, 10f);
                        SfxPlayer.PlayAt(Sfx.Clank, at, 0.5f, 10f);
                    }
                    if (dead && !mate.Dead) SfxPlayer.Play(Sfx.NoVitals, 0.3f);
                }
                mate.Known = true;
                mate.Diving = diving;
                mate.Dead = dead;
            }
        }

        // ------------------------------------------------------------------ expedition

        void UpdateExpedition(ExpeditionState state, CrewProgress progress)
        {
            int maxId = 0;
            bool corrupted = false;
            var packets = state.Packets;
            for (int i = 0; i < packets.Count; i++)
            {
                var p = packets[i];
                if (p.Id <= maxId) continue;
                maxId = p.Id;
                corrupted = p.Status == PacketStatus.Corrupted;
            }
            int credits = state.Credits;
            int levels = 0;
            for (int i = 0; i < UpgradeCatalog.Count; i++) levels += progress.Level((UpgradeType)i);
            int expedition = progress.Expedition;

            if (worldKnown)
            {
                if (expedition > lastExpedition || maxId < lastPacketId)
                {
                    // A new expedition: scans still held when it ended were discarded, not uploaded.
                    lastTurnover = Time.time;
                    pendingDock = -1f;
                }
                bool aboard = LocalAboard;
                // Data arrived (the diver who just uploaded it aboard already heard their own upload).
                if (maxId > lastPacketId && aboard && Time.time - lastUpload > 1.5f)
                    SfxPlayer.Play(corrupted ? Sfx.DataInCorrupt : Sfx.DataIn, 0.3f);
                if (credits > lastCredits && aboard) SfxPlayer.Play(Sfx.KaChunk, 0.4f);
                if (levels > lastLevels) SfxPlayer.Play(Sfx.Purchase, 0.3f);
                if (expedition > lastExpedition) SfxPlayer.Play(Sfx.Foghorn, 0.5f);
            }
            // Drops (a new expedition, a rehost) just move the baseline, silently.
            worldKnown = true;
            lastPacketId = maxId;
            lastCredits = credits;
            lastLevels = levels;
            lastExpedition = expedition;
        }

        void UpdateChips()
        {
            var archive = FootageArchive.Instance;
            if (archive == null || !archive.IsSpawned)
            {
                chipsKnown = false;
                return;
            }
            int inserted = 0;
            var chips = archive.Chips;
            for (int i = 0; i < chips.Count; i++)
                if (chips[i].Status == ChipStatus.Inserted) inserted++;
            if (chipsKnown && inserted > lastChips)
            {
                if (reader == null) reader = FindFirstObjectByType<ChipReader>();
                if (reader != null) SfxPlayer.PlayAt(Sfx.ChipInsert, reader.transform.position, 0.5f, 6f);
            }
            chipsKnown = true;
            lastChips = inserted;
        }

        // ------------------------------------------------------------------ sub travel

        void UpdateTravel()
        {
            var nav = SubNavigation.Instance;
            bool travelling = nav != null && nav.IsSpawned && nav.Travelling;
            if (!travelKnown)
            {
                travelKnown = true;
                wasTravelling = travelling;
                lastStation = nav != null ? nav.Current : 0;
                return;
            }

            bool aboard = LocalAboard;
            if (travelling && !wasTravelling && aboard) StartTravel(nav);
            else if (!travelling && wasTravelling) StopMotor();

            if (motor != null)
            {
                float now = Time.time;
                if (motorStopStart < 0f)
                {
                    motor.Pitch = Mathf.Lerp(0.6f, 1.2f, (now - travelStart) / 1.5f); // spin up, then hold
                }
                else
                {
                    float k = now - motorStopStart; // spin down while it fades out over 1 s
                    motor.Pitch = Mathf.Lerp(motorStopPitch, 0.7f, k);
                    if (k >= 1f) motor = null;
                }
            }
            if (pingAt >= 0f && Time.time >= pingAt)
            {
                pingAt = -1f;
                if (travelling && aboard) SfxPlayer.Play(Sfx.SonarPing, 0.35f); // just before the view fades back in
            }

            if (!travelling && nav != null) lastStation = nav.Current;
            wasTravelling = travelling;
        }

        void StartTravel(SubNavigation nav)
        {
            var stations = nav.Stations;
            int to = nav.Current;
            bool down = HasPoint(stations, to) && HasPoint(stations, lastStation)
                        && SubNavigation.DepthOf(stations[to]) > SubNavigation.DepthOf(stations[lastStation]);
            SfxPlayer.Play(down ? Sfx.BallastFlood : Sfx.BallastBlow, 0.45f);
            if (motor != null) motor.Stop(0.3f);
            motor = SfxPlayer.Loop(Sfx.Motor, 0.35f);
            motor.Pitch = 0.6f;
            travelStart = Time.time;
            motorStopStart = -1f;
            pingAt = travelStart + 3.5f;
        }

        void StopMotor()
        {
            if (motor == null || motorStopStart >= 0f) return;
            motor.Stop(1f);
            motorStopStart = Time.time;
            motorStopPitch = motor.Pitch;
        }

        static bool HasPoint(SubNavigation.Station[] stations, int index) =>
            stations != null && index >= 0 && index < stations.Length && stations[index] != null && stations[index].point != null;
    }
}
