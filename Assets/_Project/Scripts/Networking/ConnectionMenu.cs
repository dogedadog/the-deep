using System.Threading.Tasks;
using TheDeep.Core;
using TheDeep.Data;
using TheDeep.Player;
using TheDeep.Progression;
using TheDeep.UI;
using TheDeep.UI.Terminal;
using TheDeep.Voice;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDeep.Networking
{
    /// <summary>
    /// Title/connect screen shown over the orbiting sub camera, a small session readout while
    /// playing, and an Esc pause panel (resume / settings / leave).
    /// </summary>
    public class ConnectionMenu : MonoBehaviour
    {
        [SerializeField] ExteriorPreviewCamera menuCamera;

        GameObject menuPanel, pausePanel;
        SettingsMenu settings;
        SaveSlotMenu saveSlots;
        Text banner;
        float bannerUntil;
        InputField codeField, addressField;
        Text statusText, sessionText, pauseInfo, copyCodeLabel, leaveLabel;
        Button hostOnlineButton, joinOnlineButton, hostLocalButton, joinLocalButton, copyCodeButton;
        bool paused;
        /// <summary>Our own connection was accepted; until then the menu stays up with a CONNECTING status.</summary>
        bool everConnected;
        string connectingTo = "";
        bool joiningOnline;
        float leaveConfirmUntil, copiedUntil;

        SessionManager Sessions => SessionManager.Instance;
        static bool Connected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        /// <summary>Connected and in the game, not still connecting.</summary>
        bool InGame => Connected && everConnected;
        /// <summary>HOST and JOIN only work from the title screen with nothing in progress.</summary>
        bool CanStart => !Connected && !Sessions.IsBusy && !Sessions.IsLeaving;

        void Start()
        {
            BuildUI();
            Sessions.StatusChanged += message => statusText.text = message;
            NetworkManager.Singleton.OnClientStopped += OnDisconnected;
            // Hide the menu once our own connection is accepted (NGO also raises this for the host's own id),
            // not when the client merely starts: a wrong address would give a long black screen.
            NetworkManager.Singleton.OnClientConnectedCallback += id =>
            {
                RefreshSessionText();
                if (id == NetworkManager.Singleton.LocalClientId) OnConnected();
            };
            NetworkManager.Singleton.OnClientDisconnectCallback += _ => RefreshSessionText();
            ExpeditionState.Announced += ShowBanner;
            ShowMenu(true);
            HandleCommandLine();
        }

        void OnDestroy() => ExpeditionState.Announced -= ShowBanner;

        void ShowBanner(string message)
        {
            banner.text = message;
            bannerUntil = Time.time + 7f;
        }

        /// <summary>Host flow: pick a save slot first, then start the session on it.</summary>
        void ChooseSlotThenHost(bool online)
        {
            menuPanel.SetActive(false);
            saveSlots.Open(online ? "HOST ONLINE  -  CHOOSE EXPEDITION LOG" : "HOST LOCAL  -  CHOOSE EXPEDITION LOG",
                async (slot, startNew) =>
                {
                    SaveSystem.Use(slot, startNew);
                    menuPanel.SetActive(true);
                    if (online) await Sessions.HostOnline();
                    else Sessions.HostLocal();
                },
                () => menuPanel.SetActive(true));
        }

        void JoinLocal(string address)
        {
            connectingTo = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            joiningOnline = false;
            Sessions.JoinLocal(address);
        }

        async Task JoinOnline(string code)
        {
            connectingTo = "SESSION " + SessionManager.CleanJoinCode(code);
            joiningOnline = true;
            await Sessions.JoinOnline(code);
        }

        /// <summary>
        /// Testing shortcuts to skip the menu: -autohost / -autojoin [address] (local),
        /// -autohostonline / -joincode CODE (online).
        /// </summary>
        async void HandleCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            string Next(int i) => i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autohost") Sessions.HostLocal();
                if (args[i] == "-autojoin") JoinLocal(Next(i) ?? "127.0.0.1");
                if (args[i] == "-autohostonline") await Sessions.HostOnline();
                if (args[i] == "-joincode" && Next(i) != null) await JoinOnline(Next(i));
            }
        }

        void OnConnected()
        {
            everConnected = true;
            // SETTINGS may have been opened while connecting: don't leave it over the game with the cursor captured.
            if (settings.IsOpen) settings.Close();
            ShowMenu(false);
            RefreshSessionText();
        }

        void OnDisconnected(bool wasHost)
        {
            SetPaused(false);
            ShowMenu(true);
            string message = DisconnectMessage(wasHost);
            everConnected = false;
            if (!Sessions.IsBusy) statusText.text = message;
        }

        string DisconnectMessage(bool wasHost)
        {
            if (wasHost) return "Session closed.";
            if (Sessions.LeftOnPurpose) return everConnected ? "Left the session." : "Connection cancelled.";
            if (!everConnected)
                return joiningOnline
                    ? $"Could not connect to {connectingTo}. The host may have left."
                    : $"Could not reach a host at {connectingTo}. Check the IP and that the host is running.";
            // Without a reason from the host this is a technical "[Disconnect Event]..." string.
            string reason = NetworkManager.Singleton.DisconnectReason;
            if (!string.IsNullOrEmpty(reason) && !reason.StartsWith("[Disconnect Event]"))
                return reason.Contains("shutting down") ? "The host ended the session." : reason;
            return "Lost connection to the host (the host may have left).";
        }

        void ShowMenu(bool show)
        {
            menuPanel.SetActive(show);
            sessionText.gameObject.SetActive(!show);
            menuCamera.MenuMode = show;
            if (show) FirstPersonController.SetCursorLocked(false);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            bool esc = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            banner.gameObject.SetActive(Time.time < bannerUntil);
            if (settings.IsOpen)
            {
                if (esc && !settings.IsCapturing) settings.Close();
                return;
            }
            if (saveSlots.IsOpen)
            {
                if (esc) { saveSlots.Close(); menuPanel.SetActive(true); }
                return;
            }

            bool canStart = CanStart;
            hostOnlineButton.interactable = joinOnlineButton.interactable = canStart;
            hostLocalButton.interactable = joinLocalButton.interactable = canStart;
            if (Connected && !everConnected)
            {
                statusText.text = $"CONNECTING TO {connectingTo}...   (ESC TO CANCEL)";
                if (esc) Sessions.Leave();
                return;
            }
            if (!Connected) return;

            var player = PlayerNetwork.Local;
            // Keep the corner readout off the screen while you're outside the sub.
            sessionText.enabled = !IsDiving(player);
            // Esc pauses (also while dead or in the F2 view), unless it's being used to leave the terminal.
            if (esc && player != null && !Sessions.IsLeaving && (paused || !player.Controller.Has(FirstPersonController.Lock.Terminal)))
                SetPaused(!paused);
            if (paused) UpdatePausePanel();
            if (Time.frameCount % 30 == 0) RefreshSessionText();
        }

        void SetPaused(bool on)
        {
            paused = on;
            pausePanel.SetActive(on);
            var player = PlayerNetwork.Local;
            if (player != null) player.Controller.SetLock(FirstPersonController.Lock.Pause, on);
            FirstPersonController.SetCursorLocked(!on && Connected && (player == null || !player.Controller.Has(FirstPersonController.Lock.Terminal)));
            copiedUntil = 0f;
            copyCodeButton.gameObject.SetActive(Sessions.IsOnline);
            ResetLeaveButton();
        }

        void UpdatePausePanel()
        {
            copyCodeButton.gameObject.SetActive(Sessions.IsOnline);
            copyCodeLabel.text = Time.unscaledTime < copiedUntil ? "COPIED!" : "COPY CODE";
            if (leaveConfirmUntil > 0f)
            {
                if (Time.unscaledTime >= leaveConfirmUntil) ResetLeaveButton();
            }
            else if (Time.frameCount % 30 == 0) RefreshPauseInfo();
        }

        void RefreshPauseInfo()
        {
            string info = SessionDescription();
            if (Sessions.IsOnline) info += "\n\nShare the join code with friends so they can join.";
            var player = PlayerNetwork.Local;
            var health = player != null ? player.GetComponent<DiverHealth>() : null;
            if (IsDiving(player) && (health == null || !health.IsDead))
                info += "\n\n<color=#FFB84D>THE DIVE KEEPS GOING WHILE PAUSED - AIR STILL DRAINS.</color>";
            pauseInfo.text = info;
        }

        /// <summary>Back to the plain LEAVE button (END SESSION for the host), no confirm pending.</summary>
        void ResetLeaveButton()
        {
            leaveConfirmUntil = 0f;
            bool host = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            leaveLabel.text = host ? "END SESSION" : "LEAVE";
            leaveLabel.fontSize = host ? 16 : 18;
            leaveLabel.color = RetroUI.Ink;
            RefreshPauseInfo();
        }

        void OnLeaveClicked()
        {
            var nm = NetworkManager.Singleton;
            int crew = nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count - 1 : 0;
            // The host leaving ends the game for everyone, so that takes a second click.
            if (crew > 0 && leaveConfirmUntil == 0f)
            {
                leaveConfirmUntil = Time.unscaledTime + 5f;
                leaveLabel.text = "SURE?";
                leaveLabel.color = new Color(0.6f, 0.05f, 0.05f);
                pauseInfo.text = $"LEAVING ENDS THE SESSION FOR {crew} CREW. CLICK AGAIN.";
                return;
            }
            leaveConfirmUntil = 0f;
            pausePanel.SetActive(false);
            Sessions.Leave();
        }

        void CopyJoinCode()
        {
            if (!Sessions.IsOnline) return;
            GUIUtility.systemCopyBuffer = Sessions.JoinCode;
            copiedUntil = Time.unscaledTime + 2f;
        }

        static bool IsDiving(PlayerNetwork player)
        {
            if (player == null) return false;
            var diver = player.GetComponent<DiverController>();
            return diver != null && diver.IsDiving;
        }

        static bool EnterPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        void RefreshSessionText() => sessionText.text = SessionDescription();

        string SessionDescription()
        {
            if (!Connected) return "";
            string where = Sessions.IsOnline ? $"JOIN CODE  {Sessions.JoinCode}" : "LOCAL SESSION";
            // Every machine keeps the voice list (the host included), so clients see the crew count too.
            int count = PlayerVoice.All.Count;
            string expedition = CrewProgress.Instance != null && CrewProgress.Instance.IsSpawned ? $"EXPEDITION #{CrewProgress.Instance.Expedition}   |   " : "";
            return expedition + (count > 0 ? $"{where}   |   CREW {count}/{SessionManager.MaxPlayers}" : where);
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)canvasGo.transform;

            sessionText = RetroUI.Label("SessionInfo", root, "", 20, new Color(0.8f, 0.95f, 0.85f, 0.8f), TextAnchor.UpperRight, FontStyle.Bold);
            RetroUI.Stretch(sessionText.rectTransform, 20, 16, 24, 0);
            sessionText.gameObject.AddComponent<Shadow>();

            BuildMainMenu(root);
            BuildPauseMenu(root);
            settings = gameObject.AddComponent<SettingsMenu>();
            settings.Build(root);
            saveSlots = gameObject.AddComponent<SaveSlotMenu>();
            saveSlots.Build(root);

            banner = RetroUI.Label("Banner", root, "", 26, new Color(1f, 0.9f, 0.55f), TextAnchor.UpperCenter, FontStyle.Bold);
            RetroUI.Stretch(banner.rectTransform, 100, 90, 100, 0);
            banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
            banner.gameObject.SetActive(false);
        }

        void BuildMainMenu(RectTransform root)
        {
            menuPanel = RetroUI.Rect("MainMenu", root).gameObject;
            var panel = RetroUI.Stretch((RectTransform)menuPanel.transform);

            var title = RetroUI.Label("Title", panel, "THE DEEP", 110, new Color(0.85f, 0.95f, 0.9f), TextAnchor.UpperLeft, FontStyle.Bold);
            RetroUI.Place(title.rectTransform, 90, 80, 900, 130);
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -4);
            var subtitle = RetroUI.Label("Subtitle", panel, "DEEP SURVEY DIVISION  -  DSV ABYSSAL-3", 24, new Color(0.6f, 0.8f, 0.75f), TextAnchor.UpperLeft);
            RetroUI.Place(subtitle.rectTransform, 96, 210, 900, 30);

            var box = RetroUI.Panel("Box", panel, RetroUI.Face, raycast: true);
            RetroUI.Place(box.rectTransform, 90, 280, 560, 470);
            RetroUI.Bevel(box.rectTransform, raised: true, width: 3);
            var header = RetroUI.Panel("Header", box.transform, RetroUI.TitleBar);
            RetroUI.Place(header.rectTransform, 4, 4, 552, 34);
            var headerText = RetroUI.Label("Text", header.transform, "START EXPEDITION", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Stretch(headerText.rectTransform, 12, 0, 0, 0);

            var b = box.transform;
            Section(b, "ONLINE  (friends join with a code)", 52);
            hostOnlineButton = Place(RetroUI.Button("HostOnline", b, "HOST", () => { if (CanStart) ChooseSlotThenHost(online: true); }, 20), 20, 80, 160, 46);
            codeField = Field(b, "JOIN CODE", 200, 80, 190, 46);
            joinOnlineButton = Place(RetroUI.Button("JoinOnline", b, "JOIN", async () => { if (CanStart) await JoinOnline(codeField.text); }, 20), 400, 80, 140, 46);

            Section(b, "LOCAL  (testing on this PC / LAN, no account needed)", 150);
            hostLocalButton = Place(RetroUI.Button("HostLocal", b, "HOST", () => { if (CanStart) ChooseSlotThenHost(online: false); }, 20), 20, 178, 160, 46);
            addressField = Field(b, "127.0.0.1", 200, 178, 190, 46);
            joinLocalButton = Place(RetroUI.Button("JoinLocal", b, "JOIN", () => { if (CanStart) JoinLocal(addressField.text); }, 20), 400, 178, 140, 46);

            // Enter in a field joins, like clicking its JOIN button (focus loss alone doesn't).
            codeField.onEndEdit.AddListener(async _ => { if (EnterPressed() && CanStart) await JoinOnline(codeField.text); });
            addressField.onEndEdit.AddListener(_ => { if (EnterPressed() && CanStart) JoinLocal(addressField.text); });

            statusText = RetroUI.Readout("Status", b, "Up to 5 players. Host a session, then share the join code.", 17);
            RetroUI.Place((RectTransform)statusText.transform.parent, 20, 250, 520, 140);

            Place(RetroUI.Button("Quit", b, "QUIT", Quit, 18), 20, 405, 120, 42);
            Place(RetroUI.Button("Settings", b, "SETTINGS", () => OpenSettings(menuPanel), 18), 150, 405, 160, 42);
            var hint = RetroUI.Label("Hint", b, "Hosts pick a save slot.", 14, RetroUI.Shadow, TextAnchor.MiddleRight);
            RetroUI.Place(hint.rectTransform, 320, 405, 220, 42);
        }

        void BuildPauseMenu(RectTransform root)
        {
            var dim = RetroUI.Panel("Pause", root, new Color(0, 0, 0, 0.55f), raycast: true);
            RetroUI.Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;

            var box = RetroUI.Panel("Box", dim.transform, RetroUI.Face, raycast: true);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(480, 360);
            RetroUI.Bevel(rt, raised: true, width: 3);
            var header = RetroUI.Panel("Header", box.transform, RetroUI.TitleBar);
            RetroUI.Place(header.rectTransform, 4, 4, 472, 34);
            var headerText = RetroUI.Label("Text", header.transform, "PAUSED", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Stretch(headerText.rectTransform, 12, 0, 0, 0);

            pauseInfo = RetroUI.Readout("Info", box.transform, "", 17);
            RetroUI.Place((RectTransform)pauseInfo.transform.parent, 20, 54, 440, 150);
            copyCodeButton = Place(RetroUI.Button("CopyCode", box.transform, "COPY CODE", CopyJoinCode, 16), 20, 216, 200, 40);
            copyCodeLabel = copyCodeButton.GetComponentInChildren<Text>();
            Place(RetroUI.Button("Resume", box.transform, "RESUME", () => SetPaused(false), 18), 20, 286, 136, 50);
            Place(RetroUI.Button("Settings", box.transform, "SETTINGS", () => OpenSettings(pausePanel), 18), 172, 286, 136, 50);
            leaveLabel = Place(RetroUI.Button("Leave", box.transform, "LEAVE", OnLeaveClicked, 18), 324, 286, 136, 50).GetComponentInChildren<Text>();
            pausePanel.SetActive(false);
        }

        /// <summary>Hide <paramref name="from"/> while the settings screen is up, then bring it back.</summary>
        void OpenSettings(GameObject from)
        {
            from.SetActive(false);
            settings.Open(() => from.SetActive(from == menuPanel ? !InGame : paused));
        }

        static void Section(Transform parent, string text, float y)
        {
            var label = RetroUI.Label("Section", parent, text, 16, RetroUI.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Place(label.rectTransform, 20, y, 520, 24);
        }

        static Button Place(Button button, float x, float y, float w, float h)
        {
            RetroUI.Place(button.GetComponent<RectTransform>(), x, y, w, h);
            return button;
        }

        static InputField Field(Transform parent, string placeholder, float x, float y, float w, float h)
        {
            var bg = RetroUI.Panel("Field", parent, Color.white, raycast: true);
            RetroUI.Place(bg.rectTransform, x, y, w, h);
            RetroUI.Bevel(bg.rectTransform, raised: false);
            var text = RetroUI.Label("Text", bg.transform, "", 22, RetroUI.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            RetroUI.Stretch(text.rectTransform, 8, 4, 8, 4);
            text.supportRichText = false;
            var hint = RetroUI.Label("Placeholder", bg.transform, placeholder, 20, RetroUI.Shadow, TextAnchor.MiddleCenter, FontStyle.Italic);
            RetroUI.Stretch(hint.rectTransform, 8, 4, 8, 4);
            var field = bg.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = 24;
            return field;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
