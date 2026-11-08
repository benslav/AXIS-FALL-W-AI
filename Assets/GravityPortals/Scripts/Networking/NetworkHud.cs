using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Minimal connect menu: host, join or run a dedicated server.</summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkHud : MonoBehaviour
    {
        public string address = "127.0.0.1";
        public string port = "7777";

        NetworkManager manager;
        string status = "";

        void Awake()
        {
            manager = GetComponent<NetworkManager>();
            manager.OnClientDisconnectCallback += id =>
            {
                if (!manager.IsServer && id == manager.LocalClientId)
                    status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected." : manager.DisconnectReason;
            };
        }

        void OnGUI()
        {
            const float w = 270f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 10f, 10f, w, 260f), GUI.skin.box);

            if (!manager.IsClient && !manager.IsServer)
            {
                GUILayout.Label("<b>GRAVITY PORTALS</b>  multiplayer", Rich());
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60));
                address = GUILayout.TextField(address);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", GUILayout.Width(60));
                port = GUILayout.TextField(port);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Host (server + play)")) Begin(() => manager.StartHost(), listenAll: true);
                if (GUILayout.Button("Join")) Begin(() => manager.StartClient(), listenAll: false);
                if (GUILayout.Button("Dedicated server (no player)")) Begin(() => manager.StartServer(), listenAll: true);
                if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
            }
            else
            {
                string mode = manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client";
                int players = FindObjectsByType<NetworkGravityPlayer>(FindObjectsSortMode.None).Length;
                GUILayout.Label($"{mode}   players: {players}");
                if (manager.IsClient && !manager.IsServer && !manager.IsConnectedClient) GUILayout.Label("Connecting...");

                if (manager.IsServer)
                    GUILayout.Label($"Rejected updates (all players): {TotalRejections()}");

                if (GUILayout.Button("Disconnect")) manager.Shutdown();
            }

            GUILayout.EndArea();
        }

        void Begin(System.Func<bool> start, bool listenAll)
        {
            var transport = manager.GetComponent<UnityTransport>();
            if (!ushort.TryParse(port, out ushort p)) p = 7777;
            transport.SetConnectionData(address.Trim(), p, listenAll ? "0.0.0.0" : null);
            status = start() ? "" : "Could not start. Is the port in use?";
        }

        static int TotalRejections()
        {
            int total = 0;
            foreach (var p in FindObjectsByType<NetworkGravityPlayer>(FindObjectsSortMode.None)) total += p.Rejections;
            return total;
        }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }
    }
}
