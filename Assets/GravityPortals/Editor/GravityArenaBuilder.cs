using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GravityPortals;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the gravity-portal test arena as a new scene.
/// Runs once automatically after first compile; rebuild any time from
/// Tools > Gravity Portals > Build Arena Scene (overwrites the arena scene).
/// </summary>
[InitializeOnLoad]
public static class GravityArenaBuilder
{
    const string Root = "Assets/GravityPortals";
    const string ScenePath = Root + "/Scenes/GravityArena.unity";
    const string MaterialFolder = Root + "/Materials";
    const string PrefabFolder = Root + "/Prefabs";
    const string AutoRunKey = "ClaudeTest.GravityArenaBuilder.AutoRan";
    const string NetworkVersionKey = "ClaudeTest.GravityArenaBuilder.NetworkedVersion";
    const int NetworkedVersion = 4; // 1 = multiplayer, 2 = combat, 3 = polish (portal views), 4 = portal layout for carry-through gravity

    // Arena interior: x and z in [-20, 20], y in [0, 16].
    const float HalfSize = 20f;
    const float Height = 16f;
    const float Wall = 1f;

    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Magenta = new Color(1f, 0.2f, 0.85f);
    static readonly Color Green = new Color(0.3f, 1f, 0.35f);
    static readonly Color Yellow = new Color(1f, 0.85f, 0.1f);
    static readonly Color Orange = new Color(1f, 0.45f, 0.05f);
    static readonly Color Red = new Color(1f, 0.15f, 0.2f);

    static Material portalMat, portalViewMat;
    static Transform geometryRoot, portalRoot, propRoot, lightRoot;
    static int nextPortalId;

    static GravityArenaBuilder()
    {
        // Build once on first compile, and rebuild once when the arena gains a new
        // version (v1 = multiplayer), so the scene picks up the networked setup.
        bool firstRun = !EditorPrefs.GetBool(AutoRunKey, false);
        bool outdated = EditorPrefs.GetInt(NetworkVersionKey, 0) < NetworkedVersion;
        if (!firstRun && !outdated) return;
        EditorPrefs.SetBool(AutoRunKey, true);
        EditorPrefs.SetInt(NetworkVersionKey, NetworkedVersion);
        EditorApplication.delayCall += () =>
        {
            if (firstRun && !outdated && File.Exists(ScenePath)) return;
            Build();
        };
    }

    [MenuItem("Tools/Gravity Portals/Build Arena Scene")]
    public static void Build()
    {
        BuildScene(ScenePath, () =>
        {
            var grid = GridTexture();
            var floorMat = LitMaterial("Floor", new Color(0.42f, 0.42f, 0.44f), grid, new Vector2(21, 21));
            var ceilingMat = LitMaterial("Ceiling", new Color(0.30f, 0.34f, 0.46f), grid, new Vector2(21, 21));
            var westMat = LitMaterial("WallWest", new Color(0.26f, 0.42f, 0.42f), grid, new Vector2(20, 8));
            var eastMat = LitMaterial("WallEast", new Color(0.40f, 0.30f, 0.46f), grid, new Vector2(20, 8));
            var northMat = LitMaterial("WallNorth", new Color(0.46f, 0.36f, 0.28f), grid, new Vector2(20, 8));
            var southMat = LitMaterial("WallSouth", new Color(0.30f, 0.42f, 0.30f), grid, new Vector2(20, 8));
            var structureMat = LitMaterial("Structure", new Color(0.78f, 0.80f, 0.84f), grid, new Vector2(3, 3));
            var darkMat = LitMaterial("StructureDark", new Color(0.16f, 0.17f, 0.20f), null, Vector2.one);
            var accentMat = EmissiveMaterial("Accent", new Color(0.2f, 0.9f, 1f) * 2.2f);

            BuildLighting();
            BuildShell(floorMat, ceilingMat, westMat, eastMat, northMat, southMat, accentMat);
            BuildStructures(structureMat, darkMat, out var rampA, out var rampB);
            BuildPortals(rampA, rampB);
            BuildSpawnPoints();
        }, new Vector3(0, 13, -19), new Vector3(0, 3, 0));
    }

