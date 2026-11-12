using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Endless Pavilion": traditional wooden rooms floating in a dark, foggy void, each facing a
/// different direction, inside one cell of an endless lattice (every face of the space is a
/// portal to the opposite face, so it repeats forever; falling forever counts as the void).
///
/// The layout comes from <see cref="PavilionLayout"/> with a fixed seed, so it's the same map
/// every match (the Shifting Pavilion uses the same generator with a new seed each match).
/// Change <see cref="LayoutSeed"/> and bump <see cref="Version"/> for a different layout.
/// </summary>
[InitializeOnLoad]
public static class EndlessPavilionBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/EndlessPavilion.unity";
    const string VersionKey = "ClaudeTest.EndlessPavilion.Version";
    const int Version = 7; // 2 = wrap-around portal walls; 3 = baked lattice copies; 4 = generated, varied layout; 5 = whole buildings and a city round them; 6 = brighter lighting; 7 = bridges and beams linking everything

    /// <summary>The fixed layout. Picked for a good mix of room kinds and outdoor platforms.</summary>
    internal const int LayoutSeed = 12;

    internal static readonly Color Fog = new Color(0.16f, 0.095f, 0.06f); // warm amber haze, lit by the city
    const string LatticeMeshPath = "Assets/GravityPortals/Meshes/EndlessPavilionLattice.asset";
    const string GeometryMeshPath = "Assets/GravityPortals/Meshes/EndlessPavilionGeometry.asset";

    static EndlessPavilionBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Endless Pavilion")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = Fog;
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(54f, 22f, 52f), new Vector3(32f, 22f, 30f));
    }

    static void Layout()
    {
        var materials = Materials();
        Atmosphere();

        var layout = PavilionLayout.Generate(LayoutSeed);
        var result = PavilionBuilder.Build(layout, GravityArenaBuilder.GeometryRoot, materials,
            GravityArenaBuilder.PortalMaterial, GravityArenaBuilder.PortalViewMaterialAsset);
        foreach (var go in result.Boxes) GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
        Debug.Log($"[EndlessPavilion] Layout {LayoutSeed}: {layout.Rooms.Count} rooms, {layout.Buildings.Count} buildings.");

        // The merged visual meshes live in one asset next to the scene (not inline in it).
        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(GeometryMeshPath);
        AssetDatabase.CreateAsset(result.Meshes[0], GeometryMeshPath);
        for (int i = 1; i < result.Meshes.Count; i++) AssetDatabase.AddObjectToAsset(result.Meshes[i], GeometryMeshPath);
        AssetDatabase.SaveAssets();

        // Look-only copies of the level in the 26 neighbouring cells, from one baked mesh.
        var mesh = PavilionBuilder.LatticeMesh(layout, materials, out var used);
        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(LatticeMeshPath);
        AssetDatabase.CreateAsset(mesh, LatticeMeshPath);
        var copies = new GameObject("Wrap-Around Copies").AddComponent<WrapAroundCopies>();
        copies.source = result.Root;
        copies.period = Vector3.one * PavilionLayout.CellSize;
        copies.range = 1;
        copies.mesh = mesh;
        copies.materials = used;
    }

    /// <summary>The pavilion materials, one per <see cref="PavilionMat"/> in order.</summary>
    internal static Material[] Materials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        return new[]
        {
            GravityArenaBuilder.LitMaterial("Pavilion Tatami", new Color(0.66f, 0.62f, 0.42f), grid, new Vector2(7, 7)),
            GravityArenaBuilder.LitMaterial("Pavilion Wood", new Color(0.2f, 0.12f, 0.08f), null, Vector2.one),
            GravityArenaBuilder.LitMaterial("Pavilion Lacquer", new Color(0.55f, 0.09f, 0.06f), null, Vector2.one),
            // Paper screens glow softly, as if lit from behind.
            GravityArenaBuilder.EmissiveMaterial("Pavilion Paper", new Color(1f, 0.82f, 0.6f) * 0.95f),
            GravityArenaBuilder.EmissiveMaterial("Pavilion Lantern", new Color(1f, 0.55f, 0.2f) * 4f),
            GravityArenaBuilder.LitMaterial("Pavilion Stone", new Color(0.46f, 0.44f, 0.42f), grid, new Vector2(3, 3)),
        };
    }

    internal static void Atmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.012f; // thin enough to see a couple of repeats before it fades
        RenderSettings.fogColor = Fog;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.56f, 0.43f, 0.34f);

        var key = new GameObject("Key Light").AddComponent<Light>();
        key.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        key.type = LightType.Directional;
        key.intensity = 0.85f;
        key.color = new Color(1f, 0.8f, 0.65f);
        key.shadows = LightShadows.None;
        key.transform.rotation = Quaternion.LookRotation(new Vector3(-0.4f, -1f, 0.3f));

        // Rooms and buildings face every way, so a softer light from the opposite side keeps
        // the faces the main light misses from going black.
        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        fill.type = LightType.Directional;
        fill.intensity = 0.45f;
        fill.color = new Color(0.85f, 0.75f, 0.7f);
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(0.5f, 0.85f, -0.35f));
    }
}
