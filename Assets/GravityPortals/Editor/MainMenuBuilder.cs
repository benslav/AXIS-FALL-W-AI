using System.Collections.Generic;
using GravityPortals;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the MainMenu scene: the first scene of the game, which also serves as the lobby.
/// It holds the session's NetworkManager (kept alive through every map), the lobby logic and
/// the menu screens. Players aren't spawned here; SessionFlow spawns them when a map starts.
/// </summary>
[InitializeOnLoad]
public static class MainMenuBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/MainMenu.unity";
    const string PrefabFolder = "Assets/GravityPortals/Prefabs";
    const string VersionKey = "ClaudeTest.MainMenu.Version";
    const int Version = 1;

    static MainMenuBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Main Menu")]
    public static void Build()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Player.prefab");
        var crate = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Crate.prefab");
        var bolt = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/PlasmaBolt.prefab");
        if (player == null || crate == null || bolt == null)
        {
            Debug.LogError("[MainMenu] Build a map first (Tools > Gravity Portals > Build Arena Scene); the menu reuses its prefabs.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // The shared lobby object.
        var lobbyGo = new GameObject("LobbyState");
        lobbyGo.AddComponent<NetworkObject>();
        lobbyGo.AddComponent<LobbyState>();
        var lobby = GravityArenaBuilder.SavePrefab(lobbyGo, "LobbyState");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Menu Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.02f, 0.04f);
        camGo.AddComponent<AudioListener>();

        // Session NetworkManager: no automatic player prefab (players spawn when a map starts).
        var netGo = new GameObject("NetworkManager (session)");
        var transport = netGo.AddComponent<UnityTransport>();
        var manager = netGo.AddComponent<NetworkManager>();
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = null;

        string listPath = $"{PrefabFolder}/SessionPrefabs.asset";
        AssetDatabase.DeleteAsset(listPath);
        var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
        foreach (var prefab in new[] { player, crate, bolt, lobby }) list.Add(new NetworkPrefab { Prefab = prefab });
        AssetDatabase.CreateAsset(list, listPath);
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);

        var flow = netGo.AddComponent<SessionFlow>();
        flow.lobbyStatePrefab = lobby.GetComponent<NetworkObject>();
        flow.playerPrefab = player.GetComponent<NetworkObject>();
        flow.cratePrefab = crate.GetComponent<NetworkObject>();

        new GameObject("Main Menu").AddComponent<MainMenuUI>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        PutFirstInBuild(ScenePath);
        Debug.Log("[MainMenu] Built. Press Play from the MainMenu scene to create or join a lobby.");
    }

    /// <summary>The menu must be scene 0 so a built game starts on it.</summary>
    static void PutFirstInBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
