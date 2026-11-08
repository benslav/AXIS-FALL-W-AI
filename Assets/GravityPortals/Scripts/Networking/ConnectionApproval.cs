using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Who may join a game. Joining players send their build version and name; the host turns
    /// away other versions (with a message telling them to update) and full lobbies, and keeps
    /// each player's name for the lobby, scoreboard and kill feed.
    /// </summary>
    public static class ConnectionApproval
    {
        public const int MaxPlayers = 8;
        const string NameKey = "PlayerName";

        static readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();

        /// <summary>This player's chosen name (saved between sessions).</summary>
        public static string LocalName
        {
            get
            {
                string n = PlayerPrefs.GetString(NameKey, "");
                return string.IsNullOrWhiteSpace(n) ? $"Player{Random.Range(100, 999)}" : n;
            }
            set => PlayerPrefs.SetString(NameKey, Clean(value));
        }

        public static void Setup(NetworkManager manager)
        {
            manager.NetworkConfig.ConnectionApproval = true;
            RefreshPayload(manager);
            if (manager.ConnectionApprovalCallback == null) manager.ConnectionApprovalCallback = Approve;
        }

        /// <summary>Call before hosting or joining, so a name changed in the menu is sent.</summary>
        public static void RefreshPayload(NetworkManager manager) =>
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes($"{Application.version}\n{LocalName}");

        /// <summary>Server: the name a client joined with.</summary>
        public static string NameOf(ulong clientId) =>
            names.TryGetValue(clientId, out var n) ? n : $"Player {clientId + 1}";

        public static void Forget(ulong clientId) => names.Remove(clientId);

        static void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string payload = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
            string[] parts = payload.Split('\n');
            string version = parts.Length > 0 ? parts[0] : "unknown";
            string name = parts.Length > 1 ? Clean(parts[1]) : "";

            var manager = NetworkManager.Singleton;
            bool isHost = manager != null && request.ClientNetworkId == NetworkManager.ServerClientId;

            if (version != Application.version)
            {
                Reject(response, $"Version mismatch: the host is on v{Application.version}, you're on v{version}. Download the latest build.");
                return;
            }
            if (!isHost && manager != null && manager.ConnectedClientsIds.Count >= MaxPlayers)
            {
                Reject(response, "That lobby is full.");
                return;
            }

            names[request.ClientNetworkId] = string.IsNullOrEmpty(name) ? $"Player {request.ClientNetworkId + 1}" : name;
            response.Approved = true;
            // Lobbies spawn players themselves when a map starts; plain map scenes spawn right away.
            response.CreatePlayerObject = manager != null && manager.NetworkConfig.PlayerPrefab != null;
        }

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = reason;
        }

        static string Clean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            name = name.Replace("\n", " ").Trim();
            return name.Length > 16 ? name.Substring(0, 16) : name;
        }
    }
}
