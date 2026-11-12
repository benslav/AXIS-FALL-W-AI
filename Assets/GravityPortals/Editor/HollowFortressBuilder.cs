using GravityPortals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "Hollow Fortress": builds the scene from <see cref="FortressLayout"/> (a hollow cube with a
/// full arena on all six inner faces round a central void). This builder only adds the
/// materials (one palette per face), the atmosphere and the saved meshes.
/// </summary>
[InitializeOnLoad]
public static class HollowFortressBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/HollowFortress.unity";
    const string VersionKey = "ClaudeTest.HollowFortress.Version";
    const int Version = 1;
    const string MeshPath = "Assets/GravityPortals/Meshes/HollowFortressGeometry.asset";

    static readonly Color Fog = new Color(0.035f, 0.03f, 0.045f);

    static HollowFortressBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Hollow Fortress")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = Fog;
        // Overview: from inside the void, looking down at the floor arena.
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(0f, -6f, -18f), new Vector3(0f, -30f, 0f));
    }

    static void Layout()
    {
        var materials = Materials();
        Atmosphere();

        var layout = FortressLayout.Generate();
        var result = PavilionBuilder.Build(layout, GravityArenaBuilder.GeometryRoot, materials,
            GravityArenaBuilder.PortalMaterial, GravityArenaBuilder.PortalViewMaterialAsset);
        foreach (var go in result.Boxes) GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

        GravityArenaBuilder.EnsureFolder("Assets/GravityPortals", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(result.Meshes[0], MeshPath);
        for (int i = 1; i < result.Meshes.Count; i++) AssetDatabase.AddObjectToAsset(result.Meshes[i], MeshPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HollowFortress] {layout.Boxes.Count} parts, {layout.Portals.Count} door portals, {layout.Spawns.Count} spawns.");
    }

    /// <summary>Shared materials, then four per face (tatami, lacquer, lantern, paper) in that
    /// face's colour. Indices match <see cref="FortressLayout"/>.</summary>
    static Material[] Materials()
    {
        var grid = GravityArenaBuilder.GridTexture();
        var m = new Material[FortressLayout.MaterialCount];
        m[FortressLayout.MatShell] = GravityArenaBuilder.LitMaterial("Fortress Shell", new Color(0.16f, 0.1f, 0.07f), grid, new Vector2(24, 24));
        m[FortressLayout.MatWood] = GravityArenaBuilder.LitMaterial("Fortress Wood", new Color(0.26f, 0.15f, 0.09f), null, Vector2.one);
        m[FortressLayout.MatStone] = GravityArenaBuilder.LitMaterial("Fortress Stone", new Color(0.45f, 0.43f, 0.41f), grid, new Vector2(3, 3));
        m[FortressLayout.MatPaper] = GravityArenaBuilder.EmissiveMaterial("Fortress Paper", new Color(1f, 0.9f, 0.75f) * 0.6f);
        m[FortressLayout.MatHeart] = GravityArenaBuilder.EmissiveMaterial("Fortress Heart", new Color(1f, 0.9f, 0.7f) * 3.5f);
        for (int f = 0; f < 6; f++)
        {
            Color c = FortressLayout.FaceColors[f];
            string n = FortressLayout.FaceNames[f];
            m[FortressLayout.FaceMat(f, FortressLayout.SlotTatami)] = GravityArenaBuilder.LitMaterial(
                $"Fortress {n} Tatami", Color.Lerp(new Color(0.62f, 0.58f, 0.42f), c, 0.4f), grid, new Vector2(11, 11));
            m[FortressLayout.FaceMat(f, FortressLayout.SlotLacquer)] = GravityArenaBuilder.LitMaterial(
                $"Fortress {n} Lacquer", c * 0.7f, null, Vector2.one);
            m[FortressLayout.FaceMat(f, FortressLayout.SlotLantern)] = GravityArenaBuilder.EmissiveMaterial(
                $"Fortress {n} Lantern", c * 3f);
            m[FortressLayout.FaceMat(f, FortressLayout.SlotPaper)] = GravityArenaBuilder.EmissiveMaterial(
                $"Fortress {n} Paper", Color.Lerp(Color.white, c, 0.45f) * 0.55f);
        }
        return m;
    }

    static void Atmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.006f; // light: the far faces should still read across the void
        RenderSettings.fogColor = Fog;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.3f, 0.34f);
        // No directional light: each face is lit by its own coloured lanterns, so its colour
        // stays its own (and tells you which way is down).
    }
}
