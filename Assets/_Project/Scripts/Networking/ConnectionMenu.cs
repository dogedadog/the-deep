using TheDeep.Core;
using TheDeep.Player;
using TheDeep.UI.Terminal;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDeep.Networking
{
    /// <summary>
    /// Title/connect screen shown over the orbiting sub camera, a small session readout while
    /// playing, and an Esc pause panel (resume / leave).
    /// </summary>
    public class ConnectionMenu : MonoBehaviour
    {
        [SerializeField] ExteriorPreviewCamera menuCamera;

        GameObject menuPanel, pausePanel;
        InputField codeField, addressField;
        Text statusText, sessionText, pauseInfo;
        bool paused;

        SessionManager Sessions => SessionManager.Instance;
        static bool Connected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Start()
        {
            BuildUI();
            Sessions.StatusChanged += message => statusText.text = message;
            NetworkManager.Singleton.OnClientStarted += OnConnected;
            NetworkManager.Singleton.OnClientStopped += OnDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += _ => RefreshSessionText();
            NetworkManager.Singleton.OnClientDisconnectCallback += _ => RefreshSessionText();
            ShowMenu(true);
            HandleCommandLine();
        }

        /// <summary>Testing shortcut: launch a build with -autohost, or -autojoin [address], to skip the menu.</summary>
        void HandleCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autohost") Sessions.HostLocal();
                if (args[i] == "-autojoin") Sessions.JoinLocal(i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "127.0.0.1");
            }
        }

        void OnConnected()
        {
            ShowMenu(false);
            RefreshSessionText();
        }

        void OnDisconnected(bool wasHost)
        {
            SetPaused(false);
            ShowMenu(true);
            if (!Sessions.IsBusy) statusText.text = "Disconnected.";
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
            if (!Connected) return;
            var keyboard = Keyboard.current;
            var player = PlayerNetwork.Local;
            // Esc pauses, unless it's being used to leave the terminal (which locks input first).
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && player != null && (paused || !player.Controller.InputLocked))
                SetPaused(!paused);
            if (Time.frameCount % 30 == 0) RefreshSessionText();
        }

        void SetPaused(bool on)
        {
            paused = on;
            pausePanel.SetActive(on);
            var player = PlayerNetwork.Local;
            if (player != null) player.Controller.InputLocked = on;
            FirstPersonController.SetCursorLocked(!on && Connected);
            if (on) pauseInfo.text = SessionDescription() + "\n\nShare the join code with friends so they can join.";
        }

        void RefreshSessionText() => sessionText.text = SessionDescription();

        string SessionDescription()
        {
            if (!Connected) return "";
            string where = Sessions.IsOnline ? $"JOIN CODE  {Sessions.JoinCode}" : "LOCAL SESSION";
            int count = NetworkManager.Singleton.IsServer ? NetworkManager.Singleton.ConnectedClientsIds.Count : -1;
            return count > 0 ? $"{where}   |   CREW {count}/{SessionManager.MaxPlayers}" : where;
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
            Place(RetroUI.Button("HostOnline", b, "HOST", async () => await Sessions.HostOnline(), 20), 20, 80, 160, 46);
            codeField = Field(b, "JOIN CODE", 200, 80, 190, 46);
            Place(RetroUI.Button("JoinOnline", b, "JOIN", async () => await Sessions.JoinOnline(codeField.text), 20), 400, 80, 140, 46);

            Section(b, "LOCAL  (testing on this PC / LAN, no account needed)", 150);
            Place(RetroUI.Button("HostLocal", b, "HOST", () => Sessions.HostLocal(), 20), 20, 178, 160, 46);
            addressField = Field(b, "127.0.0.1", 200, 178, 190, 46);
            Place(RetroUI.Button("JoinLocal", b, "JOIN", () => Sessions.JoinLocal(addressField.text), 20), 400, 178, 140, 46);

            statusText = RetroUI.Readout("Status", b, "Up to 5 players. Host a session, then share the join code.", 17);
            RetroUI.Place((RectTransform)statusText.transform.parent, 20, 250, 520, 140);

            Place(RetroUI.Button("Quit", b, "QUIT", Quit, 18), 20, 405, 120, 42);
            var hint = RetroUI.Label("Hint", b, "Save slots arrive with the economy step.", 14, RetroUI.Shadow, TextAnchor.MiddleRight);
            RetroUI.Place(hint.rectTransform, 150, 405, 390, 42);
        }

        void BuildPauseMenu(RectTransform root)
        {
            var dim = RetroUI.Panel("Pause", root, new Color(0, 0, 0, 0.55f), raycast: true);
            RetroUI.Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;

            var box = RetroUI.Panel("Box", dim.transform, RetroUI.Face, raycast: true);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(480, 300);
            RetroUI.Bevel(rt, raised: true, width: 3);
            var header = RetroUI.Panel("Header", box.transform, RetroUI.TitleBar);
            RetroUI.Place(header.rectTransform, 4, 4, 472, 34);
            var headerText = RetroUI.Label("Text", header.transform, "PAUSED", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Stretch(headerText.rectTransform, 12, 0, 0, 0);

            pauseInfo = RetroUI.Readout("Info", box.transform, "", 17);
            RetroUI.Place((RectTransform)pauseInfo.transform.parent, 20, 54, 440, 150);
            Place(RetroUI.Button("Resume", box.transform, "RESUME", () => SetPaused(false), 20), 20, 226, 200, 50);
            Place(RetroUI.Button("Leave", box.transform, "LEAVE SESSION", () => Sessions.Leave(), 20), 260, 226, 200, 50);
            pausePanel.SetActive(false);
        }

        static void Section(Transform parent, string text, float y)
        {
            var label = RetroUI.Label("Section", parent, text, 16, RetroUI.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            RetroUI.Place(label.rectTransform, 20, y, 520, 24);
        }

        static void Place(Button button, float x, float y, float w, float h) =>
            RetroUI.Place(button.GetComponent<RectTransform>(), x, y, w, h);

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
