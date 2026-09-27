using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace TheDeep.Networking
{
    /// <summary>
    /// Starts and stops multiplayer sessions.
    /// Online: private Unity Relay/Lobby session, friends join with a short code (max 5 players).
    /// Local: direct connection by IP (same PC / LAN) for testing without Unity Cloud.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public const int MaxPlayers = 5;
        const ushort LocalPort = 7777;
        const int MaxCodeLength = 8;

        ISession session;
        bool leaving;

        public static SessionManager Instance { get; private set; }

        /// <summary>Code friends type in to join. Null for local sessions.</summary>
        public string JoinCode => session?.Code;
        public bool IsOnline => session != null;
        public bool IsBusy { get; private set; }
        /// <summary>A LEAVE is in progress (online, leaving the session can take a moment).</summary>
        public bool IsLeaving => leaving;
        /// <summary>The last session ended because this player chose to leave (not a drop or a host shutdown).</summary>
        public bool LeftOnPurpose { get; private set; }

        /// <summary>Human-readable progress/error messages for the menu.</summary>
        public event Action<string> StatusChanged;

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true; // other players keep ticking when this window isn't focused
        }

        void Start()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped += OnClientStopped;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }

        public async Task<bool> HostOnline()
        {
            if (AlreadyInSession()) return false;
            LeftOnPurpose = false;
            return await Run("Creating session...", async () =>
            {
                await EnsureSignedIn();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (LeftWhileStarting("Session closed.")) return;
                Status($"Session created. Join code: {session.Code}");
            });
        }

        public async Task<bool> JoinOnline(string code)
        {
            if (AlreadyInSession()) return false;
            LeftOnPurpose = false;
            code = CleanJoinCode(code);
            if (code.Length == 0)
            {
                Status("Type the join code first.");
                return false;
            }
            return await Run($"Joining {code}...", async () =>
            {
                await EnsureSignedIn();
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                if (LeftWhileStarting("Connection cancelled.")) return;
                Status("Joined.");
            });
        }

        /// <summary>
        /// Pulls the code out of whatever was typed or pasted: " abc-123 ", "ABC 123" and
        /// "Join code: ABC123" all give "ABC123".
        /// </summary>
        public static string CleanJoinCode(string input)
        {
            string[] tokens = Regex.Split(input ?? "", "[^A-Za-z0-9]+").Where(t => t.Length > 0).ToArray();
            string joined = string.Concat(tokens);
            string code = joined.Length <= MaxCodeLength ? joined : tokens[tokens.Length - 1];
            return code.ToUpperInvariant();
        }

        public bool HostLocal()
        {
            if (AlreadyInSession()) return false;
            LeftOnPurpose = false;
            Transport.SetConnectionData("127.0.0.1", LocalPort, "0.0.0.0");
            bool ok = NetworkManager.Singleton.StartHost();
            Status(ok ? $"Hosting locally on port {LocalPort}." : "Could not start local host (port in use?).");
            return ok;
        }

        public bool JoinLocal(string address)
        {
            if (AlreadyInSession()) return false;
            LeftOnPurpose = false;
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Transport.SetConnectionData(address, LocalPort);
            // Give up on an unreachable address after about 10 s (the transport default is about a minute).
            Transport.ConnectTimeoutMS = 1000;
            Transport.MaxConnectAttempts = 10;
            bool ok = NetworkManager.Singleton.StartClient();
            Status(ok ? $"Connecting to {address}..." : "Could not start client.");
            return ok;
        }

        public async void Leave()
        {
            if (leaving) return;
            leaving = true;
            LeftOnPurpose = true;
            // Forget the session before awaiting: leaving stops the network, and the OnClientStopped
            // raised in the middle of LeaveAsync must not try to leave it a second time.
            var leavingSession = session;
            session = null;
            try
            {
                if (leavingSession != null) await leavingSession.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] Leave failed: {e.Message}");
            }
            finally
            {
                leaving = false;
            }
            // The Multiplayer SDK owns the NetworkManager for online sessions, so only shut it down ourselves if it's still running.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            Status("Left the session.");
        }

        void OnClientStopped(bool wasHost) => DropSession();

        /// <summary>The network stopped (host left, connection lost): leave the online session too, so IsOnline and JoinCode don't go stale.</summary>
        async void DropSession()
        {
            var dropped = session;
            session = null;
            if (dropped == null) return;
            try
            {
                await dropped.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] Could not leave the dropped session: {e.Message}");
            }
        }

        /// <summary>
        /// Leave() ran while the SDK was still starting (the session wasn't ours yet, so it only stopped the network):
        /// leave the session that just arrived too, instead of keeping a stale IsOnline/JoinCode.
        /// </summary>
        bool LeftWhileStarting(string message)
        {
            if (!LeftOnPurpose) return false;
            DropSession();
            Status(message);
            return true;
        }

        bool AlreadyInSession()
        {
            if (!IsBusy && !leaving && !NetworkManager.Singleton.IsListening) return false;
            Status("Already in a session - leave first.");
            return true;
        }

        static UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();

        static async Task EnsureSignedIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // A fresh profile per run so several copies on one PC (e.g. Multiplayer Play Mode)
                // don't all sign in as the same player.
                var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 8));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async Task<bool> Run(string startMessage, Func<Task> action)
        {
            if (IsBusy) return false;
            IsBusy = true;
            Status(startMessage);
            try
            {
                await action();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] {e}");
                session = null;
                // Esc on CONNECTING stops the network under the SDK, which then fails the start with its own error.
                Status(LeftOnPurpose ? "Connection cancelled." : Friendly(e));
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        static string Friendly(Exception e)
        {
            // The useful reason is often an inner exception, so search the whole chain.
            string all = e.ToString();
            if (all.Contains("UnityProjectNotLinkedException") || all.Contains("link your Unity project"))
                return "Online play needs this project linked to Unity Cloud (Edit > Project Settings > Services). Use LOCAL for now.";
            if (all.Contains("not found", StringComparison.OrdinalIgnoreCase) || all.Contains("SessionNotFound"))
                return "No session with that code.";
            return "Failed: " + e.GetBaseException().Message;
        }

        void Status(string message)
        {
            Debug.Log($"[Session] {message}");
            StatusChanged?.Invoke(message);
        }
    }
}
