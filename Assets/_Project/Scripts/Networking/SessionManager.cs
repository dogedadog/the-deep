using System;
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

        ISession session;

        public static SessionManager Instance { get; private set; }

        /// <summary>Code friends type in to join. Null for local sessions.</summary>
        public string JoinCode => session?.Code;
        public bool IsOnline => session != null;
        public bool IsBusy { get; private set; }

        /// <summary>Human-readable progress/error messages for the menu.</summary>
        public event Action<string> StatusChanged;

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true; // other players keep ticking when this window isn't focused
        }

        public async Task<bool> HostOnline()
        {
            return await Run("Creating session...", async () =>
            {
                await EnsureSignedIn();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Status($"Session created. Join code: {session.Code}");
            });
        }

        public async Task<bool> JoinOnline(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                Status("Type the join code first.");
                return false;
            }
            return await Run($"Joining {code}...", async () =>
            {
                await EnsureSignedIn();
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Status("Joined.");
            });
        }

        public bool HostLocal()
        {
            Transport.SetConnectionData("127.0.0.1", LocalPort, "0.0.0.0");
            bool ok = NetworkManager.Singleton.StartHost();
            Status(ok ? $"Hosting locally on port {LocalPort}." : "Could not start local host (port in use?).");
            return ok;
        }

        public bool JoinLocal(string address)
        {
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Transport.SetConnectionData(address, LocalPort);
            bool ok = NetworkManager.Singleton.StartClient();
            Status(ok ? $"Connecting to {address}..." : "Could not start client.");
            return ok;
        }

        public async void Leave()
        {
            try
            {
                if (session != null) await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] Leave failed: {e.Message}");
            }
            session = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            Status("Left the session.");
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
                Status(Friendly(e));
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
