using Unity.Netcode;

namespace TheDeep.Core
{
    /// <summary>
    /// Time that's the same on every machine in a session (the server's clock), so things driven by
    /// it, like drifting creatures, line up for all players. Falls back to local time offline.
    /// </summary>
    public static class SharedClock
    {
        public static float Time
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? (float)nm.ServerTime.Time : UnityEngine.Time.time;
            }
        }
    }
}
