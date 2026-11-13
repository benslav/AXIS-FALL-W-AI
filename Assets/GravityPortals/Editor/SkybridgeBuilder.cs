using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Skybridge": builds the scene from <see cref="SkybridgeLayout"/> (a floor and a ceiling
/// arena 40 m apart, joined by four vertical bridges of equal length and direct door pairs),
/// with materials, lighting, and the void bounds (fall off and you die).
/// </summary>
[InitializeOnLoad]
public static class SkybridgeBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/Skybridge.unity";
    const string VersionKey = "ClaudeTest.Skybridge.Version";
    const int Version = 3; // 2 = space station; 3 = longer, more bridges at random, floor and ceiling laid out separately
    const string MeshPath = "Assets/GravityPortals/Meshes/SkybridgeGeometry.asset";

    static readonly Color Void = new Color(0.01f, 0.012f, 0.025f);

    static SkybridgeBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Skybridge")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = Void;
        // Overview from the side of the gap, seeing both planes and the bridges.
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(-20f, 20f, -95f), new Vector3(0f, 20f, 0f));
    }

    static void Layout()
    {
        var materials = Materials();
        Atmosphere();

        var layout = SkybridgeLayout.Generate();
        var result = PavilionBuilder.Build(layout, GravityArenaBuilder.GeometryRoot, materials,
            GravityArenaBuilder.PortalMaterial, GravityArenaBuilder.PortalViewMaterialAsset);
        foreach (var go in result.Boxes) GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(result.Meshes[0], MeshPath);
        for (int i = 1; i < result.Meshes.Count; i++) AssetDatabase.AddObjectToAsset(result.Meshes[i], MeshPath);
        AssetDatabase.SaveAssets();

        // Leaving this box (off a bridge or a plane's edge) is falling into the void.
        new GameObject("Void Bounds").AddComponent<ArenaBounds>().bounds = SkybridgeLayout.VoidBounds;

        Debug.Log($"[Skybridge] {layout.Boxes.Count} parts, {layout.Portals.Count} door portals, {layout.Spawns.Count} spawns.");
    }

    static Material[] Materials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        var m = new Material[SkybridgeLayout.MaterialCount];
        // Space station: hull plating (warm-tinted floor half, cool-tinted ceiling half), bare and
        // dark steel, hazard yellow, grating, white work lights, and the two halves' glow colours.
        m[SkybridgeLayout.MatHullFloor] = GravityArenaBuilder.LitMaterial("Station Hull Floor", new Color(0.48f, 0.45f, 0.42f), grid, new Vector2(16, 16));
        m[SkybridgeLayout.MatHullCeiling] = GravityArenaBuilder.LitMaterial("Station Hull Ceiling", new Color(0.4f, 0.44f, 0.5f), grid, new Vector2(16, 16));
        m[SkybridgeLayout.MatSteel] = GravityArenaBuilder.LitMaterial("Station Steel", new Color(0.55f, 0.57f, 0.6f), null, Vector2.one);
        m[SkybridgeLayout.MatHazard] = GravityArenaBuilder.LitMaterial("Station Hazard", new Color(0.95f, 0.72f, 0.1f), null, Vector2.one);
        m[SkybridgeLayout.MatDarkSteel] = GravityArenaBuilder.LitMaterial("Station Dark Steel", new Color(0.2f, 0.21f, 0.24f), grid, new Vector2(2, 2));
        m[SkybridgeLayout.MatLight] = GravityArenaBuilder.EmissiveMaterial("Station Light", new Color(0.9f, 0.95f, 1f) * 3f);
        m[SkybridgeLayout.MatFloorGlow] = GravityArenaBuilder.EmissiveMaterial("Station Floor Glow", new Color(1f, 0.55f, 0.2f) * 2.5f);
        m[SkybridgeLayout.MatCeilingGlow] = GravityArenaBuilder.EmissiveMaterial("Station Ceiling Glow", new Color(0.3f, 0.75f, 1f) * 2.5f);
        m[SkybridgeLayout.MatGrate] = GravityArenaBuilder.LitMaterial("Station Grate", new Color(0.36f, 0.38f, 0.4f), grid, new Vector2(3, 24));
        return m;
    }

    static void Atmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.003f; // thin: space should stay clear enough to see the stars
        RenderSettings.fogColor = Void;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.4f);

        // A distant sun: hard, cool-white light from one side, so the hull reads as metal.
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.transform.SetParent(GravityArenaBuilder.LightRoot, false);
        sun.type = LightType.Directional;
        sun.intensity = 0.7f;
        sun.color = new Color(0.9f, 0.95f, 1f);
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.6f, -0.35f, 0.7f));
    }
}
