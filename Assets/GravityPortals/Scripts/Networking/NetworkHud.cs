using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPortals
{
    /// <summary>
    /// Connect menu (host, join, dedicated server, map choice) plus command-line launch,
    /// so the same build runs as a desktop game or a headless server on Windows or Linux.
    ///
    /// Command line (all optional):
    ///   -server | -host | -client     start that mode right away (batch mode defaults to -server)
    ///   -ip &lt;address&gt;               address to join (client) — default 127.0.0.1
    ///   -port &lt;number&gt;              UDP port — default 7777
    ///   -map &lt;name&gt;                 map to load first, e.g. "GravityArena" or "atrium"
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkHud : MonoBehaviour
    {
        public string address = "127.0.0.1";
        public string port = "7777";

        enum Mode { None, Server, Host, Client }

        // Survive the scene load when a map is picked, so the new scene's menu carries on.
        static bool commandLineRead;
        static Mode pendingMode = Mode.None;
        static string pendingAddress, pendingPort;

        NetworkManager manager;
        string status = "";
        static readonly List<(string name, string path)> maps = new List<(string, string)>();

        void Awake()
        {
            manager = GetComponent<NetworkManager>();

            // Every map scene carries its own NetworkManager. One survives scene loads
            // (Netcode keeps it alive), so drop the copy that arrives with a new scene.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton != manager)
            {
                Destroy(gameObject);
                return;
            }

            manager.OnClientDisconnectCallback += id =>
            {
                if (!manager.IsServer && id == manager.LocalClientId)
                    status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected." : manager.DisconnectReason;
            };
            FindMaps();
        }

        void Start()
        {
            if (manager == null || NetworkManager.Singleton != manager) return;

            if (!commandLineRead)
            {
                commandLineRead = true;
                ReadCommandLine(out var mode, out var map);
                pendingMode = mode;
                if (!string.IsNullOrEmpty(map) && LoadMap(map)) return; // continues in the new scene
            }

            if (pendingMode != Mode.None)
            {
                var mode = pendingMode;
                pendingMode = Mode.None;
                if (pendingAddress != null) address = pendingAddress;
                if (pendingPort != null) port = pendingPort;
                StartMode(mode);
            }
        }

        // ------------------------------------------------------------------ command line

        void ReadCommandLine(out Mode mode, out string map)
        {
            mode = Application.isBatchMode ? Mode.Server : Mode.None;
            map = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-server": mode = Mode.Server; break;
                    case "-host": mode = Mode.Host; break;
                    case "-client": mode = Mode.Client; break;
                    case "-ip":
                    case "-address":
                        if (next != null) { address = pendingAddress = next; i++; }
                        break;
                    case "-port":
                        if (next != null) { port = pendingPort = next; i++; }
                        break;
                    case "-map":
                        if (next != null) { map = next; i++; }
                        break;
                }
            }
        }

        void StartMode(Mode mode)
        {
            switch (mode)
            {
                case Mode.Server:
                    // Headless: no need to spin faster than the simulation.
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = 60;
                    Begin(() => manager.StartServer(), listenAll: true);
                    Debug.Log($"[GravityPortals] Dedicated server on UDP port {port}, map '{SceneManager.GetActiveScene().name}'. {status}");
                    break;
                case Mode.Host:
                    Begin(() => manager.StartHost(), listenAll: true);
                    break;
                case Mode.Client:
                    Begin(() => manager.StartClient(), listenAll: false);
                    break;
            }
        }

        // ------------------------------------------------------------------ maps

        static void FindMaps()
        {
            if (maps.Count > 0) return;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string file = Path.GetFileNameWithoutExtension(path);
                if (file == "SampleScene") continue; // Unity's template scene, not a map
                maps.Add((Pretty(file), path));
            }
        }

        static string Pretty(string sceneName)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sceneName.Length; i++)
            {
                char c = sceneName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(sceneName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Replace("Tri Gravity", "Tri-Gravity");
        }

        /// <summary>Loads a map by (partial, case-insensitive) name. False if it's already loaded or unknown.</summary>
        bool LoadMap(string query)
        {
            FindMaps();
            string q = query.Replace(" ", "").Replace("-", "").ToLowerInvariant();
            foreach (var m in maps)
            {
                string file = Path.GetFileNameWithoutExtension(m.path).ToLowerInvariant();
                if (!file.Contains(q)) continue;
                if (SceneManager.GetActiveScene().path == m.path) return false;
                SwitchTo(m.path);
                return true;
            }
            Debug.LogWarning($"[GravityPortals] No map matches '{query}'. Maps: {string.Join(", ", maps.ConvertAll(x => x.name))}");
            return false;
        }

        void SwitchTo(string scenePath)
        {
            // This scene's NetworkManager would survive the load; remove it so the new
            // scene's own one (with its own spawn points and props) takes over.
            Destroy(gameObject);
            SceneManager.LoadScene(scenePath);
        }

        // ------------------------------------------------------------------ menu

        void OnGUI()
        {
            if (manager == null) return;
            const float w = 270f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 10f, 10f, w, 330f), GUI.skin.box);

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

                if (maps.Count > 1)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("Map (when hosting; joining uses the host's map)");
                    string active = SceneManager.GetActiveScene().path;
                    foreach (var m in maps)
                    {
                        bool current = m.path == active;
                        GUI.enabled = !current;
                        if (GUILayout.Button(current ? $"> {m.name}" : m.name)) SwitchTo(m.path);
                        GUI.enabled = true;
                    }
                }
            }
            else
            {
                string mode = manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client";
                int players = FindObjectsByType<NetworkGravityPlayer>(FindObjectsSortMode.None).Length;
                GUILayout.Label($"{mode}   players: {players}   map: {Pretty(SceneManager.GetActiveScene().name)}");
                if (manager.IsClient && !manager.IsServer && !manager.IsConnectedClient) GUILayout.Label("Connecting...");

                if (manager.IsServer)
                    GUILayout.Label($"Rejected updates (all players): {TotalRejections()}");

                if (GUILayout.Button("Disconnect")) manager.Shutdown();
            }

            GUILayout.EndArea();
        }

        void Begin(Func<bool> start, bool listenAll)
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
