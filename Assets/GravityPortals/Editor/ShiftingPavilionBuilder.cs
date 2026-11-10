using GravityPortals;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "Shifting Pavilion": the Endless Pavilion's rooms, doors and endless lattice, but with a new
/// random layout every match. The scene holds only the atmosphere and a
/// <see cref="ShiftingPavilion"/>, which generates the level when the map loads from the seed
/// the lobby host rolled (see <see cref="LobbyState.MapSeed"/>).
/// </summary>
[InitializeOnLoad]
public static class ShiftingPavilionBuilder
{
    const string ScenePath = "Assets/GravityPortals/Scenes/ShiftingPavilion.unity";
    const string VersionKey = "ClaudeTest.ShiftingPavilion.Version";
    const int Version = 1;

    static ShiftingPavilionBuilder()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        EditorPrefs.SetInt(VersionKey, Version);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Gravity Portals/Build Shifting Pavilion")]
    public static void Build()
    {
        GravityArenaBuilder.CameraBackground = EndlessPavilionBuilder.Fog;
        GravityArenaBuilder.BuildScene(ScenePath, Layout, new Vector3(54f, 22f, 52f), new Vector3(32f, 22f, 30f));
    }

    static void Layout()
    {
        var materials = EndlessPavilionBuilder.Materials();
        EndlessPavilionBuilder.Atmosphere();

        var copies = new GameObject("Wrap-Around Copies").AddComponent<WrapAroundCopies>();
        copies.period = Vector3.one * PavilionLayout.CellSize;
        copies.range = 1;
        copies.buildOnStart = false; // built once the layout exists

        var generator = new GameObject("Shifting Pavilion").AddComponent<ShiftingPavilion>();
        generator.materials = materials;
        generator.portalMaterial = GravityArenaBuilder.PortalMaterial;
        generator.portalViewMaterial = GravityArenaBuilder.PortalViewMaterialAsset;
        generator.copies = copies;
        generator.fallbackSeed = EndlessPavilionBuilder.LayoutSeed + 1;
    }
}
