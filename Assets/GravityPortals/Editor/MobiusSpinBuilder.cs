using GravityPortals;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "Möbius Spin" (stage 1 test): builds the scene from <see cref="MobiusSpinLayout"/> (two
/// concentric rings with spin gravity and one twist gate pair) in the Skybridge station style,
/// plus the <see cref="SpinGravity"/> field and the void bounds.
/// </summary>
[InitializeOnLoad]
public static class MobiusSpinBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/MobiusSpin.unity";
    const string VersionKey = "ClaudeTest.MobiusSpin.Version";
    const int Version = 7; // 2: spokes and the glass hub; 3: tubes from the inner ring to the hub; 4: smooth tubes; 5: wide exact lips, hub door at the pole; 6: whole station 1.5x; 7: full-width two-sided twist gates
    const string MeshPath = "Assets/GravityPortals/Meshes/MobiusSpinGeometry.asset";

    static MobiusSpinBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Mobius Spin")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = new Color(0.01f, 0.012f, 0.025f);
        // Overview from off the side of the rings, looking at the hub.
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(-45f, 25f, -70f), Vector3.zero);
    }

    static void Layout()
    {
        var materials = SkybridgeBuilder.Materials();
        System.Array.Resize(ref materials, MobiusSpinLayout.MaterialCount);
        materials[MobiusSpinLayout.MatGlass] = GlassMaterial();
        SkybridgeBuilder.Atmosphere();

        var layout = MobiusSpinLayout.Generate();
        var result = PavilionBuilder.Build(layout, GravityArenaBuilder.GeometryRoot, materials,
            GravityArenaBuilder.PortalMaterial, GravityArenaBuilder.PortalViewMaterialAsset);
        foreach (var go in result.Boxes)
        {
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            // Glass (the hub, the tube windows): solid to walk on, but shots (and portal sight checks) go straight through.
            if (go.name.EndsWith(": Glass") && go.GetComponent<Collider>() != null) go.AddComponent<ShootThrough>();
        }

        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(result.Meshes[0], MeshPath);
        for (int i = 1; i < result.Meshes.Count; i++) AssetDatabase.AddObjectToAsset(result.Meshes[i], MeshPath);
        AssetDatabase.SaveAssets();

        // Spin gravity round the Z axis: down is away from the hub on the outer walkway, toward it on
        // the inner corridor, round the curves of the spoke tunnels, and out onto the hub's glass.
        var spin = new GameObject("Spin Gravity").AddComponent<SpinGravity>();
        spin.axisPoint = Vector3.zero;
        spin.field = MobiusSpinLayout.Field();

        new GameObject("Void Bounds").AddComponent<ArenaBounds>().bounds = MobiusSpinLayout.VoidBounds;

        Debug.Log($"[MobiusSpin] {layout.Boxes.Count} parts, {layout.Portals.Count} portals, {layout.Spawns.Count} spawns.");
    }

    /// <summary>Faintly blue, see-through, glossy glass (URP Lit, transparent).</summary>
    static Material GlassMaterial()
    {
        var mat = GravityArenaBuilder.LitMaterial("Station Glass", new Color(0.6f, 0.85f, 1f, 0.16f), null, Vector2.one);
        mat.SetFloat("_Surface", 1f);       // transparent
        mat.SetFloat("_Blend", 0f);         // alpha
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);          // both sides: seen from inside and out
        mat.SetFloat("_Smoothness", 0.9f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