    /// <summary>
    /// Shared scene setup for every map: makes a fresh scene, runs <paramref name="layout"/>
    /// (geometry, lights, portals, spawn points), then adds the networked prefabs,
    /// NetworkManager and overview camera, and saves it.
    /// </summary>
    internal static void BuildScene(string scenePath, System.Action layout, Vector3 cameraPosition, Vector3 cameraTarget)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Root, "Scenes");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Prefabs");
        nextPortalId = 0;

        // Portal views skip portal surfaces (no recursion); your own body is hidden
        // from your camera but visible in portals.
        EnsureLayer(8, "PortalSurface");
        EnsureLayer(9, "LocalPlayer");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var crateMat = LitMaterial("Crate", new Color(0.95f, 0.55f, 0.15f), null, Vector2.one);
        portalMat = UnlitMaterial("Portal");
        portalViewMat = PortalViewMaterial();

        geometryRoot = new GameObject("Arena").transform;
        portalRoot = new GameObject("Portals").transform;
        propRoot = new GameObject("Props").transform;
        lightRoot = new GameObject("Lighting").transform;

        layout();

        var crate = CratePrefab(crateMat);
        var projectile = ProjectilePrefab();
        var player = PlayerPrefab(projectile);
        AssetDatabase.DeleteAsset($"{PrefabFolder}/Ball.prefab"); // replaced by the Plasma Launcher
        BuildCamera(cameraPosition, cameraTarget);
        BuildNetworking(player, projectile, crate);

        EditorSceneManager.SaveScene(scene, scenePath);
        AddToBuildSettings(scenePath);
        Debug.Log($"[GravityPortals] Built and opened '{scenePath}'. Press Play, then Host (or Join) in the top-right menu.");
    }

    internal static Transform GeometryRoot => geometryRoot;
    internal static Transform LightRoot => lightRoot;

    internal static void AddSpawnPoint(string name, Vector3 position, Quaternion rotation)
    {
        var parent = GameObject.Find("Spawn Points");
        var root = parent != null ? parent.transform : new GameObject("Spawn Points").transform;
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<SpawnPoint>();
    }

    /// <summary>Crate drop point. Its up axis is the crate's "up" (gravity points the other way).</summary>
    internal static void AddPropSpawn(string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(propRoot, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<PropSpawnPoint>();
    }

    // ------------------------------------------------------------------ layout

    static void BuildShell(Material floor, Material ceiling, Material west, Material east,
        Material north, Material south, Material accent)
    {
        float span = HalfSize * 2f;
        Block("Floor", new Vector3(0, -Wall / 2, 0), new Vector3(span + 2 * Wall, Wall, span + 2 * Wall), floor);
        Block("Ceiling", new Vector3(0, Height + Wall / 2, 0), new Vector3(span + 2 * Wall, Wall, span + 2 * Wall), ceiling);
        Block("Wall West", new Vector3(-HalfSize - Wall / 2, Height / 2, 0), new Vector3(Wall, Height, span), west);
        Block("Wall East", new Vector3(HalfSize + Wall / 2, Height / 2, 0), new Vector3(Wall, Height, span), east);
        Block("Wall North", new Vector3(0, Height / 2, HalfSize + Wall / 2), new Vector3(span, Height, Wall), north);
        Block("Wall South", new Vector3(0, Height / 2, -HalfSize - Wall / 2), new Vector3(span, Height, Wall), south);

        // Glowing trim along every edge of the room: makes "which way is the floor" readable
        // from any orientation. No colliders, so it never trips anyone up.
        float e = 0.12f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Trim(new Vector3(sx * (HalfSize - e / 2), e / 2, 0), new Vector3(e, e, span), accent);
            Trim(new Vector3(sx * (HalfSize - e / 2), Height - e / 2, 0), new Vector3(e, e, span), accent);
            Trim(new Vector3(0, e / 2, sx * (HalfSize - e / 2)), new Vector3(span, e, e), accent);
            Trim(new Vector3(0, Height - e / 2, sx * (HalfSize - e / 2)), new Vector3(span, e, e), accent);
            for (int sz = -1; sz <= 1; sz += 2)
                Trim(new Vector3(sx * (HalfSize - e / 2), Height / 2, sz * (HalfSize - e / 2)), new Vector3(e, Height, e), accent);
        }
    }

    static void BuildStructures(Material mat, Material dark, out Pose rampA, out Pose rampB)
    {
        // Central tower: its east face is walkable through the yellow portal.
        Block("Tower", new Vector3(0, 4, 0), new Vector3(6, 8, 6), mat);

        // Two raised platforms, each with a ramp.
        Block("Platform SW", new Vector3(-12, 4.5f, -12), new Vector3(8, 1, 8), mat);
        Block("Platform SW Pillar", new Vector3(-12, 2, -12), new Vector3(1.5f, 4, 1.5f), dark);
        Block("Platform NE", new Vector3(12, 4.5f, 12), new Vector3(8, 1, 8), mat);
        Block("Platform NE Pillar", new Vector3(12, 2, 12), new Vector3(1.5f, 4, 1.5f), dark);
        rampA = Ramp("Ramp NE", new Vector3(12, 0, -2), new Vector3(12, 5, 8), 4f, mat);
        rampB = Ramp("Ramp SW", new Vector3(4, 0, -12), new Vector3(-8, 5, -12), 4f, mat);

        // Catwalk high on the north side: a hidden route for wall walkers.
        Block("Catwalk North", new Vector3(0, 9.75f, 14), new Vector3(24, 0.5f, 3), dark);

        // Floor cover.
        Block("Cover 1", new Vector3(-14, 0.75f, 4), new Vector3(2, 1.5f, 4), dark);
        Block("Cover 2", new Vector3(-5, 0.75f, 12), new Vector3(4, 1.5f, 2), dark);
        Block("Cover 3", new Vector3(16, 0.75f, -8), new Vector3(2, 1.5f, 3), dark);

        // Cover hanging from the ceiling, for fights up there.
        Block("Ceiling Block 1", new Vector3(-8, Height - 1.5f, 8), new Vector3(6, 3, 6), dark);
        Block("Ceiling Block 2", new Vector3(10, Height - 1f, -10), new Vector3(4, 2, 8), dark);

        // Wall cover.
        Block("West Wall Ledge", new Vector3(-HalfSize + 1, 6, -8), new Vector3(2, 3, 4), dark);
        Block("East Wall Ledge", new Vector3(HalfSize - 1, 12, 4), new Vector3(2, 4, 3), dark);
    }

    static void BuildPortals(Pose rampA, Pose rampB)
    {
        // Your fall direction carries through a portal, so dropping into one lands you on the
        // surface the OTHER portal faces. A pair between surfaces A and B therefore takes A's
        // walkers to the surface opposite B, and B's walkers to the surface opposite A.
        // These six pairs give every surface (floor, ceiling, four walls) two ways out, and
        // every flight path across the room is clear of the tower, platforms and catwalk.
        var up = Vector3.up;
        var square = new Vector2(3f, 3f);
        var door = new Vector2(2.5f, 3f);

        // Cyan: SW platform top (floor -> south wall) <-> north wall (north wall -> ceiling).
        Link(Make("Cyan A (platform)", new Vector3(-12, 5, -12), up, Vector3.forward, square, Cyan),
             Make("Cyan B (north wall)", new Vector3(6, 6, HalfSize), Vector3.back, up, square, Cyan));

        // Magenta: ceiling (ceiling -> north wall) <-> south wall (south wall -> floor).
        Link(Make("Magenta A (ceiling)", new Vector3(-8, Height, -2), Vector3.down, Vector3.forward, square, Magenta),
             Make("Magenta B (south wall)", new Vector3(-6, 8, -HalfSize), Vector3.forward, up, square, Magenta));

        // Green: NE ramp (floor -> east wall) <-> west wall (west wall -> ceiling).
        Vector3 rampNormal = rampA.rotation * Vector3.up;
        Vector3 rampUp = rampA.rotation * Vector3.forward;
        Link(Make("Green A (ramp)", rampA.position, rampNormal, rampUp, door, Green),
             Make("Green B (west wall)", new Vector3(-HalfSize, 8, 4), Vector3.right, up, square, Green));

        // Red: ceiling (ceiling -> west wall) <-> east wall (east wall -> floor).
        Link(Make("Red A (ceiling)", new Vector3(6, Height, 8), Vector3.down, Vector3.forward, square, Red),
             Make("Red B (east wall)", new Vector3(HalfSize, 12, -14), Vector3.left, up, square, Red));

        // Orange: west wall (west wall -> south wall) <-> north wall (north wall -> east wall).
        Link(Make("Orange A (west wall)", new Vector3(-HalfSize, 8, -15), Vector3.right, up, square, Orange),
             Make("Orange B (north wall)", new Vector3(-10, 8, HalfSize), Vector3.back, up, square, Orange));

        // Yellow: south wall (south wall -> west wall) <-> east wall (east wall -> north wall).
        Link(Make("Yellow A (south wall)", new Vector3(10, 8, -HalfSize), Vector3.forward, up, square, Yellow),
             Make("Yellow B (east wall)", new Vector3(HalfSize, 9, -8), Vector3.left, up, square, Yellow));
    }

    static void BuildSpawnPoints()
    {
        var root = new GameObject("Spawn Points").transform;
        (Vector3 pos, float yaw)[] spawns =
        {
            (new Vector3(6, 0.95f, -17), 0f), (new Vector3(-16, 0.95f, 0), 90f),
            (new Vector3(17, 0.95f, 2), -90f), (new Vector3(0, 0.95f, 17), 180f),
            (new Vector3(-17, 0.95f, -17), 45f), (new Vector3(16, 0.95f, 16), -135f),
        };
        for (int i = 0; i < spawns.Length; i++)
        {
            var go = new GameObject($"Spawn {i + 1}");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(spawns[i].pos, Quaternion.Euler(0, spawns[i].yaw, 0));
            go.AddComponent<SpawnPoint>();
        }

        Vector3[] crates = { new Vector3(-2, 0.4f, -9), new Vector3(2, 0.4f, -9), new Vector3(0, 0.4f, -10.5f) };
        for (int i = 0; i < crates.Length; i++)
        {
            var go = new GameObject($"Crate Spawn {i + 1}");
            go.transform.SetParent(propRoot, false);
            go.transform.position = crates[i];
            go.AddComponent<PropSpawnPoint>();
        }
    }

    static GameObject CratePrefab(Material crateMat)
    {
        var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = "Crate";
        crate.transform.localScale = Vector3.one * 0.8f;
        crate.GetComponent<Renderer>().sharedMaterial = crateMat;
        AddNetworkedPhysics(crate, 5f);
        crate.AddComponent<GravityBody>();
        return SavePrefab(crate, "Crate");
    }

    static GameObject ProjectilePrefab()
    {
        var bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bolt.name = "PlasmaBolt";
        bolt.transform.localScale = Vector3.one * 0.28f;
        bolt.GetComponent<Renderer>().sharedMaterial =
            EmissiveMaterial("PlasmaBolt", WeaponDefs.Get(WeaponDefs.Launcher).Accent * 3f);
        bolt.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        AddNetworkedPhysics(bolt, 1f);
        var body = bolt.AddComponent<GravityBody>();
        body.gravityStrength = 4f; // a gentle arc along the shooter's gravity
        var glow = bolt.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = WeaponDefs.Get(WeaponDefs.Launcher).Accent;
        glow.range = 5f;
        glow.intensity = 2.5f;
        glow.shadows = LightShadows.None;
        bolt.AddComponent<Projectile>();
        return SavePrefab(bolt, "PlasmaBolt");
    }

    /// <summary>Server-simulated rigidbody: clients get a kinematic copy that NetworkTransform moves.</summary>
    static void AddNetworkedPhysics(GameObject go, float mass)
    {
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>();
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        go.AddComponent<NetworkRigidbody>();
    }

    static GameObject PlayerPrefab(GameObject projectilePrefab)
    {
        var player = new GameObject("Player");
        var capsule = player.AddComponent<CapsuleCollider>();
        capsule.radius = 0.4f;
        capsule.height = 1.8f;
        var rb = player.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.useGravity = false;
        var controller = player.AddComponent<GravityPlayerController>();
        player.AddComponent<NetworkObject>();
        player.AddComponent<NetworkGravityPlayer>();
        var combat = player.AddComponent<PlayerCombat>();
        combat.projectilePrefab = projectilePrefab.GetComponent<NetworkObject>();

        // What other players see. Your own copy is hidden from you.
        var bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bodyMesh.name = "BodyMesh";
        Object.DestroyImmediate(bodyMesh.GetComponent<Collider>());
        bodyMesh.transform.SetParent(player.transform, false);
        bodyMesh.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);

        // Head pivots with the view, so the visor shows where someone is aiming.
        var head = new GameObject("HeadPivot").transform;
        head.SetParent(player.transform, false);
        head.localPosition = new Vector3(0, 0.65f, 0);
        var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor";
        Object.DestroyImmediate(visor.GetComponent<Collider>());
        visor.transform.SetParent(head, false);
        visor.transform.localPosition = new Vector3(0, 0, 0.33f);
        visor.transform.localScale = new Vector3(0.55f, 0.14f, 0.14f);
        controller.headPivot = head;

        return SavePrefab(player, "Player");
    }

    static void BuildCamera(Vector3 position, Vector3 target)
    {
        // Shows the arena from above until you spawn; then your player takes it over.
        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.position = position;
        camGo.transform.LookAt(target);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.fieldOfView = 80f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.04f);
        camGo.AddComponent<AudioListener>();
    }

    static void BuildNetworking(GameObject player, GameObject projectile, GameObject crate)
    {
        var go = new GameObject("NetworkManager");
        var transport = go.AddComponent<UnityTransport>();
        var manager = go.AddComponent<NetworkManager>();
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = player;

        string listPath = $"{PrefabFolder}/NetworkPrefabs.asset";
        AssetDatabase.DeleteAsset(listPath);
        var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
        list.Add(new NetworkPrefab { Prefab = projectile });
        list.Add(new NetworkPrefab { Prefab = crate });
        AssetDatabase.CreateAsset(list, listPath);
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);

        go.AddComponent<NetworkHud>();
        go.AddComponent<ArenaNetworkSpawner>().cratePrefab = crate.GetComponent<NetworkObject>();
    }

    static GameObject SavePrefab(GameObject instance, string name)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        var asset = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);

        // NetworkObject ids are normally generated when Unity validates the asset;
        // do it now so the prefab is usable straight away.
        var netObj = asset.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(netObj, null);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
        return asset;
    }

    static void BuildLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.40f);

        // The arena is a closed box, so the directional light casts no shadows
        // (otherwise the ceiling would shadow everything).
        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.transform.SetParent(lightRoot, false);
        sun.type = LightType.Directional;
        sun.intensity = 0.8f;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(55, -35, 0);

        Vector3[] spots =
        {
            new Vector3(-12, 12, -12), new Vector3(12, 12, 12), new Vector3(-12, 4, 12),
            new Vector3(12, 4, -12), new Vector3(0, 13, 0), new Vector3(0, 3, -14),
        };
        Color[] tints = { Cyan, Magenta, Green, Orange, Color.white, Yellow };
        for (int i = 0; i < spots.Length; i++)
        {
            var l = new GameObject($"Point Light {i + 1}").AddComponent<Light>();
            l.transform.SetParent(lightRoot, false);
            l.transform.position = spots[i];
            l.type = LightType.Point;
            l.range = 22f;
            l.intensity = 3f;
            l.color = Color.Lerp(tints[i], Color.white, 0.55f);
            l.shadows = LightShadows.None;
        }
    }

    // ------------------------------------------------------------------ helpers

    internal static GameObject Block(string name, Vector3 center, Vector3 size, Material mat, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(geometryRoot, false);
        go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
        return go;
    }

    internal static void Trim(Vector3 center, Vector3 size, Material mat)
    {
        var go = Block("Trim", center, size, mat);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>A sloped slab whose top surface runs from bottom to top. Returns the
    /// pose of the middle of the top surface (up = surface normal, forward = uphill).</summary>
    static Pose Ramp(string name, Vector3 bottom, Vector3 top, float width, Material mat)
    {
        const float thickness = 0.5f;
        Vector3 dir = top - bottom;
        var rot = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 mid = (bottom + top) * 0.5f;
        Vector3 normal = rot * Vector3.up;
        Block(name, mid - normal * thickness * 0.5f, new Vector3(width, thickness, dir.magnitude), mat, rot);
        return new Pose(mid, rot);
    }

    internal static Portal Make(string name, Vector3 position, Vector3 normal, Vector3 upHint, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(portalRoot, false);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal, upHint));
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        var portal = go.AddComponent<Portal>();
        portal.portalId = nextPortalId++;
        portal.size = size;
        portal.color = color;
        portal.visualMaterial = portalMat;
        portal.viewMaterial = portalViewMat;
        box.size = new Vector3(size.x, size.y, portal.triggerFront + portal.triggerBack);
        box.center = new Vector3(0, 0, (portal.triggerFront - portal.triggerBack) * 0.5f);
        return portal;
    }

    internal static void Link(Portal a, Portal b)
    {
        a.linkedPortal = b;
        b.linkedPortal = a;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }

    static Material SaveMaterial(string name, Material mat)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = mat.shader;
            existing.CopyPropertiesFromMaterial(mat);
            existing.shaderKeywords = mat.shaderKeywords;
            existing.globalIlluminationFlags = mat.globalIlluminationFlags;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

    internal static Material LitMaterial(string name, Color color, Texture2D texture, Vector2 tiling)
    {
        var mat = new Material(LitShader) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Smoothness", 0.25f);
        if (texture != null)
        {
            mat.SetTexture("_BaseMap", texture);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
        }
        return SaveMaterial(name, mat);
    }

    internal static Material EmissiveMaterial(string name, Color emission)
    {
        var mat = new Material(LitShader) { name = name };
        mat.SetColor("_BaseColor", Color.black);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", emission);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return SaveMaterial(name, mat);
    }

    static Material UnlitMaterial(string name)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", Color.white);
        return SaveMaterial(name, mat);
    }

    static Material PortalViewMaterial()
    {
        var shader = Shader.Find("GravityPortals/PortalView");
        if (shader == null)
        {
            Debug.LogWarning("[GravityArenaBuilder] PortalView shader not found; portals will use plain glowing surfaces.");
            return null;
        }
        return SaveMaterial("PortalView", new Material(shader) { name = "PortalView" });
    }

    static void EnsureLayer(int index, string layerName)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return; // already there

        var slot = layers.GetArrayElementAtIndex(index);
        if (!string.IsNullOrEmpty(slot.stringValue))
        {
            Debug.LogWarning($"[GravityArenaBuilder] Layer {index} is already '{slot.stringValue}'; '{layerName}' not added.");
            return;
        }
        slot.stringValue = layerName;
        tagManager.ApplyModifiedProperties();
    }

    internal static Texture2D GridTexture()
    {
        string path = $"{MaterialFolder}/Grid.png";
        if (!File.Exists(path))
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool line = x < 3 || y < 3 || x >= n - 3 || y >= n - 3;
                    bool sub = x == n / 2 || y == n / 2;
                    float v = line ? 1f : sub ? 0.78f : 0.66f;
                    pixels[y * n + x] = new Color(v, v, v, 1f);
                }
            tex.SetPixels(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
