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
/// Shared map-building helpers used by every map builder: scene setup (prefabs, networking,
/// camera), blocks, portals, materials and spawn points. (Named after the original Gravity
/// Arena map, which has since been removed.)
/// </summary>
public static class GravityArenaBuilder
{
    const string Root = "Assets/GravityPortals";
    const string MaterialFolder = Root + "/Materials";
    const string PrefabFolder = Root + "/Prefabs";

    static Material portalMat, portalViewMat;
    static Transform geometryRoot, portalRoot, propRoot, lightRoot;
    static int nextPortalId;

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

    /// <summary>Set by a layout to override the camera's background (e.g. to match fog).</summary>
    internal static Color? CameraBackground;

    internal static Transform GeometryRoot => geometryRoot;
    internal static Transform LightRoot => lightRoot;
    internal static Material PortalMaterial => portalMat;
    internal static Material PortalViewMaterialAsset => portalViewMat;

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
        cam.backgroundColor = CameraBackground ?? new Color(0.02f, 0.025f, 0.04f);
        CameraBackground = null;
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

    internal static GameObject SavePrefab(GameObject instance, string name)
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

    internal static void EnsureFolder(string parent, string child)
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
        scenes.Add(new EditorBuildSettingsScene(path, true)); // the main menu stays first
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
