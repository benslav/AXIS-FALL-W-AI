using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Main menu and lobby screen. Not connected: name, create lobby, join with a code, settings,
    /// quit. In a lobby: the code to invite friends, who's in, map voting, and Start (host).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public const string SensitivityKey = "MouseSensitivity";
        public const string VolumeKey = "MasterVolume";

        enum Screen { Main, Settings }
        Screen screen = Screen.Main;
        string nameInput, codeInput = "";
        Texture2D background;

        void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            nameInput = ConnectionApproval.LocalName;
            Sfx.MasterVolume = PlayerPrefs.GetFloat(VolumeKey, Sfx.MasterVolume);
            background = Gradient();
        }

        void OnDestroy()
        {
            if (background != null) Destroy(background);
        }

        static Texture2D Gradient()
        {
            var t = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float k = y / 63f;
                t.SetPixel(0, y, Color.Lerp(new Color(0.01f, 0.01f, 0.02f), new Color(0.03f, 0.09f, 0.16f), k));
            }
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            var flow = SessionFlow.Instance;
            float w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
            GUI.DrawTexture(new Rect(0, 0, w, h), background);

            GUI.Label(new Rect(0, h * 0.06f, w, 90), "AXIS-FALL", MenuStyles.Title);
            GUI.Label(new Rect(0, h * 0.06f + 80, w, 30), "<color=#7fe8ff>gravity portal arena</color>",
                new GUIStyle(MenuStyles.Rich) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            GUI.Label(new Rect(w - 140, h - 30, 130, 24), $"v{Application.version}", new GUIStyle(MenuStyles.Rich) { alignment = TextAnchor.MiddleRight });

            if (flow == null)
            {
                GUI.Label(new Rect(0, h * 0.5f, w, 30), "Network setup missing from this scene.", MenuStyles.Rich);
                return;
            }

            if (flow.InSession) LobbyScreen(flow, w, h);
            else if (screen == Screen.Settings) SettingsScreen(w, h);
            else MainScreen(flow, w, h);
        }

        // ------------------------------------------------------------------ screens

        void MainScreen(SessionFlow flow, float w, float h)
        {
            var r = new Rect((w - 380) / 2f, h * 0.3f, 380, 420);
            MenuStyles.Panel(r);
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 20, r.width - 48, r.height - 40));

            GUILayout.Label("Your name", MenuStyles.Rich);
            string edited = GUILayout.TextField(nameInput, 16, MenuStyles.Field);
            if (edited != nameInput) { nameInput = edited; ConnectionApproval.LocalName = edited; }
            GUILayout.Space(14);

            GUI.enabled = !flow.Busy;
            if (GUILayout.Button("Create lobby", MenuStyles.Big)) flow.CreateLobby();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            codeInput = GUILayout.TextField(codeInput, 12, MenuStyles.Field, GUILayout.Width(170)).ToUpperInvariant();
            if (GUILayout.Button("Join lobby", MenuStyles.Button)) flow.JoinLobby(codeInput);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Space(14);
            if (GUILayout.Button("Settings", MenuStyles.Button)) screen = Screen.Settings;
            if (GUILayout.Button("Quit", MenuStyles.Button))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            if (!string.IsNullOrEmpty(flow.Message)) { GUILayout.Space(8); GUILayout.Label(flow.Message, MenuStyles.Rich); }
            GUILayout.EndArea();
        }

        void SettingsScreen(float w, float h)
        {
            var r = new Rect((w - 420) / 2f, h * 0.3f, 420, 330);
            MenuStyles.Panel(r);
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 20, r.width - 48, r.height - 40));
            GUILayout.Label("<size=22><b>Settings</b></size>", MenuStyles.Rich);
            GUILayout.Space(10);

            float sens = PlayerPrefs.GetFloat(SensitivityKey, 2f);
            GUILayout.Label($"Mouse sensitivity: {sens:0.0}", MenuStyles.Rich);
            float newSens = GUILayout.HorizontalSlider(sens, 0.2f, 6f);
            if (!Mathf.Approximately(newSens, sens)) PlayerPrefs.SetFloat(SensitivityKey, newSens);
            GUILayout.Space(12);

            float vol = PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            GUILayout.Label($"Volume: {Mathf.RoundToInt(vol * 100)}%", MenuStyles.Rich);
            float newVol = GUILayout.HorizontalSlider(vol, 0f, 1f);
            if (!Mathf.Approximately(newVol, vol))
            {
                PlayerPrefs.SetFloat(VolumeKey, newVol);
                Sfx.MasterVolume = newVol;
            }
            GUILayout.Space(12);

            bool full = UnityEngine.Screen.fullScreen;
            bool newFull = GUILayout.Toggle(full, " Fullscreen", new GUIStyle(GUI.skin.toggle) { fontSize = 16 });
            if (newFull != full) UnityEngine.Screen.fullScreen = newFull;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", MenuStyles.Button)) { PlayerPrefs.Save(); screen = Screen.Main; }
            GUILayout.EndArea();
        }

        void LobbyScreen(SessionFlow flow, float w, float h)
        {
            var lobby = LobbyState.Instance;
            var manager = flow.Manager;
            var r = new Rect((w - 760) / 2f, h * 0.27f, 760, Mathf.Min(520, h * 0.68f));
            MenuStyles.Panel(r);

            if (lobby == null)
            {
                GUI.Label(new Rect(r.x + 24, r.y + 24, r.width - 48, 30), "Connecting to the lobby...", MenuStyles.PanelLabel);
                if (GUI.Button(new Rect(r.x + 24, r.yMax - 64, 200, 40), "Cancel", MenuStyles.Button)) flow.Leave();
                return;
            }

            // Header: the invite code.
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 18, r.width - 48, 70));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<size=22><b>LOBBY</b></size>     Code: <size=26><b><color=#7fe8ff>{flow.JoinCode}</color></b></size>", MenuStyles.Rich);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(flow.JoinCode) && GUILayout.Button("Copy code", MenuStyles.Button, GUILayout.Width(130)))
                GUIUtility.systemCopyBuffer = flow.JoinCode;
            GUILayout.EndHorizontal();
            GUILayout.Label("Invite friends by sending them this code; they enter it under Join lobby.", MenuStyles.Rich);
            GUILayout.EndArea();

            float top = r.y + 96, colH = r.height - 96 - 76;

            // Players
            GUILayout.BeginArea(new Rect(r.x + 24, top, 300, colH));
            GUILayout.Label($"<b>Players ({lobby.Members.Count}/{ConnectionApproval.MaxPlayers})</b>", MenuStyles.PanelLabel);
            for (int i = 0; i < lobby.Members.Count; i++)
            {
                var m = lobby.Members[i];
                string host = m.ClientId == NetworkManagerServerId ? "  <color=#ffd860>(host)</color>" : "";
                string you = m.ClientId == manager.LocalClientId ? "  <color=#7fe8ff>(you)</color>" : "";
                string vote = m.Vote >= 0 && m.Vote < MapList.All.Count ? $"\n   <color=#9aa>votes {MapList.All[m.Vote].name}</color>" : "\n   <color=#667>no vote yet</color>";
                GUILayout.Label($"{m.Name}{host}{you}{vote}", MenuStyles.Rich);
            }
            GUILayout.EndArea();

            // Map vote
            GUILayout.BeginArea(new Rect(r.x + 350, top, r.width - 374, colH));
            GUILayout.Label("<b>Vote for a map</b>", MenuStyles.PanelLabel);
            int myVote = lobby.VoteOf(manager.LocalClientId);
            for (int i = 0; i < MapList.All.Count; i++)
            {
                int votes = lobby.VotesFor(i);
                string label = $"{(i == myVote ? "<color=#7fe8ff>> </color>" : "")}{MapList.All[i].name}   <color=#9aa>{(votes > 0 ? new string('|', votes) + $" {votes}" : "")}</color>";
                if (GUILayout.Button(label, new GUIStyle(MenuStyles.Button) { alignment = TextAnchor.MiddleLeft, fixedHeight = 34, fontSize = 15 }))
                    lobby.Vote(i == myVote ? -1 : i);
            }
            GUILayout.EndArea();

            // Footer
            GUILayout.BeginArea(new Rect(r.x + 24, r.yMax - 64, r.width - 48, 50));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(manager.IsServer ? "Close lobby" : "Leave lobby", MenuStyles.Button, GUILayout.Width(170))) flow.Leave();
            GUILayout.FlexibleSpace();
            if (manager.IsServer)
            {
                if (GUILayout.Button("Start game", MenuStyles.Big, GUILayout.Width(220))) flow.StartMatch();
            }
            else
            {
                GUILayout.Label("Waiting for the host to start...", MenuStyles.PanelLabel);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static ulong NetworkManagerServerId => Unity.Netcode.NetworkManager.ServerClientId;
    }
}
