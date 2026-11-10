using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPortals
{
    /// <summary>
    /// Lobby to match and back. Lives on the main menu's NetworkManager, which stays loaded for
    /// the whole session (the menu scene doubles as the lobby).
    ///
    ///  - Create lobby: host online through Relay; the join code is the invite.
    ///  - Start: the host loads the voted map on everyone's machine; when it's loaded, the
    ///    server spawns every player (and anyone who joins mid-match once they've caught up)
    ///    plus the map's crates.
    ///  - Back to lobby / Leave: everyone returns to the menu scene, still connected; or you
    ///    disconnect and land on the main menu (also what happens if the host leaves).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SessionFlow : MonoBehaviour
    {
        public NetworkObject lobbyStatePrefab;
        public NetworkObject playerPrefab;
        public NetworkObject cratePrefab;

        public static SessionFlow Instance { get; private set; }

        NetworkManager manager;
        bool sceneEventsHooked;

        public string JoinCode { get; private set; }
        public string Message { get; set; }
        public bool Busy { get; private set; }
        public NetworkManager Manager => manager;
        public bool InSession => manager != null && manager.IsListening;

        void Awake()
        {
            manager = GetComponent<NetworkManager>();
            // Coming back to the menu scene creates another copy of this object; keep the live one.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton != manager)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ConnectionApproval.Setup(manager);
            manager.OnServerStarted += OnServerStarted;
            manager.OnClientDisconnectCallback += OnClientDisconnect;
            manager.OnClientStopped += _ => OnSessionEnded();
        }

        // ------------------------------------------------------------------ lobby

        public async void CreateLobby()
        {
            if (Busy || InSession) return;
            Busy = true;
            Message = "Creating lobby...";
            try
            {
                ConnectionApproval.RefreshPayload(manager);
                JoinCode = await RelayConnector.HostAsync(manager);
                Message = "";
            }
            catch (System.Exception e)
            {
                Message = RelayConnector.Explain(e);
                Debug.LogWarning($"[Lobby] Create failed: {e}");
            }
            finally { Busy = false; }
        }

        public async void JoinLobby(string code)
        {
            if (Busy || InSession) return;
            if (string.IsNullOrWhiteSpace(code)) { Message = "Type the lobby code first."; return; }
            Busy = true;
            Message = "Joining...";
            try
            {
                ConnectionApproval.RefreshPayload(manager);
                await RelayConnector.JoinAsync(manager, code);
                JoinCode = code.Trim().ToUpperInvariant();
                Message = "";
            }
            catch (System.Exception e)
            {
                Message = RelayConnector.Explain(e);
                Debug.LogWarning($"[Lobby] Join failed: {e}");
            }
            finally { Busy = false; }
        }

        /// <summary>Host: load the winning map for everyone.</summary>
        public void StartMatch()
        {
            if (!manager.IsServer || LobbyState.Instance == null || MapList.All.Count == 0) return;
            int map = LobbyState.Instance.PickWinner();
            LobbyState.Instance.ClearVotes();
            manager.SceneManager.LoadScene(MapList.All[map].scene, LoadSceneMode.Single);
        }

        /// <summary>Host: everyone back to the lobby, still connected.</summary>
        public void BackToLobby()
        {
            if (!manager.IsServer || MapList.InMenu) return;
            if (LobbyState.Instance != null) LobbyState.Instance.RollMapSeed(); // next Shifting Pavilion is a new layout
            manager.SceneManager.LoadScene(MapList.MenuScene, LoadSceneMode.Single);
        }

        /// <summary>Disconnect and go to the main menu.</summary>
        public void Leave()
        {
            Message = "";
            if (manager.IsListening) manager.Shutdown();
            OnSessionEnded();
        }

        // ------------------------------------------------------------------ server side

        void OnServerStarted()
        {
            if (lobbyStatePrefab != null && LobbyState.Instance == null)
                Instantiate(lobbyStatePrefab).Spawn(false); // kept across map changes

            if (!sceneEventsHooked && manager.SceneManager != null)
            {
                manager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
                manager.SceneManager.OnSynchronizeComplete += OnClientCaughtUp;
                sceneEventsHooked = true;
            }
        }

        void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> loaded, List<ulong> timedOut)
        {
            if (!manager.IsServer || sceneName == MapList.MenuScene) return;
            foreach (var clientId in loaded) SpawnPlayer(clientId);
            SpawnCrates();
        }

        void OnClientCaughtUp(ulong clientId)
        {
            // Joined while a match is on: they've loaded the map, now give them a body.
            if (manager.IsServer && !MapList.InMenu) SpawnPlayer(clientId);
        }

        void SpawnPlayer(ulong clientId)
        {
            if (playerPrefab == null || !manager.ConnectedClients.TryGetValue(clientId, out var client)) return;
            if (client.PlayerObject != null) return;
            Instantiate(playerPrefab).SpawnAsPlayerObject(clientId, destroyWithScene: true);
        }

        void SpawnCrates()
        {
            if (cratePrefab == null) return;
            foreach (var point in FindObjectsByType<PropSpawnPoint>(FindObjectsSortMode.None))
            {
                var crate = Instantiate(cratePrefab, point.transform.position, point.transform.rotation);
                if (crate.TryGetComponent(out GravityBody body)) body.GravityDirection = -point.transform.up;
                crate.Spawn(true);
            }
        }

        // ------------------------------------------------------------------ leaving

        void OnClientDisconnect(ulong clientId)
        {
            if (manager.IsServer || clientId != manager.LocalClientId) return;
            Message = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected from the lobby." : manager.DisconnectReason;
        }

        void OnSessionEnded()
        {
            JoinCode = null;
            sceneEventsHooked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!MapList.InMenu) SceneManager.LoadScene(MapList.MenuScene);
        }

        // ------------------------------------------------------------------ in-game menu

        void OnGUI()
        {
            if (!InSession || MapList.InMenu || Cursor.lockState == CursorLockMode.Locked) return;

            float w = 320f, h = manager.IsServer ? 230f : 190f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, w - 40, h - 32));
            GUILayout.Label("<size=22><b>PAUSED</b></size>", MenuStyles.Rich);
            if (!string.IsNullOrEmpty(JoinCode)) GUILayout.Label($"Lobby code: <b>{JoinCode}</b>", MenuStyles.Rich);
            GUILayout.Space(8);
            if (GUILayout.Button("Resume", MenuStyles.Button))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (manager.IsServer && GUILayout.Button("Back to lobby (everyone)", MenuStyles.Button)) BackToLobby();
            if (GUILayout.Button(manager.IsServer ? "End game and leave" : "Leave game", MenuStyles.Button)) Leave();
            GUILayout.EndArea();
        }
    }
}
