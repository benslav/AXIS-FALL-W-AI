using System.Collections.Generic;
using System.Text;
using GravityPortals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "The Tesseract": builds the scene from <see cref="TesseractLayout"/> (eight cube rooms joined
/// like the cells of a hypercube, 24 door pairs) with sizes from the TesseractSettings asset.
/// Also the door report: Tools > Gravity Portals > The Tesseract: List Doors and Check.
/// </summary>
[InitializeOnLoad]
public static class TesseractBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/TheTesseract.unity";
    const string VersionKey = "ClaudeTest.TheTesseract.Version";
    const int Version = 4; // 1: greybox rooms and all 24 doors; 2: rounded edges, walk onto any wall; 3: crates, grenades, sound through doors; 4: colours, glow, recursion, teams, hill
    const string MeshPath = "Assets/GravityPortals/Meshes/TheTesseractGeometry.asset";
    const string SettingsFolder = "Assets/GravityPortals/Settings";
    const string SettingsPath = SettingsFolder + "/TesseractSettings.asset";

    static readonly Color Background = new Color(0.02f, 0.02f, 0.03f);

    static TesseractBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build The Tesseract")]
    public static void Build()
    {
        var config = Settings().config;
        var problems = TesseractLayout.Validate(config, TesseractLayout.Generate(config));
        if (problems.Count > 0)
        {
            Debug.LogError("[Tesseract] Not built, the layout has problems:\n  - " + string.Join("\n  - ", problems));
            return;
        }
        GravityArenaBuilder.CameraBackground = Background;
        // Overview: inside the Core, from a low corner.
        Vector3 core = TesseractLayout.Centre(TesseractLayout.RoomIndex("Core"), config);
        float h = config.Half;
        GravityArenaBuilder.BuildScene(ScenePath, () => Layout(config),
            core + new Vector3(-h * 0.7f, -h * 0.5f, -h * 0.7f), core + new Vector3(h, 0f, 0f));
    }

    [MenuItem("Tools/Gravity Portals/The Tesseract: List Doors and Check")]
    public static void ListDoors()
    {
        var config = Settings().config;
        Debug.Log("[Tesseract] " + TesseractLayout.Report(config));
        if (EditorSceneManager.GetActiveScene().path == ScenePath) CheckScene(config);
        else Debug.Log($"[Tesseract] Open {ScenePath} to also check the doors built in the scene.");
    }

    /// <summary>The asset holding the room and door sizes (made with the defaults the first time).</summary>
    internal static TesseractSettings Settings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TesseractSettings>(SettingsPath);
        if (settings != null) return settings;
        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Settings");
        settings = ScriptableObject.CreateInstance<TesseractSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static void Layout(TesseractConfig config)
    {
        var grid = GravityArenaBuilder.GridTexture();
        var materials = new Material[TesseractLayout.MaterialCount];
        for (int r = 0; r < TesseractLayout.Rooms.Length; r++)
        {
            var room = TesseractLayout.Rooms[r];
            // Tiles about 2 m across, so you can judge distance on any face.
            materials[TesseractLayout.MatWall(r)] = GravityArenaBuilder.LitMaterial($"Tesseract {room.Name} Wall",
                Color.Lerp(new Color(0.55f, 0.55f, 0.58f), room.Color, 0.6f), grid, Vector2.one * (config.roomSize * 0.5f));
            materials[TesseractLayout.MatGlow(r)] = GravityArenaBuilder.EmissiveMaterial($"Tesseract {room.Name} Glow", room.Color * 2.6f);
        }
        materials[TesseractLayout.MatBeacon] = GravityArenaBuilder.EmissiveMaterial("Tesseract Beacon",
            TesseractLayout.Rooms[TesseractLayout.RoomIndex(TesseractLayout.HillRoom)].Color * 4f);

        RenderSettings.skybox = null;
        RenderSettings.fog = false; // closed rooms: nothing far away to fade
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.33f);

        var layout = TesseractLayout.Generate(config);
        var result = PavilionBuilder.Build(layout, GravityArenaBuilder.GeometryRoot, materials,
            GravityArenaBuilder.PortalMaterial, GravityArenaBuilder.PortalViewMaterialAsset);
        foreach (var go in result.Boxes) GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(result.Meshes[0], MeshPath);
        for (int i = 1; i < result.Meshes.Count; i++) AssetDatabase.AddObjectToAsset(result.Meshes[i], MeshPath);
        AssetDatabase.SaveAssets();

        // Down follows the curve you stand on: walk off the floor round any edge onto any wall.
        var gravity = new GameObject("Room Gravity").AddComponent<SpinGravity>();
        gravity.axisPoint = Vector3.zero;
        gravity.rooms = TesseractLayout.Field(config);

        var options = new GameObject("Map Options").AddComponent<MapOptions>();
        // Every door keeps your gravity relative to you, so the "gravity changes" warning would only mislead.
        options.gravityWarnings = false;
        // Rooms only meet through doors: a sound in the next room is heard from its door.
        options.soundThroughPortals = true;
        // The view settles in about 0.3 s after any sudden change of gravity (a drop caught by a curve).
        options.gravityTurnRate = 12f;
        options.gravityTurnMinSpeed = 300f;
        options.curveViewEase = 16f;
        // Doors seen through doors show their rooms too.
        options.portalRecursion = config.portalRecursion;

        // Teams (for a later team mode; free-for-all ignores it) spawn on opposite faces of the Shell.
        foreach (var spawn in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            spawn.team = TesseractLayout.SpawnTeam(spawn.transform.up);

        // The objective: the whole Core (marked only: there's no hill mode yet).
        int core = TesseractLayout.RoomIndex(TesseractLayout.HillRoom);
        var hill = new GameObject("Hill (Core)").AddComponent<HillZone>();
        hill.bounds = new Bounds(TesseractLayout.Centre(core, config), Vector3.one * config.roomSize);
        hill.color = TesseractLayout.Rooms[core].Color;

        Debug.Log($"[Tesseract] {TesseractLayout.Rooms.Length} rooms, {layout.Portals.Count} door portals, {layout.Spawns.Count} spawns, {layout.Surfaces.Count} curved surfaces.");
    }

    /// <summary>Checks the doors actually in the open scene: every room has six, every link leads
    /// back, and no door leads to its own room or the opposite one.</summary>
    static void CheckScene(TesseractConfig config)
    {
        var problems = new List<string>();
        var portals = Object.FindObjectsByType<Portal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var count = new int[TesseractLayout.Rooms.Length];
        foreach (var p in portals)
        {
            int room = RoomAt(p.transform.position, config);
            count[room]++;
            if (p.linkedPortal == null) { problems.Add($"{p.name} leads nowhere."); continue; }
            if (p.linkedPortal.linkedPortal != p) problems.Add($"{p.name} -> {p.linkedPortal.name}, which doesn't lead back.");
            int target = RoomAt(p.linkedPortal.transform.position, config);
            if (target == room) problems.Add($"{p.name} leads back into its own room.");
            else if (TesseractLayout.Opposite(room, target))
                problems.Add($"{p.name} joins opposite rooms {TesseractLayout.Rooms[room].Name} and {TesseractLayout.Rooms[target].Name}.");
        }
        for (int r = 0; r < count.Length; r++)
            if (count[r] != 6) problems.Add($"{TesseractLayout.Rooms[r].Name} has {count[r]} doors in the scene; it needs exactly 6.");

        var sb = new StringBuilder($"[Tesseract] Scene check, {portals.Length} portals: ");
        if (problems.Count == 0) sb.Append("all good.");
        else sb.Append($"{problems.Count} problem(s):\n  - ").Append(string.Join("\n  - ", problems));
        if (problems.Count == 0) Debug.Log(sb.ToString());
        else Debug.LogWarning(sb.ToString());
    }

    static int RoomAt(Vector3 p, TesseractConfig config)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int r = 0; r < TesseractLayout.Rooms.Length; r++)
        {
            float d = (p - TesseractLayout.Centre(r, config)).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = r; }
        }
        return best;
    }
}
