using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds a flat ground platform in the open scene.
/// Runs once automatically after this script first compiles; afterwards use
/// Tools > Build Ground Platform to build it again.
/// </summary>
[InitializeOnLoad]
public static class GroundPlatformBuilder
{
    const string AutoRunKey = "ClaudeTest.GroundPlatformBuilder.AutoRan";
    const string GroundName = "Ground";
    const string MaterialPath = "Assets/Materials/Ground.mat";

    static readonly Vector3 Size = new Vector3(50f, 1f, 50f);
    static readonly Color GroundColor = new Color(0.36f, 0.42f, 0.33f);

    static GroundPlatformBuilder()
    {
        if (EditorPrefs.GetBool(AutoRunKey, false)) return;
        EditorPrefs.SetBool(AutoRunKey, true);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Build Ground Platform")]
    public static void Build()
    {
        var existing = GameObject.Find(GroundName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log($"[GroundPlatformBuilder] '{GroundName}' already exists in the scene; selected it.");
            return;
        }

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); // includes a BoxCollider
        ground.name = GroundName;
        ground.transform.position = new Vector3(0f, -Size.y / 2f, 0f); // top surface at y = 0
        ground.transform.localScale = Size;
        GameObjectUtility.SetStaticEditorFlags(ground, (StaticEditorFlags)~0);
        ground.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial();

        Undo.RegisterCreatedObjectUndo(ground, "Build Ground Platform");
        Selection.activeGameObject = ground;
        EditorSceneManager.MarkSceneDirty(ground.scene);

        Debug.Log($"[GroundPlatformBuilder] Built '{GroundName}' ({Size.x}x{Size.z}, top at y=0) in scene '{ground.scene.name}'. Save the scene to keep it.");
    }

    static Material GetOrCreateMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = "Ground" };
        mat.SetColor("_BaseColor", GroundColor);
        mat.color = GroundColor;
        mat.SetFloat("_Smoothness", 0.1f);

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        AssetDatabase.CreateAsset(mat, MaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
