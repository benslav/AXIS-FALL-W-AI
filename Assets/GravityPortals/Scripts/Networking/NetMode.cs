using Unity.Netcode;

namespace GravityPortals
{
    /// <summary>Who runs what. Offline counts as "server": everything simulates locally.</summary>
    public static class NetMode
    {
        public static bool IsOnline => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public static bool IsServer => !IsOnline || NetworkManager.Singleton.IsServer;
        public static bool IsClientOnly => IsOnline && !NetworkManager.Singleton.IsServer;
    }
}
